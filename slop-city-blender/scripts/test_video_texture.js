#!/usr/bin/env node
// Execute the pinned Unity uploader and the project guard, including native GPU
// failure boundaries. --framework executes the actual rebuilt export helpers.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const zlib = require('node:zlib');
const root = path.resolve(__dirname, '../..');
const engine = fs.readFileSync(path.join(root, '.tools/Unity-6000.3.26f1/Unity.app/Contents/PlaybackEngines/WebGLSupport/BuildTools/lib/Video.js'), 'utf8');
const plugin = fs.readFileSync(path.join(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityVideoTexture.jslib'), 'utf8');
const playback = fs.readFileSync(path.join(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityVideoPlayback.jslib'), 'utf8');
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
function helper(source, marker) {
  const at = source.indexOf(marker);
  assert.notEqual(at, -1, 'Missing helper ' + marker);
  const start = source.indexOf('function', at), open = source.indexOf('{', start);
  let depth = 0, quote = '', comment = '';
  for (let i = open; i < source.length; i++) {
    const c = source[i], next = source[i + 1];
    if (comment === 'line') { if (c === '\n') comment = ''; continue; }
    if (comment === 'block') { if (c === '*' && next === '/') { comment = ''; i++; } continue; }
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = ''; continue; }
    if (c === '/' && next === '/') { comment = 'line'; i++; continue; }
    if (c === '/' && next === '*') { comment = 'block'; i++; continue; }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '{') depth++;
    else if (c === '}' && --depth === 0) return source.slice(start, i + 1);
  }
  throw new Error('Unclosed helper ' + marker);
}
const engineUpload = helper(engine, 'JS_Video_UpdateToTexture:');
const engineCreate = helper(engine, '$jsVideoCreateTexture2D:');
const flag = process.argv.indexOf('--framework');
const framework = flag < 0 ? null : zlib.gunzipSync(fs.readFileSync(process.argv[flag + 1])).toString();
function environment({ baseline = false, webkit = false } = {}) {
  const calls = [], allocations = [], uploadFailures = [];
  const liveTextures = new Set(), invalidTextureBindings = [];
  const unpackDefaults = new Map([
    ['UNPACK_FLIP_Y_WEBGL', false], ['UNPACK_PREMULTIPLY_ALPHA_WEBGL', false],
    ['UNPACK_COLORSPACE_CONVERSION_WEBGL', 'BROWSER_DEFAULT_WEBGL'], ['UNPACK_ALIGNMENT', 4],
    ['UNPACK_ROW_LENGTH', 0], ['UNPACK_IMAGE_HEIGHT', 0], ['UNPACK_SKIP_PIXELS', 0],
    ['UNPACK_SKIP_ROWS', 0], ['UNPACK_SKIP_IMAGES', 0]
  ]);
  const graphics = {
    activeTexture: 'TEXTURE0', textures: new Map([['TEXTURE0', null]]), samplers: new Map([[0, null]]),
    buffers: new Map([['ARRAY_BUFFER', null], ['PIXEL_UNPACK_BUFFER', null]]),
    unpack: new Map(unpackDefaults), viewport: [0, 0, 1440, 900], drawFramebuffer: null,
    readFramebuffer: null, program: null, vertexArray: null, colorMask: [true, true, true, true],
    enabled: new Set(), vertexArrays: new Map()
  };
  const vertexState = () => {
    if (!graphics.vertexArrays.has(graphics.vertexArray))
      graphics.vertexArrays.set(graphics.vertexArray, { enabled: new Set(), pointers: new Map(), divisors: new Map() });
    return graphics.vertexArrays.get(graphics.vertexArray);
  };
  const textureStorage = new Map();
  const state = { lost: false, now: 0, graphics, draws: [], uploads: [], textureStorage, transferErrors: [] };
  const gl = new Proxy({ canvas: { width: 1440, height: 900 } }, {
    get(target, key) {
      if (key in target) return target[key];
      if (/^[A-Z0-9_]+$/.test(key)) return key;
      return (...args) => {
        calls.push([key, ...args]);
        if (key === 'isContextLost') return state.lost;
        if (key === 'getParameter') {
          if (graphics.unpack.has(args[0])) return graphics.unpack.get(args[0]);
          switch (args[0]) {
            case 'ACTIVE_TEXTURE': return graphics.activeTexture;
            case 'TEXTURE_BINDING_2D': return graphics.textures.get(graphics.activeTexture) || null;
            case 'SAMPLER_BINDING': return graphics.samplers.get(Number(graphics.activeTexture.slice(7))) || null;
            case 'ARRAY_BUFFER_BINDING': return graphics.buffers.get('ARRAY_BUFFER');
            case 'PIXEL_UNPACK_BUFFER_BINDING': return graphics.buffers.get('PIXEL_UNPACK_BUFFER');
            case 'VIEWPORT': return graphics.viewport.slice();
            case 'FRAMEBUFFER_BINDING':
            case 'DRAW_FRAMEBUFFER_BINDING': return graphics.drawFramebuffer;
            case 'READ_FRAMEBUFFER_BINDING': return graphics.readFramebuffer;
            case 'CURRENT_PROGRAM': return graphics.program;
            case 'VERTEX_ARRAY_BINDING': return graphics.vertexArray;
            case 'COLOR_WRITEMASK': return graphics.colorMask.slice();
            default: throw new Error('Unmodeled GL state: ' + args[0]);
          }
        }
        if (key === 'isEnabled') return graphics.enabled.has(args[0]);
        if (key === 'enable') graphics.enabled.add(args[0]);
        if (key === 'disable') graphics.enabled.delete(args[0]);
        if (key === 'activeTexture') graphics.activeTexture = args[0];
        if (key === 'bindSampler') graphics.samplers.set(args[0], args[1]);
        if (key === 'bindBuffer') graphics.buffers.set(args[0], args[1]);
        if (key === 'pixelStorei') graphics.unpack.set(args[0], args[1]);
        if (key === 'viewport') graphics.viewport = args.slice();
        if (key === 'colorMask') graphics.colorMask = args.slice();
        if (key === 'useProgram') graphics.program = args[0];
        if (key === 'bindVertexArray') graphics.vertexArray = args[0];
        if (key === 'bindFramebuffer') {
          if (args[0] !== 'READ_FRAMEBUFFER') graphics.drawFramebuffer = args[1];
          if (args[0] !== 'DRAW_FRAMEBUFFER') graphics.readFramebuffer = args[1];
        }
        if (key === 'enableVertexAttribArray') vertexState().enabled.add(args[0]);
        if (key === 'vertexAttribPointer') vertexState().pointers.set(args[0], { buffer: graphics.buffers.get('ARRAY_BUFFER'), args: args.slice(1) });
        if (key === 'vertexAttribDivisor') vertexState().divisors.set(args[0], args[1]);
        if (key === 'createTexture') {
          const texture = allocations.length ? allocations.shift() : {};
          if (texture) liveTextures.add(texture);
          return texture;
        }
        if (key === 'deleteTexture') {
          liveTextures.delete(args[0]);
          textureStorage.delete(args[0]);
          for (const [unit, texture] of graphics.textures) if (texture === args[0]) graphics.textures.set(unit, null);
        }
        if (key === 'bindTexture') {
          graphics.textures.set(graphics.activeTexture, args[1]);
          if (args[1] && !liveTextures.has(args[1])) invalidTextureBindings.push(args[1]);
        }
        if (key === 'texImage2D' || key === 'texSubImage2D') {
          const texture = graphics.textures.get(graphics.activeTexture);
          state.uploads.push({ activeTexture: graphics.activeTexture,
            texture, sampler: graphics.samplers.get(0),
            unpackBuffer: graphics.buffers.get('PIXEL_UNPACK_BUFFER'), unpack: new Map(graphics.unpack) });
          if (uploadFailures.length) throw uploadFailures.shift();
          const source = args[args.length - 1];
          if (source && typeof source.videoWidth === 'number') {
            // Model native pixel dimensions independently of aspect-corrected
            // videoWidth/videoHeight. WebGL subimage errors do not throw.
            const width = source.sourcePixelWidth || source.videoWidth;
            const height = source.sourcePixelHeight || source.videoHeight;
            const storage = textureStorage.get(texture);
            if (key === 'texImage2D') textureStorage.set(texture, { width, height, frame: source.currentTime });
            else if (!storage || args[2] + width > storage.width || args[3] + height > storage.height)
              state.transferErrors.push({ operation: key, width, height, storage });
            else storage.frame = source.currentTime;
          } else if (key === 'texImage2D' && args.length === 9) {
            textureStorage.set(texture, { width: args[3], height: args[4] });
          }
        }
        if (key === 'drawArrays') state.draws.push({
          viewport: graphics.viewport.slice(), drawFramebuffer: graphics.drawFramebuffer,
          program: graphics.program, vertexArray: graphics.vertexArray, colorMask: graphics.colorMask.slice(),
          enabled: new Set(graphics.enabled), activeTexture: graphics.activeTexture,
          texture: graphics.textures.get('TEXTURE0'), sampler: graphics.samplers.get(0),
          attributes: [...vertexState().enabled].map(index => ({ index,
            pointer: vertexState().pointers.get(index), divisor: vertexState().divisors.get(index) || 0 }))
        });
        if (/^create/.test(key)) return {};
        if (key === 'getAttribLocation') return 0;
        if (key === 'getShaderParameter' || key === 'getProgramParameter') return true;
      };
    }
  });
  const context = {
    GLctx: gl, GL: { textures: { 7: {} } }, videoInstances: {}, DOMException, Date: { now: () => state.now },
    hasSRGBATextures: true, s2lTexture: null, s2lFBO: null, s2lVBO: null,
    s2lProgram: null, s2lVertexPositionNDC: null, slopCityVideoVertexArray: null,
    Module: {}, navigator: { userAgent: 'Mozilla/5.0 desktop' },
    jsIsWebkit: () => webkit,
    clearInterval: interval => calls.push(['clearInterval', interval]),
    clearTimeout: timer => calls.push(['clearTimeout', timer]),
    jsVideoRemovePendingBlockedVideo: video => calls.push(['removePending', video]),
    LibraryManager: { library: {} }, mergeInto: (target, values) => Object.assign(target, values)
  };
  liveTextures.add(context.GL.textures[7]);
  vm.createContext(context);
  vm.runInContext('jsVideoCreateTexture2D=' + engineCreate + '; baselineUpload=' + engineUpload + ';', context);
  if (baseline) context.upload = context.baselineUpload;
  else if (framework) {
    vm.runInContext('jsVideoCreateTexture2D=' + helper(framework, 'function jsVideoCreateTexture2D(')
      + '; slopCityPrepareLinearVideo=' + helper(framework, 'function slopCityPrepareLinearVideo(')
      + '; slopCityCaptureVideoGraphicsState=' + helper(framework, 'function slopCityCaptureVideoGraphicsState(')
      + '; slopCityRestoreVideoGraphicsState=' + helper(framework, 'function slopCityRestoreVideoGraphicsState(')
      + '; slopCityNativeVideoUpdateToTexture=' + helper(framework, 'function slopCityNativeVideoUpdateToTexture(')
      + '; slopCityWebkitVideoUpload=' + helper(framework, 'function slopCityWebkitVideoUpload(')
      + '; slopCityReleaseVideoTexture=' + helper(framework, 'function slopCityReleaseVideoTexture(')
      + '; upload=' + helper(framework, 'function _JS_Video_UpdateToTexture(') + ';', context);
  } else {
    context.LibraryManager.library.JS_Video_UpdateToTexture = context.baselineUpload;
    context.LibraryManager.library.JS_Video_UpdateToTexture__deps = [
      '$videoInstances', '$jsVideoCreateTexture2D', '$hasSRGBATextures', '$s2lTexture',
      '$s2lFBO', '$s2lVBO', '$s2lProgram', '$s2lVertexPositionNDC'
    ];
    vm.runInContext(plugin, context);
    for (const [name, value] of Object.entries(context.LibraryManager.library)) {
      if (name.startsWith('$') && !name.includes('__')) context[name.slice(1)] = value;
    }
    context.upload = context.LibraryManager.library.JS_Video_UpdateToTexture;
    check(context.slopCityNativeVideoUpdateToTexture === context.baselineUpload,
      'The project aliases the exact pinned native uploader, including its linear shader conversion');
    vm.runInContext(context.LibraryManager.library.$slopCityPauseVideoForGraphicsLoss__postset, context);
  }
  if (!baseline) {
    vm.runInContext('slopCityUsesMobileVideo=' + (framework ? helper(framework, 'function slopCityUsesMobileVideo(')
      : helper(playback, '$slopCityUsesMobileVideo:')) + ';', context);
    vm.runInContext('slopCityClearVideoAttemptTimer=' + (framework ? helper(framework, 'function slopCityClearVideoAttemptTimer(')
      : helper(playback, '$slopCityClearVideoAttemptTimer:')) + ';', context);
    vm.runInContext('slopCityCancelVideoPictureWatch=' + (framework ? helper(framework, 'function slopCityCancelVideoPictureWatch(')
      : helper(playback, '$slopCityCancelVideoPictureWatch:')) + ';', context);
    vm.runInContext('_JS_Video_Pause=' + (framework ? helper(framework, 'function _JS_Video_Pause(')
      : helper(playback, 'JS_Video_Pause:')) + ';', context);
    if (framework) {
      vm.runInContext('slopCityPauseVideoForGraphicsLoss='
        + helper(framework, 'function slopCityPauseVideoForGraphicsLoss(') + ';', context);
      check(framework.includes('Module["slopCityPauseVideoForGraphicsLoss"]')
        || framework.includes("Module['slopCityPauseVideoForGraphicsLoss']"), 'Compiled helper is published to the launcher Module');
      context.Module.slopCityPauseVideoForGraphicsLoss = context.slopCityPauseVideoForGraphicsLoss;
    }
  }
  context.videoInstances[1] = { src: 'https://slopcity.org/StreamingAssets/TikTok/media/first.mp4',
    readyState: 4, isReady: true, videoWidth: 640, videoHeight: 360, currentTime: 1, seeking: false,
    pause() { this.paused = true; } };
  return { context, calls, allocations, uploadFailures, state, liveTextures, invalidTextureBindings,
    video: context.videoInstances[1], run: linear => context.upload(1, 7, linear) };
}
const old = environment({ baseline: true });
old.allocations.push(null);
assert.throws(() => old.run(false), { name: 'TypeError' }); checks++;
check(old.calls.some(call => call[0] === 'pixelStorei' && call[2] === true),
  'Pinned first-frame null allocation throws after setting unpack flip');
check(old.context.GL.textures[7] && !old.liveTextures.has(old.context.GL.textures[7]),
  'Pinned failed allocation leaves a deleted destination in the engine texture table');

function comparableCalls(calls) {
  // Compare the pinned texture/upload and conversion-draw work. State setup and
  // restoration intentionally differ; their actual values are asserted below.
  // Ignore only project bookkeeping on the shared decoded-image argument.
  const operations = new Set(['createTexture', 'deleteTexture', 'texParameteri', 'texImage2D', 'texSubImage2D',
    'framebufferTexture2D', 'enableVertexAttribArray', 'vertexAttribPointer', 'drawArrays']);
  return JSON.parse(JSON.stringify(calls.filter(call => operations.has(call[0])),
    (key, value) => key.startsWith('slopCity') ? undefined : value));
}

for (const linear of [false, true]) {
  const native = environment({ baseline: true }), guarded = environment();
  check(native.run(linear) === true && guarded.run(linear) === true, 'Healthy ' + (linear ? 'linear' : 'gamma') + ' upload succeeds');
  // Normalize the two isolated VM realms' typed-array prototypes for comparison.
  assert.deepEqual(comparableCalls(guarded.calls), comparableCalls(native.calls)); checks++;
  check(guarded.video.lastUpdateTextureTime === 1, 'Successful upload records frame time');
  check(guarded.run(linear) === false, 'An unchanged frame is not uploaded twice');
  guarded.video.currentTime = 2;
  check(guarded.run(linear) === true, 'Next frame uploads to the existing texture');
  guarded.video.videoWidth = 800; guarded.video.currentTime = 3;
  check(guarded.run(linear) === true && guarded.video.previousUploadedWidth === 800, 'Clip dimension changes remain supported');
  if (linear) check(guarded.calls.some(call => call[0] === 'drawArrays'), 'Linear conversion still draws through Unity shader');
}

const conversionCapabilities = ['BLEND', 'CULL_FACE', 'DEPTH_TEST', 'STENCIL_TEST',
  'SCISSOR_TEST', 'RASTERIZER_DISCARD', 'SAMPLE_COVERAGE', 'SAMPLE_ALPHA_TO_COVERAGE'];
function seedGraphicsState(env, destinationOnZero = false) {
  const g = env.state.graphics, gl = env.context.GLctx;
  g.textures.set('TEXTURE0', destinationOnZero ? env.context.GL.textures[7] : gl.createTexture());
  g.textures.set('TEXTURE3', env.context.GL.textures[7]);
  g.activeTexture = 'TEXTURE3';
  g.samplers.set(0, {}); g.samplers.set(3, {});
  g.buffers.set('ARRAY_BUFFER', {}); g.buffers.set('PIXEL_UNPACK_BUFFER', {});
  g.drawFramebuffer = {}; g.readFramebuffer = {}; g.program = {}; g.vertexArray = {};
  g.viewport = [9, 11, 320, 180]; g.colorMask = [false, true, false, false];
  g.enabled = new Set([...conversionCapabilities, 'DITHER']);
  g.unpack = new Map([
    ['UNPACK_FLIP_Y_WEBGL', true], ['UNPACK_PREMULTIPLY_ALPHA_WEBGL', true],
    ['UNPACK_COLORSPACE_CONVERSION_WEBGL', 'NONE'], ['UNPACK_ALIGNMENT', 1],
    ['UNPACK_ROW_LENGTH', 1024], ['UNPACK_IMAGE_HEIGHT', 1024], ['UNPACK_SKIP_PIXELS', 2],
    ['UNPACK_SKIP_ROWS', 3], ['UNPACK_SKIP_IMAGES', 1]
  ]);
  gl.enableVertexAttribArray(0); gl.enableVertexAttribArray(4);
  gl.vertexAttribPointer(0, 4, 'FLOAT', false, 16, 0); gl.vertexAttribDivisor(0, 3);
  return { ...g, destination: env.context.GL.textures[7], textures: new Map(g.textures),
    samplers: new Map(g.samplers), buffers: new Map(g.buffers), unpack: new Map(g.unpack),
    viewport: g.viewport.slice(), colorMask: g.colorMask.slice(), enabled: new Set(g.enabled),
    originalPointer: g.vertexArrays.get(g.vertexArray).pointers.get(0) };
}
function checkGraphicsRestored(env, before, label) {
  const g = env.state.graphics;
  check(g.activeTexture === before.activeTexture, label + ' restores the active texture unit');
  for (const [unit, texture] of before.textures)
    check(g.textures.get(unit) === (texture === before.destination ? env.context.GL.textures[7] : texture),
      label + ' restores ' + unit + ' with the live destination replacement when needed');
  for (const [unit, sampler] of before.samplers)
    check(g.samplers.get(unit) === sampler, label + ' restores sampler ' + unit);
  for (const [target, buffer] of before.buffers)
    check(g.buffers.get(target) === buffer, label + ' restores ' + target);
  for (const name of ['drawFramebuffer', 'readFramebuffer', 'program', 'vertexArray'])
    check(g[name] === before[name], label + ' restores ' + name);
  assert.deepEqual(g.viewport, before.viewport, label + ' restores the actual low-resolution viewport'); checks++;
  assert.deepEqual(g.colorMask, before.colorMask, label + ' restores every color-write channel'); checks++;
  assert.deepEqual(g.enabled, before.enabled, label + ' restores raster capabilities including scissor'); checks++;
  assert.deepEqual(g.unpack, before.unpack, label + ' restores every unpack setting'); checks++;
  const attributes = g.vertexArrays.get(before.vertexArray);
  check(attributes.pointers.get(0) === before.originalPointer && attributes.divisors.get(0) === 3
    && attributes.enabled.size === 2 && attributes.enabled.has(4), label + ' preserves Unity VAO attributes and divisors');
  check(env.invalidTextureBindings.length === 0, label + ' never rebinds a deleted destination');
}
for (const webkit of [false, true]) {
  for (const linear of [false, true]) {
    // Cover both an unrelated unit-zero binding and a replaced destination
    // simultaneously bound on unit zero and the original active unit.
    const env = environment({ webkit }), before = seedGraphicsState(env, webkit);
    check(env.run(linear) === true, 'Dirty WebGL2 state permits the selected upload/conversion path');
    checkGraphicsRestored(env, before, 'Successful upload');
    for (const upload of env.state.uploads) {
      check(upload.activeTexture === 'TEXTURE0' && upload.sampler === null && upload.unpackBuffer === null,
        'Every pixel upload uses texture unit zero without inherited sampler or unpack buffer');
      check(upload.unpack.get('UNPACK_FLIP_Y_WEBGL') === true
        && upload.unpack.get('UNPACK_PREMULTIPLY_ALPHA_WEBGL') === false
        && upload.unpack.get('UNPACK_COLORSPACE_CONVERSION_WEBGL') === 'BROWSER_DEFAULT_WEBGL'
        && upload.unpack.get('UNPACK_ALIGNMENT') === 4
        && ['UNPACK_ROW_LENGTH', 'UNPACK_IMAGE_HEIGHT', 'UNPACK_SKIP_PIXELS', 'UNPACK_SKIP_ROWS', 'UNPACK_SKIP_IMAGES']
          .every(name => upload.unpack.get(name) === 0), 'Pixel uploads use the intended flip and clean unpack layout');
    }
    if (linear) {
      check(env.state.draws.length === 1, 'One decoded frame performs one linear conversion draw');
      const draw = env.state.draws[0];
      check(draw.activeTexture === 'TEXTURE0' && draw.sampler === null
        && draw.texture === env.video.slopCityLinearVideoTexture, 'Conversion samples the owned scratch on clean unit zero');
      check(draw.drawFramebuffer === env.context.s2lFBO && draw.program === env.context.s2lProgram
        && draw.vertexArray === env.context.slopCityVideoVertexArray, 'Conversion uses its private framebuffer, shader and VAO');
      assert.deepEqual(draw.viewport, [0, 0, 640, 360], 'Conversion viewport covers the decoded picture'); checks++;
      check(draw.colorMask.every(Boolean) && conversionCapabilities.every(cap => !draw.enabled.has(cap)),
        'Conversion writes every color channel without inherited raster rejection or blending');
      check(draw.attributes.length === 1 && draw.attributes[0].index === env.context.s2lVertexPositionNDC
        && draw.attributes[0].divisor === 0 && draw.attributes[0].pointer.buffer === env.context.s2lVBO,
        'Conversion uses only its non-instanced vertex attribute');
    } else check(env.state.draws.length === 0, 'Gamma uploads do not add a conversion draw');
    const afterUpload = env.calls.length;
    check(env.run(linear) === false && !env.calls.slice(afterUpload).some(call => call[0] === 'getParameter'),
      'Duplicate frames skip graphics-state queries');
    for (const failure of ['allocation', 'frame']) {
      const failed = environment({ webkit }), failedBefore = seedGraphicsState(failed);
      if (failure === 'allocation') failed.allocations.push(null);
      else failed.uploadFailures.push(new DOMException('Current frame is unavailable', 'InvalidStateError'));
      check(failed.run(linear) === false, failure + ' is contained with dirty graphics state');
      checkGraphicsRestored(failed, failedBefore, failure + ' failure');
    }
  }
}

const lost = environment(); lost.state.lost = true;
check(lost.run(false) === false && !lost.calls.some(call => call[0] === 'createTexture'), 'Lost context skips native texture upload before allocation');
check(lost.video.lastUpdateTextureTime === undefined, 'Lost context does not consume frame time');
const gone = environment(); delete gone.context.videoInstances[1];
check(gone.run(false) === false, 'Destroyed decoder safely skips a late upload');
for (const device of ['iPhone', 'iPad']) {
  for (const linear of [false, true]) {
    const ios = environment({ webkit: true });
    ios.context.navigator.userAgent = 'Mozilla/5.0 (' + device + '; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 CriOS/130.0';
    ios.video.readyState = 1; ios.video.isReady = false;
    ios.video.HAVE_METADATA = 1; ios.video.HAVE_FUTURE_DATA = 3; ios.video.HAVE_ENOUGH_DATA = 4;
    // Production iOS disables RVFC. Even a sticky loadeddata flag must not
    // authorize publication while only metadata is available for this frame.
    ios.video.isLoaded = true; ios.video.slopCityDecodedFrame = 1;
    vm.runInContext('nativeIsReady=' + helper(engine, 'JS_Video_IsReady:') + ';', ios.context);
    check(ios.context.nativeIsReady(1) === true, 'Pinned ' + device + ' preparation still accepts HAVE_METADATA');
    const beforeMetadata = ios.calls.length, retainedTexture = ios.context.GL.textures[7];
    check(ios.run(linear) === false, device + ' metadata alone cannot publish a picture');
    check(ios.calls.slice(beforeMetadata).every(call => call[0] === 'isContextLost')
      && ios.context.GL.textures[7] === retainedTexture,
      device + ' metadata rejection skips graphics-state queries, allocation and upload');
    check(['lastUpdateTextureTime', 'previousUploadedWidth', 'previousUploadedHeight',
      'slopCityUploadedDecodedFrame', 'slopCityUploadedTextureId', 'slopCityUploadedPictureSource']
      .every(name => !(name in ios.video)), device + ' metadata rejection leaves first-picture deduplication untouched');
    ios.video.readyState = 2; ios.video.isLoaded = false;
    check(ios.run(linear) === true, device + ' current-frame data uploads without waiting for a loadeddata event');
    check(ios.video.lastUpdateTextureTime === 1 && ios.video.slopCityUploadedDecodedFrame === 1
      && ios.video.slopCityUploadedPictureSource === ios.video.src,
      device + ' successful first picture records its exact source, time and decoded frame');
    check(ios.calls.filter(call => call[0] === 'texImage2D' && call.length === 7 && call[6] === ios.video).length === 1
      && !ios.calls.some(call => call[0] === 'texSubImage2D'),
      device + ' current-data upload lets the video source define pixel storage');
    ios.context.GL.textures[8] = ios.context.GLctx.createTexture();
    check(ios.context.upload(1, 8, linear) === true,
      device + ' same current frame transfers to a changed destination');
    check(ios.video.slopCityUploadedTextureId === 8
      && ios.video.slopCityUploadedTexture === ios.context.GL.textures[8]
      && ios.video.slopCityUploadedPictureSource === ios.video.src,
      device + ' transferred picture retains source identity and owns its new destination');
  }
}
for (const linear of [false, true]) {
  const ios = environment({ webkit: true });
  ios.context.navigator.userAgent = 'Mozilla/5.0 (iPhone; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 CriOS/130.0';
  ios.context.hasSRGBATextures = false;
  // The failing Web duet assets are 480x426 with SAR639:640. Model a
  // display-width rounding mismatch; this is not a measured device reading.
  ios.video.videoWidth = 479; ios.video.videoHeight = 426;
  ios.video.sourcePixelWidth = 480; ios.video.sourcePixelHeight = 426;
  check(ios.run(linear) === true, 'Mobile non-square-pixel frame uploads in the selected conversion mode');
  const uploadedTexture = linear ? ios.video.slopCityLinearVideoTexture : ios.context.GL.textures[7];
  const storage = ios.state.textureStorage.get(uploadedTexture);
  check(storage.width === 480 && storage.height === 426 && storage.frame === 1
    && ios.state.transferErrors.length === 0,
    'Native source dimensions define storage without silent undersized-subimage failure');
  if (linear) {
    const destination = ios.state.textureStorage.get(ios.context.GL.textures[7]);
    check(destination.width === 479 && destination.height === 426,
      'Linear conversion retains the aspect-corrected destination dimensions');
    assert.deepEqual(ios.state.draws[0].viewport, [0, 0, 479, 426],
      'Linear conversion fits the native source pixels into the display-sized output'); checks++;
  }
  ios.video.currentTime = 2;
  check(ios.run(linear) === true, 'Later non-square-pixel frames continue uploading');
  check((linear ? ios.video.slopCityLinearVideoTexture : ios.context.GL.textures[7]) === uploadedTexture
    && ios.state.textureStorage.get(uploadedTexture).frame === 2,
    'Subsequent mobile frames reuse the texture object while defining source-sized storage');
  check(ios.calls.filter(call => call[0] === 'texImage2D' && call.length === 7 && call[6] === ios.video).length === 1
    && ios.calls.filter(call => call[0] === 'texSubImage2D').length === 1 && ios.state.transferErrors.length === 0,
    'Stable mobile frames reuse source-sized storage with subimages instead of reallocating every frame');
}
for (const readyState of [0, 1]) {
  const waiting = environment(); waiting.video.readyState = readyState;
  waiting.uploadFailures.push(new DOMException('Current frame is unavailable', 'InvalidStateError'));
  check(waiting.run(false) === false && waiting.video.lastUpdateTextureTime === undefined,
    'Unavailable frame at readiness ' + readyState + ' is contained without overriding platform readiness');
  waiting.video.readyState = 2;
  check(waiting.run(false) === true, 'Current frame can upload when readiness returns');
}
for (const linear of [false, true]) {
  for (const failure of ['allocation', 'frame']) {
    const env = environment();
    if (failure === 'allocation') env.allocations.push(null);
    else env.uploadFailures.push(new DOMException('Current frame is unavailable', 'InvalidStateError'));
    check(env.run(linear) === false, failure + ' returns false instead of escaping Unity');
    check(env.video.lastUpdateTextureTime === undefined, failure + ' restores frame time for retry');
    check(!('slopCityUploadedPictureSource' in env.video), failure + ' does not publish a first-picture source marker');
    check(env.video.previousUploadedWidth === undefined && env.video.previousUploadedHeight === undefined, failure + ' clears prematurely recorded dimensions');
    check(env.calls.some(call => call[0] === 'pixelStorei' && call[2] === false), failure + ' restores unpack flip');
    check(failure === 'allocation' ? env.context.GL.textures[7] === null : env.liveTextures.has(env.context.GL.textures[7]),
      failure + ' leaves no deleted destination handle in the texture table');
    env.context.GLctx.bindTexture('TEXTURE_2D', env.context.GL.textures[7]);
    check(env.invalidTextureBindings.length === 0, failure + ' allows the engine to bind its table entry safely');
    if (failure === 'allocation') {
      check(env.run(linear) === false, 'Allocation retry respects the existing cooldown');
      env.state.now += 5001;
    }
    check(env.run(linear) === true, failure + ' recovers on the same valid frame');
    check(env.liveTextures.has(env.context.GL.textures[7]) && env.invalidTextureBindings.length === 0,
      failure + ' retry replaces the table with a live destination');
  }
}
const scratch = environment(); scratch.allocations.push({}, null);
check(scratch.run(true) === false, 'Null linear-conversion scratch texture is contained');
check(scratch.liveTextures.has(scratch.context.GL.textures[7]), 'Scratch allocation failure preserves the new live destination');
scratch.state.now += 5001;
check(scratch.run(true) === true, 'Linear scratch allocation can recover');
const existingScratch = environment();
existingScratch.video.previousUploadedWidth = 640; existingScratch.video.previousUploadedHeight = 360;
const retained = existingScratch.context.GL.textures[7]; existingScratch.allocations.push(null);
check(existingScratch.run(true) === false, 'Same-size linear upload handles scratch allocation failure');
check(existingScratch.context.GL.textures[7] === retained && existingScratch.liveTextures.has(retained),
  'Same-size scratch failure preserves the unchanged live destination');
existingScratch.state.now += 5001;
check(existingScratch.run(true) === true && existingScratch.invalidTextureBindings.length === 0,
  'Same-size scratch failure retries with valid GPU objects');
for (const error of [new Error('Unrelated failure'), new TypeError('Unrelated bug')]) {
  const env = environment(); env.uploadFailures.push(error);
  assert.throws(() => env.run(false), e => e === error); checks++;
}
const absent = { LibraryManager: { library: {} } }; vm.createContext(absent);
assert.throws(() => vm.runInContext(plugin, absent), /requires Unity Video.js first/); checks++;
const silent = environment();
silent.video.pause = () => { silent.video.paused = true; };
silent.video.slopCityPlaybackIntent = {}; silent.video.loopEndPollInterval = 'lounge-loop';
const second = silent.context.videoInstances[2] = { slopCityPlaybackIntent: {}, pause() { this.paused = true; } };
silent.context.Module.slopCityPauseVideoForGraphicsLoss();
check(silent.video.paused && second.paused, 'Graphics loss pauses every native decoder');
check(!silent.video.slopCityPlaybackIntent && !second.slopCityPlaybackIntent, 'Graphics loss invalidates all pending playback intents');
check(silent.calls.filter(call => call[0] === 'removePending').length === 2, 'Graphics loss cancels each pending gesture retry through the actual pause bridge');
check(silent.calls.some(call => call[0] === 'clearInterval' && call[1] === 'lounge-loop'), 'Graphics loss clears the lounge loop timer');
silent.context.Module.slopCityPauseVideoForGraphicsLoss();
check(silent.video.paused && second.paused, 'Repeated graphics-loss pause remains safe');
silent.context.videoInstances = {};
silent.context.Module.slopCityPauseVideoForGraphicsLoss();
check(true, 'Graphics loss before scene playback has no decoders to pause');
// Exercise the new ownership boundaries using decoded-frame identity, including
// a changed target/mode and mixed-aspect native/WebKit decoders.
for (const webkit of [false, true]) {
  for (const linear of [false, true]) {
    const env = environment({ webkit });
    env.video.slopCityDecodedFrame = 1;
    check(env.run(linear) === true, 'Initial decoded frame uploads for selected browser path/mode');
    check(env.video.slopCityUploadedDecodedFrame === 1
      && env.video.slopCityUploadedTextureId === 7
      && env.video.slopCityUploadedLinearMode === linear
      && env.video.slopCityUploadedPictureSource === env.video.src,
      'Successful upload owns the exact source, frame, target and conversion mode');
    const afterFirst = env.calls.filter(call => ['texImage2D', 'texSubImage2D'].includes(call[0])).length;
    env.video.currentTime = 1.01;
    check(env.run(linear) === false, 'Clock advance without a decoded frame performs no new upload');
    check(env.calls.filter(call => ['texImage2D', 'texSubImage2D'].includes(call[0])).length === afterFirst,
      'Frame dedup prevents GPU work rather than merely suppressing the return value');
    for (let frame = 2; frame <= 20; frame++) {
      env.video.currentTime = frame; env.video.slopCityDecodedFrame = frame;
      check(env.run(linear) === true, 'New decoded frames continue to upload');
    }
    if (webkit) {
      check(env.calls.filter(call => call[0] === 'texSubImage2D').length === 19,
        'Desktop WebKit reuses defined same-size storage for all later decoded frames');
      check(env.calls.filter(call => call[0] === 'texImage2D').length === (linear ? 2 : 1),
        'Desktop WebKit defines only its original destination/scratch storage');
    } else {
      check(!env.calls.some(call => call[0] === 'texSubImage2D'),
        'Chromium retains the pinned compatible native upload path');
    }
    env.context.GL.textures[8] = env.context.GLctx.createTexture();
    check(env.context.upload(1, 8, linear) === true,
      'The same decoded frame uploads when its physical display target changes');
    check(env.video.slopCityUploadedTextureId === 8,
      'New physical target becomes the authoritative frame destination');
    check(env.context.upload(1, 8, !linear) === true,
      'The same decoded frame uploads when linear conversion mode changes');
    check(env.video.slopCityUploadedLinearMode === !linear,
      'Conversion mode change updates ownership instead of retaining stale output');
  }
  const env = environment({ webkit });
  env.video.slopCityDecodedFrame = 1;
  check(env.run(true) === true, 'Landscape decoder creates its linear scratch');
  const landscape = env.video.slopCityLinearVideoTexture;
  env.context.GL.textures[9] = env.context.GLctx.createTexture();
  const portrait = env.context.videoInstances[2] = { src: 'https://slopcity.org/StreamingAssets/TikTok/media/portrait.mp4',
    readyState: 4, isReady: true,
    videoWidth: 360, videoHeight: 640, currentTime: 1, seeking: false, slopCityDecodedFrame: 1 };
  check(env.context.upload(2, 9, true) === true, 'Portrait decoder creates independent linear scratch');
  const portraitScratch = portrait.slopCityLinearVideoTexture;
  check(landscape && portraitScratch && landscape !== portraitScratch,
    'Landscape and portrait scratch textures have distinct decoder owners');
  for (let frame = 2; frame <= 20; frame++) {
    env.video.currentTime = portrait.currentTime = frame;
    env.video.slopCityDecodedFrame = portrait.slopCityDecodedFrame = frame;
    check(env.context.upload(1, 7, true) && env.context.upload(2, 9, true),
      'Alternating mixed-aspect decoded frames continue drawing both displays');
    check(env.video.slopCityLinearVideoTexture === landscape
      && portrait.slopCityLinearVideoTexture === portraitScratch,
      'Alternating display updates retain each decoder scratch identity');
    check(env.context.s2lTexture === null, 'Each upload restores the native shared scratch slot');
  }
  env.context.slopCityReleaseVideoTexture(portrait);
  check(!env.liveTextures.has(portraitScratch) && env.liveTextures.has(landscape),
    'Portrait retirement releases only its owned native scratch');
  check(!('slopCityLinearVideoTexture' in portrait) && !('slopCityUploadedTexture' in portrait)
    && !('slopCityUploadedPictureSource' in portrait), 'Retirement drops source/frame/texture ownership references');
  env.context.slopCityReleaseVideoTexture(portrait);
  check(env.liveTextures.has(landscape), 'Repeated retirement preserves the other live decoder');
  env.context.slopCityReleaseVideoTexture(env.video);
  check(!env.liveTextures.has(landscape), 'Final decoder retirement releases remaining scratch');
}
console.log(JSON.stringify({ passed: true, checks, framework: !!framework,
  scope: 'Pinned native upload and failure paths, mobile source-sized pixel storage and current-data publication, WebGL2 state isolation/restoration, decoded-frame dedup/target-mode changes, independent mixed-aspect scratch retirement, and desktop WebKit storage reuse; no physical iPhone validation' }));
