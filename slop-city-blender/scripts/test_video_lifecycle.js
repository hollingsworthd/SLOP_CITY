#!/usr/bin/env node
// Execute the pinned create/destroy helpers and their project replacement.
// Native callback/source ownership is simulated; this is not browser RAM QA.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import zlib from 'node:zlib';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const __dirname = path.dirname(fileURLToPath(import.meta.url));
let root = path.resolve(__dirname, '..');
while (!fs.existsSync(path.join(root, '.tools/Unity-6000.3.26f1/Unity.app/Contents/PlaybackEngines/WebGLSupport/BuildTools/lib/Video.js'))) {
  const parent = path.dirname(root);
  assert.notEqual(parent, root, 'Pinned independent Unity tools must be installed in this workspace');
  root = parent;
}
const engine = fs.readFileSync(path.join(root, '.tools/Unity-6000.3.26f1/Unity.app/Contents/PlaybackEngines/WebGLSupport/BuildTools/lib/Video.js'), 'utf8');
const lifecycle = fs.readFileSync(path.resolve(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityVideoLifecycle.jslib'), 'utf8');
const playback = fs.readFileSync(path.resolve(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityVideoPlayback.jslib'), 'utf8');
const texture = fs.readFileSync(path.resolve(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityVideoTexture.jslib'), 'utf8');
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
check(crypto.createHash('sha256').update(helper(engine, 'JS_Video_Create:')).digest('hex')
  === '7b925fcc9c3baec9246320ca5e2a92468ed586be7b8d5a371e165cc142ccd057',
  'Copied create semantics explicitly match the audited Unity 6000.3.26f1 helper; an engine upgrade requires review');
let framework;
if (process.argv.length > 2) {
  assert.equal(process.argv[2], '--framework');
  const bytes = fs.readFileSync(process.argv[3]);
  framework = (bytes[0] === 0x1f && bytes[1] === 0x8b ? zlib.gunzipSync(bytes) : bytes).toString('utf8');
}
function environment({ baseline = false, browser = 'Chrome', frames = true, os = 'MacOS', platform = 'MacIntel', maxTouchPoints = 0, audioPolicy = false } = {}) {
  const nativeFrames = new Map(), listeners = new Map(), timers = new Map(), gestures = new Map();
  const videos = [], retiredTextures = [];
  // Model per-element permission, not a page-wide flag: a fresh native element
  // needs a real gesture, while source changes preserve its existing grant.
  const nativeAudioGrants = new WeakSet();
  const videoPrototype = {};
  let nextFrame = 1, nextTimer = 1;
  if (frames) {
    videoPrototype.requestVideoFrameCallback = function (fn) {
      const id = nextFrame++; nativeFrames.set(id, { video: this, fn }); return id;
    };
    videoPrototype.cancelVideoFrameCallback = id => nativeFrames.delete(id);
  }
  const context = vm.createContext({
    navigator: { userAgent: browser === 'Chrome' ? 'AppleWebKit Chrome/139' : browser === 'Safari' ? 'AppleWebKit Safari/107' : browser,
      platform, maxTouchPoints },
    Module: { SystemInfo: { browser, browserVersion: 107, os } },
    videoInstances: {}, videoInstanceIdCounter: 0, hasSRGBATextures: null,
    GLctx: { deleteTexture(texture) { retiredTextures.push(texture); } },
    jsVideoPendingBlockedVideos: {},
    UTF8ToString: url => url,
    window: {
      addEventListener(type, fn) { gestures.set(type, fn); },
      removeEventListener(type) { gestures.delete(type); }
    },
    document: {
      dispatchEvent() {},
      createElement(tag) {
        assert.equal(tag, 'video');
        const video = Object.assign(Object.create(videoPrototype), { style: {}, attributes: {}, isLoaded: false, loads: 0, pauses: 0,
          readyState: 0, currentTime: 0, paused: true, ended: false, volume: 1,
          buffered: { length: 0, end() { throw new RangeError('No buffered range'); } },
          setAttribute(name, value) { this.attributes[name] = value; },
          removeAttribute(name) { delete this.attributes[name]; if (name === 'src') this.src = ''; },
          addEventListener(name, fn) { listeners.set(this, { name, fn }); },
          removeEventListener(name, fn) { if (listeners.get(this)?.fn === fn) listeners.delete(this); },
          pause() { this.pauses++; this.paused = true; }, load() {
            this.loads++;
            if (audioPolicy) { this.currentTime = this.readyState = 0; this.ended = false; }
          }, play() {
            this.playCalls = (this.playCalls || 0) + 1;
            if (audioPolicy && !this.muted && !nativeAudioGrants.has(this)) {
              if (!context.fixtureGesture) return Promise.reject(Object.assign(new Error('Gesture required'), { name: 'NotAllowedError' }));
              nativeAudioGrants.add(this);
            }
            this.paused = false; return Promise.resolve();
          }
        });
        videos.push(video); return video;
      }
    },
    setTimeout(fn) { const id = nextTimer++; timers.set(id, fn); return id; },
    clearTimeout(id) { timers.delete(id); }, clearInterval(id) { timers.delete(id); },
    _JS_Video_SetLoop() {},
    LibraryManager: { library: {} }, mergeInto(target, fields) { Object.assign(target, fields); }
  });
  vm.runInContext('jsIsWebkit=' + helper(engine, '$jsIsWebkit:') + ';'
    + 'jsVideoAllAudioTracksAreDisabled=' + helper(engine, '$jsVideoAllAudioTracksAreDisabled:') + ';'
    + 'jsVideoAttemptToPlayBlockedVideos=' + helper(engine, '$jsVideoAttemptToPlayBlockedVideos:') + ';'
    + 'jsVideoRemovePendingBlockedVideo=' + helper(engine, '$jsVideoRemovePendingBlockedVideo:') + ';'
    + '_JS_Video_Create=' + helper(engine, 'JS_Video_Create:') + ';'
    + '_JS_Video_Seek=' + helper(engine, 'JS_Video_Seek:') + ';'
    + '_JS_Video_IsReady=' + helper(engine, 'JS_Video_IsReady:') + ';'
    + 'nativeVideoPlay=' + helper(engine, 'JS_Video_Play:') + ';'
    + '_JS_Video_Destroy=' + helper(engine, 'JS_Video_Destroy:') + ';', context);
  if (!baseline) {
    vm.runInContext('slopCityReleaseVideoTexture=' + (framework
      ? helper(framework, 'function slopCityReleaseVideoTexture(')
      : helper(texture, '$slopCityReleaseVideoTexture:')) + ';', context);
    context.LibraryManager.library.JS_Video_Destroy = context._JS_Video_Destroy;
    context.LibraryManager.library.JS_Video_Destroy__deps = ['$jsVideoRemovePendingBlockedVideo'];
    // The lifecycle plugin wraps both native functions; register the actual
    // pinned readiness helper just as Emscripten's Video.js registration does.
    context.LibraryManager.library.JS_Video_IsReady = context._JS_Video_IsReady;
    context.LibraryManager.library.JS_Video_IsReady__deps = ['$videoInstances', '$jsIsWebkit'];
    vm.runInContext(playback, context);
    vm.runInContext(lifecycle, context);
    check(context.LibraryManager.library.JS_Video_Create__deps.includes('$slopCityDisposeOwnedVideoDecoders')
      && context.LibraryManager.library.$slopCityDisposeOwnedVideoDecoders__deps.includes('JS_Video_Destroy')
      && !context.LibraryManager.library.JS_Video_Destroy__deps.includes('$slopCityDisposeOwnedVideoDecoders'),
      'Create links synchronous cleanup/postset without a destroy dependency cycle');
    for (const [key, value] of Object.entries(context.LibraryManager.library)) {
      if (key.includes('__')) continue;
      context[key.startsWith('$') ? key.slice(1) : '_' + key] = value;
    }
    if (framework) {
      for (const key of Object.keys(context.LibraryManager.library)) {
        if (key.includes('__') || typeof context.LibraryManager.library[key] !== 'function') continue;
        const name = key.startsWith('$') ? key.slice(1) : '_' + key;
        vm.runInContext(name + '=' + helper(framework, 'function ' + name + '(') + ';', context);
      }
      check(framework.includes('Module["slopCityDisposeOwnedVideoDecoders"]')
        || framework.includes("Module['slopCityDisposeOwnedVideoDecoders']"),
        'Compiled synchronous owned-decoder cleanup is published to launcher Module');
    }
    vm.runInContext(context.LibraryManager.library.$slopCityDisposeOwnedVideoDecoders__postset, context);
  }
  function fire(id) {
    const entry = nativeFrames.get(id); assert.ok(entry);
    nativeFrames.delete(id); entry.fn();
  }
  return { context, videos, nativeFrames, listeners, timers, gestures, retiredTextures, videoPrototype, fire, nativeAudioGrants,
    create: (source = 'StreamingAssets/clip.mp4') => context._JS_Video_Create(source),
    gesture(type = 'touchend') {
      context.fixtureGesture = true;
      try { gestures.get(type)?.({ type, isTrusted: true, pointerType: 'touch' }); }
      finally { delete context.fixtureGesture; }
    },
    destroy: id => context._JS_Video_Destroy(id) };
}
const old = environment({ baseline: true });
const oldId = old.create(), oldVideo = old.videos[0];
old.fire([...old.nativeFrames.keys()][0]);
old.destroy(oldId);
check(old.nativeFrames.size === 1, 'Pinned destroy leaves the recurring native frame callback registered');
old.fire([...old.nativeFrames.keys()][0]);
check(old.nativeFrames.size === 1, 'Pinned callback can reschedule after its decoder identity was deleted');
check(oldVideo.loads === 0 && oldVideo.pauses === 0, 'Pinned destroy does not explicitly reset the retired media resource');
for (const browser of ['Chrome', 'Firefox', 'Safari', 'Other']) {
  for (const frames of [false, true]) {
    const native = environment({ baseline: true, browser, frames }), guarded = environment({ browser, frames });
    const nativeId = native.create(), guardedId = guarded.create();
    function snapshot(env) {
      const video = env.videos[0];
      return { src: video.src, muted: video.muted, attributes: video.attributes,
        style: video.style, crossOrigin: video.crossOrigin, isLoaded: video.isLoaded,
        srgb: env.context.hasSRGBATextures, listeners: env.listeners.size };
    }
    check(nativeId === guardedId && JSON.stringify(snapshot(native)) === JSON.stringify(snapshot(guarded)),
      browser + ' with frame API ' + frames + ' preserves the actual pinned create configuration/readiness');
    check(guarded.nativeFrames.size === (frames ? 1 : 0), browser + ' has one bounded callback when decoded-frame tracking is available');
    if (native.nativeFrames.size) {
      native.fire([...native.nativeFrames.keys()][0]); guarded.fire([...guarded.nativeFrames.keys()][0]);
      check(JSON.stringify(snapshot(native)) === JSON.stringify(snapshot(guarded)),
        'Chrome first-frame readiness and hiccup recurrence match the pinned engine');
    } else if (guarded.nativeFrames.size) {
      guarded.fire([...guarded.nativeFrames.keys()][0]);
      check(guarded.videos[0].isLoaded && guarded.videos[0].slopCityDecodedFrame === 1
        && guarded.nativeFrames.size === 1, browser + ' presented-frame tracking marks readiness and retains one bounded request');
    }
    guarded.destroy(guardedId);
  }
}
for (const firstFrame of [false, true]) {
  const env = environment(), id = env.create(), video = env.videos[0];
  check(video.src === 'StreamingAssets/clip.mp4' && video.muted && video.attributes.playsinline === ''
    && video.crossOrigin === 'anonymous', 'Created decoder retains URL, inline, mute and CORS configuration');
  const loadedListener = env.listeners.get(video).fn;
  if (firstFrame) {
    env.fire([...env.nativeFrames.keys()][0]);
    check(video.isLoaded && env.nativeFrames.size === 1, 'Initial decoded frame marks ready and starts the compositor workaround');
    for (let i = 0; i < 1000; i++) env.fire([...env.nativeFrames.keys()][0]);
    check(env.nativeFrames.size === 1, 'Healthy compositor recurrence retains exactly one native request');
    check(video.slopCityDecodedFrame === 1001, 'Decoded-frame count advances on presented frames only');
  }
  const stale = env.nativeFrames.values().next().value.fn;
  video.isLoaded = false;
  const scratch = video.slopCityLinearVideoTexture = {};
  env.destroy(id);
  check(!env.context.videoInstances[id] && env.nativeFrames.size === 0 && env.listeners.size === 0,
    'Destroy cancels both first-frame/recurring callback and loadeddata listener');
  check(video.pauses === 1 && video.loads === 1 && video.src === '', 'Destroy pauses and resets the media resource');
  check(env.retiredTextures.includes(scratch) && !video.slopCityLinearVideoTexture, 'Destroy deletes the owned linear scratch texture');
  stale(); loadedListener();
  check(!video.isLoaded && env.nativeFrames.size === 0, 'A delivered stale frame or load event cannot resurrect a destroyed decoder');
  env.destroy(id);
  check(video.loads === 1, 'Repeated destroy is harmless');
}
const cycles = environment();
for (let i = 0; i < 1000; i++) {
  const id = cycles.create(), video = cycles.context.videoInstances[id];
  cycles.fire([...cycles.nativeFrames.keys()][0]);
  cycles.context._JS_Video_Play(id, false);
  video.loopEndPollInterval = i + 5000; cycles.timers.set(video.loopEndPollInterval, () => {});
  cycles.context.jsVideoAddPendingBlockedVideo(id, video);
  cycles.destroy(id);
}
check(Object.keys(cycles.context.videoInstances).length === 0 && cycles.nativeFrames.size === 0
  && cycles.listeners.size === 0 && cycles.timers.size === 0 && cycles.gestures.size === 0
  && Object.keys(cycles.context.jsVideoPendingBlockedVideos).length === 0,
  'Repeated creation/play/queue/destruction returns all native requests, timers, listeners and identities to baseline');
for (const browser of ['Firefox', 'Safari']) {
  const env = environment({ browser, frames: false }), id = env.create(), video = env.videos[0];
  check(env.context.hasSRGBATextures === false && env.nativeFrames.size === 0,
    browser + ' preserves pinned texture capability and callback-free fallback');
  env.listeners.get(video).fn();
  check(video.isLoaded && env.listeners.size === 0, browser + ' loadeddata readiness remains functional');
  env.destroy(id); check(video.loads === 1, browser + ' source reset works without the frame API');
}
const absent = vm.createContext({ LibraryManager: { library: {} } });
assert.throws(() => vm.runInContext(lifecycle, absent), /requires Unity Video.js first/); checks++;
async function latePlayback() {
  const flush = () => new Promise(resolve => setImmediate(resolve));
  async function unlock(env, id) {
    const video = env.context.videoInstances[id];
    video.volume = .3;
    env.context._JS_Video_Play(id, false); await flush();
    video.slopCityUploadedPictureSource = video.src;
    env.context._JS_Video_SetMute(id, false); await flush(); await flush();
    env.gesture(); await flush();
    check(video.slopCityAudioUnlocked && env.nativeAudioGrants.has(video) && !video.muted,
      'Only a native accepted audible gesture confirms this element grant');
    return video;
  }
  // Reproduce the reported hold-to-walk sequence with actual lifecycle/playback
  // helpers. Native permissions are simulated, not an iPhone acceptance claim.
  const walk = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  const loungeId = walk.create('StreamingAssets/lounge.mp4');
  const lounge = await unlock(walk, loungeId);
  const staleLoaded = walk.listeners.get(lounge).fn;
  lounge.enabledTracks = [false]; lounge.lastUpdateTextureTime = 12;
  lounge.lastSeenPlaybackTime = 12; lounge.previousUploadedWidth = 480;
  lounge.previousUploadedHeight = 270; lounge.slopCityUploadedPictureSource = lounge.src;
  lounge.slopCityTextureRetryAfter = Date.now() + 30000; lounge.currentTime = 12;
  lounge.onendedRef = 17; lounge.onendedCallback = () => { throw new Error('Old end handler'); };
  const scratch = lounge.slopCityLinearVideoTexture = {};
  walk.context._JS_Video_SetMute(loungeId, true);
  walk.destroy(loungeId);
  check(walk.context.slopCityReusableIOSVideo === lounge && lounge.paused && lounge.muted && lounge.src === ''
    && Object.keys(walk.context.videoInstances).length === 0 && walk.nativeFrames.size === 0
    && walk.timers.size === 0 && walk.gestures.size === 0 && walk.listeners.size === 0
    && walk.retiredTextures.includes(scratch),
    'Leaving lounge fully retires source, decoder identity, scratch, requests and listeners while retaining one empty element');
  const billboardId = walk.create('StreamingAssets/billboard.mp4');
  const billboard = walk.context.videoInstances[billboardId];
  check(billboard === lounge && billboardId !== loungeId && !walk.context.slopCityReusableIOSVideo
    && !billboard.slopCityPlaybackIntent && !billboard.slopCityUploadedPictureSource
    && !billboard.enabledTracks && !billboard.isReady && !billboard.isLoaded
    && billboard.lastUpdateTextureTime === undefined && billboard.lastSeenPlaybackTime === undefined
    && billboard.previousUploadedWidth === undefined && billboard.previousUploadedHeight === undefined
    && billboard.slopCityTextureRetryAfter === undefined && billboard.onendedRef === undefined
    && !billboard.onendedCallback && billboard.currentTime === 0 && !billboard.slopCityDestroyed,
    'Next screen reuses only the same native grant with a fresh source/identity, track mask, frame state and bookmark');
  staleLoaded();
  check(!billboard.isLoaded && walk.listeners.size === 1
    && billboard.slopCityLoadedDataListener === walk.listeners.get(billboard).fn,
    'Old loaded callback cannot mark a reused element ready or consume its new listener ownership');
  walk.context._JS_Video_Play(billboardId, true); await flush();
  billboard.slopCityUploadedPictureSource = billboard.src;
  const pictureTimer = billboard.slopCityPlaybackIntent.pictureWatch.timer;
  const picturePoll = walk.timers.get(pictureTimer); walk.timers.delete(pictureTimer); picturePoll();
  walk.context._JS_Video_SetMute(billboardId, false);
  check(billboard.slopCityAudioUnlocked && walk.nativeAudioGrants.has(billboard)
    && !billboard.paused && !billboard.muted && walk.gestures.size === 0
    && walk.videos.length === 1 && Object.keys(walk.context.videoInstances).length === 1,
    'Walking into billboard range while still holding the thumb uses the existing native grant without a stop/release gesture or another decoder');
  for (let step = 0; step < 100; step++) {
    walk.context._JS_Video_SetMute(billboardId, true);
    check(billboard.muted, 'Leaving live audio eligibility immediately mutes the reused screen');
    walk.context._JS_Video_SetMute(billboardId, false);
  }
  check(!billboard.muted && !billboard.paused && walk.timers.size === 0 && walk.gestures.size === 0,
    'Repeated moving range/occlusion changes retain the accepted grant without new play promises');
  walk.context._JS_Video_Pause(billboardId);
  check(billboard.paused && walk.timers.size === 0 && walk.gestures.size === 0,
    'Graphics-loss pause still cancels playback work on the reused element');
  // A browser may revoke permission. A retained hint must never override an
  // actual denial, and a later source must not inherit a rejected grant.
  walk.nativeAudioGrants.delete(billboard);
  walk.context._JS_Video_Play(billboardId, false); await flush(); await flush();
  check(!billboard.slopCityAudioUnlocked && billboard.muted && walk.context.jsVideoPendingBlockedVideos[billboardId] === billboard,
    'A reused element whose browser grant was revoked returns to muted picture and real-gesture recovery');
  walk.destroy(billboardId);
  check(!walk.context.slopCityReusableIOSVideo, 'Denied permission cannot seed the reusable element pool');
  const freshId = walk.create('StreamingAssets/next.mp4');
  const fresh = walk.context.videoInstances[freshId];
  check(fresh !== billboard && !fresh.slopCityAudioUnlocked, 'A different native element never inherits another element audio permission');
  await unlock(walk, freshId);
  walk.destroy(freshId);
  walk.context.Module.slopCityDisposeOwnedVideoDecoders();
  check(!walk.context.slopCityReusableIOSVideo && !walk.context.slopCityDisposingVideoDecoders,
    'Page disposal drops the source-free shell even when no active decoder remains');
  // Directly reproduce the prior fresh-element policy: a held touch started
  // outside range cannot enable a newly allocated screen until touchend.
  const before = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  const beforeHome = before.create(); await unlock(before, beforeHome); before.destroy(beforeHome);
  before.context.slopCityReusableIOSVideo = null;
  const beforeBillboard = before.create(); const blocked = before.context.videoInstances[beforeBillboard];
  before.context._JS_Video_Play(beforeBillboard, true); await flush();
  blocked.slopCityUploadedPictureSource = blocked.src;
  before.context._JS_Video_SetMute(beforeBillboard, false); await flush(); await flush();
  check(blocked.muted && !blocked.slopCityAudioUnlocked && before.context.jsVideoPendingBlockedVideos[beforeBillboard] === blocked,
    'Fresh-element baseline stays silent while walking after the one automatic audible attempt is denied');
  before.gesture(); await flush();
  check(!blocked.muted && blocked.slopCityAudioUnlocked,
    'Fresh-element baseline starts sound at thumb release, matching the reported stop-to-hear symptom');
  before.context.Module.slopCityDisposeOwnedVideoDecoders();
  const boundedPool = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  const firstId = boundedPool.create('StreamingAssets/first.mp4'); const first = await unlock(boundedPool, firstId);
  const secondId = boundedPool.create('StreamingAssets/second.mp4'); const second = await unlock(boundedPool, secondId);
  boundedPool.destroy(firstId); boundedPool.destroy(secondId);
  check(boundedPool.context.slopCityReusableIOSVideo === first && first !== second && first.src === '' && second.src === ''
    && Object.keys(boundedPool.context.videoInstances).length === 0 && boundedPool.timers.size === 0 && boundedPool.gestures.size === 0,
    'Even unexpected multiple retirements retain only one empty element and zero active decoders');
  boundedPool.context.Module.slopCityDisposeOwnedVideoDecoders();
  const longWalk = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  let nextScreenId = longWalk.create(); const sharedElement = await unlock(longWalk, nextScreenId);
  for (let screen = 0; screen < 1000; screen++) {
    longWalk.destroy(nextScreenId);
    nextScreenId = longWalk.create('StreamingAssets/screen-' + screen + '.mp4');
    longWalk.context._JS_Video_Play(nextScreenId, true); await flush();
    longWalk.context._JS_Video_SetMute(nextScreenId, false);
    check(longWalk.context.videoInstances[nextScreenId] === sharedElement && !sharedElement.muted
      && Object.keys(longWalk.context.videoInstances).length === 1 && !longWalk.context.slopCityReusableIOSVideo
      && longWalk.nativeFrames.size === 0 && longWalk.gestures.size === 0,
      'Repeated iOS source handoff retains one granted element and exactly one owned decoder without new gestures');
  }
  longWalk.context.Module.slopCityDisposeOwnedVideoDecoders();
  check(longWalk.videos.length === 1 && !longWalk.context.slopCityReusableIOSVideo
    && Object.keys(longWalk.context.videoInstances).length === 0 && longWalk.listeners.size === 0
    && longWalk.timers.size === 0 && longWalk.gestures.size === 0,
    'A thousand granted iOS screen handoffs finish with one allocated shell and no retained sources or native work');
  for (const success of [false, true]) {
    const late = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
    const retiredId = late.create('StreamingAssets/retired.mp4'); const reused = await unlock(late, retiredId);
    late.context._JS_Video_Pause(retiredId);
    const nativePlay = reused.play; let settle;
    reused.play = () => new Promise((resolve, reject) => { settle = success ? resolve : reject; });
    late.context._JS_Video_Play(retiredId, false);
    late.destroy(retiredId); reused.play = nativePlay;
    const currentId = late.create('StreamingAssets/current.mp4');
    settle(success ? undefined : Object.assign(new Error('Old permission denial'), { name: 'NotAllowedError' }));
    await flush();
    check(late.context.videoInstances[currentId] === reused && reused.slopCityAudioUnlocked
      && !reused.slopCityPlaybackIntent && !reused.slopCityUploadedPictureSource && !reused.isLoaded
      && !late.context.jsVideoPendingBlockedVideos[retiredId] && late.timers.size === 0
      && late.gestures.size === 0 && late.listeners.size === 1,
      'Late prior-source play ' + (success ? 'success' : 'denial') + ' cannot alter a reused element grant, intent, picture or listener');
    late.context.Module.slopCityDisposeOwnedVideoDecoders();
  }
  for (const os of ['MacOS', 'Android']) {
    const unchanged = environment({ os }); const oldId = unchanged.create(); const oldElement = unchanged.context.videoInstances[oldId];
    oldElement.slopCityAudioUnlocked = true;
    unchanged.destroy(oldId); const newId = unchanged.create();
    check(!unchanged.context.slopCityReusableIOSVideo && unchanged.context.videoInstances[newId] !== oldElement,
      os + ' keeps its existing fresh-element lifecycle even after audible playback');
    unchanged.context.Module.slopCityDisposeOwnedVideoDecoders();
  }
  const disposal = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  const disposedId = disposal.create(); await unlock(disposal, disposedId);
  disposal.context.Module.slopCityDisposeOwnedVideoDecoders();
  check(!disposal.context.slopCityReusableIOSVideo && Object.keys(disposal.context.videoInstances).length === 0,
    'Disposing an active granted decoder cannot repopulate the shell during page teardown');
  const resetError = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  const brokenId = resetError.create(); const broken = await unlock(resetError, brokenId);
  broken.load = () => { throw new Error('Native reset failed'); };
  assert.throws(() => resetError.destroy(brokenId), /Native reset failed/); checks++;
  check(!resetError.context.slopCityReusableIOSVideo && Object.keys(resetError.context.videoInstances).length === 0
    && resetError.timers.size === 0 && resetError.gestures.size === 0 && resetError.listeners.size === 0,
    'A failed native source reset cannot retain an element for reuse or resurrect its identity/retries');
  const partialDisposal = environment({ os: 'iPhoneOS', browser: 'Safari', audioPolicy: true });
  const errorId = partialDisposal.create(); const errorVideo = await unlock(partialDisposal, errorId);
  const survivorId = partialDisposal.create(); await unlock(partialDisposal, survivorId);
  errorVideo.load = () => { throw new Error('First reset failed'); };
  assert.throws(() => partialDisposal.context.Module.slopCityDisposeOwnedVideoDecoders(), /First reset failed/); checks++;
  check(Object.keys(partialDisposal.context.videoInstances).length === 0 && !partialDisposal.context.slopCityReusableIOSVideo
    && !partialDisposal.context.slopCityDisposingVideoDecoders && partialDisposal.timers.size === 0
    && partialDisposal.gestures.size === 0 && partialDisposal.listeners.size === 0,
    'Disposal continues retiring later decoders after a reset exception and clears the shell/disposing state');
  for (const device of [{ os: 'iPhoneOS' }, { os: 'iPadOS' }, { os: 'MacOS', platform: 'MacIntel', maxTouchPoints: 5 }]) {
    const { os } = device;
    for (const browser of ['Safari', 'Chrome', 'Firefox']) {
      for (const frames of [false, true]) {
        const env = environment({ ...device, browser, frames });
        const prototypeFrameAPI = env.videoPrototype.requestVideoFrameCallback;
        const id = env.create(), video = env.videos[0];
        check(Object.hasOwn(video, 'requestVideoFrameCallback') && video.requestVideoFrameCallback === undefined
          && env.nativeFrames.size === 0, os + '/' + browser + ' owns callback-free create before any native registration');
        check(env.videoPrototype.requestVideoFrameCallback === prototypeFrameAPI
          && Object.create(env.videoPrototype).requestVideoFrameCallback === prototypeFrameAPI,
          os + '/' + browser + ' leaves prototype and unrelated video API unchanged');
        video.HAVE_METADATA = 1; video.HAVE_FUTURE_DATA = 3; video.HAVE_ENOUGH_DATA = 4;
        video.readyState = 0; video.currentTime = 0;
        env.context.navigator.userAgent = 'Mozilla/5.0 (' + (os === 'iPadOS' ? 'iPad' : 'iPhone') + ') AppleWebKit/605.1.15';
        check(!env.context._JS_Video_IsReady(id), os + ' still waits for metadata');
        video.readyState = 1;
        check(env.context._JS_Video_IsReady(id) && video.isReady, os + ' retains native metadata readiness fallback');
        let nativePlayCalls = 0;
        video.play = () => { nativePlayCalls++; return Promise.resolve(); };
        env.context._JS_Video_Play(id, false);
        await new Promise(resolve => setImmediate(resolve));
        check(nativePlayCalls === 1 && video.slopCityPlaybackIntent.pending === false
          && env.nativeFrames.size === 0 && !video.slopCityFrameRequest,
          os + '/' + browser + ' successful project playback keeps audio request and registers no RVFC');
        // Exercise the pinned native play helper too, so its own typeof check
        // cannot bypass the owned shadow if a future shared path invokes it.
        env.context.nativeVideoPlay(id, false);
        check(nativePlayCalls === 2 && env.nativeFrames.size === 0,
          os + '/' + browser + ' pinned native playback also selects callback-free path');
        env.context._JS_Video_Seek(id, 3.25);
        check(video.currentTime === 3.25 && video.lastSeenPlaybackTime === 3.25
          && env.nativeFrames.size === 0, os + ' pinned seek changes playback time without native callback registration');
        const loadedListener = env.listeners.get(video).fn;
        loadedListener();
        check(video.isLoaded && env.listeners.size === 0 && env.nativeFrames.size === 0,
          os + ' loadeddata still updates readiness without a queued native frame');
        // A successful mobile play may still wait for its trusted audio gesture.
        // Cancel that real intent before creating the independent pending-play
        // completion whose stale result the following pause assertion exercises.
        env.context._JS_Video_Pause(id);
        let settle;
        video.play = () => new Promise(resolve => { settle = resolve; });
        env.context._JS_Video_Play(id, false);
        env.context._JS_Video_Pause(id);
        settle();
        await new Promise(resolve => setImmediate(resolve));
        check(!video.slopCityPlaybackIntent && env.nativeFrames.size === 0 && env.timers.size === 0,
          os + ' pause invalidates a stale successful play completion on fallback');
        video.isLoaded = false;
        env.destroy(id); loadedListener();
        check(!video.isLoaded && !env.context.videoInstances[id] && env.nativeFrames.size === 0
          && env.timers.size === 0 && env.gestures.size === 0,
          os + ' fallback destroy rejects stale readiness and retires existing owners');
      }
    }
  }
  for (const maxTouchPoints of [0, 1]) {
    const desktop = environment({ browser: 'Safari', platform: 'MacIntel', maxTouchPoints });
    const api = desktop.videoPrototype.requestVideoFrameCallback;
    desktop.create();
    check(desktop.videos[0].requestVideoFrameCallback === api
      && !Object.hasOwn(desktop.videos[0], 'requestVideoFrameCallback') && desktop.nativeFrames.size === 1,
      'Desktop Safari with ' + maxTouchPoints + ' touch points keeps native decoded-frame tracking');
  }
  const missingNavigator = environment({ browser: 'Safari' });
  delete missingNavigator.context.navigator;
  missingNavigator.context.jsIsWebkit = () => true;
  missingNavigator.create();
  check(missingNavigator.nativeFrames.size === 1, 'iPad desktop-mode detection guards missing navigator without changing unrelated create');
  for (const os of ['MacOS', 'iPhoneOS', 'iPadOS']) {
    const teardown = environment({ os, browser: 'Safari' });
    const staleFrames = [], staleLoads = [], settlers = [];
    for (let count = 0; count < 3; count++) {
      const id = teardown.create(), video = teardown.videos[count];
      if (teardown.nativeFrames.size) {
        staleFrames.push([...teardown.nativeFrames.values()].find(entry => entry.video === video).fn);
      }
      staleLoads.push(teardown.listeners.get(video).fn);
      video.play = () => new Promise(resolve => settlers.push(resolve));
      teardown.context._JS_Video_Play(id, false);
      video.loopEndPollInterval = 9000 + count; teardown.timers.set(video.loopEndPollInterval, () => {});
      teardown.context.jsVideoAddPendingBlockedVideo(id, video);
    }
    check(typeof teardown.context.Module.slopCityDisposeOwnedVideoDecoders === 'function',
      os + ' synchronous cleanup is available before another game frame');
    teardown.context.Module.slopCityDisposeOwnedVideoDecoders();
    check(Object.keys(teardown.context.videoInstances).length === 0 && teardown.nativeFrames.size === 0
      && teardown.listeners.size === 0 && teardown.timers.size === 0 && teardown.gestures.size === 0
      && Object.keys(teardown.context.jsVideoPendingBlockedVideos).length === 0,
      os + ' synchronous cleanup retires every owned decoder/callback/request/loop/retry');
    teardown.videos.forEach(video => { video.isLoaded = false; });
    settlers.forEach(resolve => resolve()); staleFrames.forEach(frame => frame()); staleLoads.forEach(load => load());
    await new Promise(resolve => setImmediate(resolve));
    check(teardown.videos.every(video => video.slopCityDestroyed && video.pauses >= 1 && video.loads === 1
      && video.src === '' && !video.isLoaded) && teardown.nativeFrames.size === 0,
      os + ' synchronous cleanup resets sources and prevents stale resurrection');
    teardown.context.Module.slopCityDisposeOwnedVideoDecoders();
    check(teardown.videos.every(video => video.loads === 1), os + ' repeated synchronous cleanup is idempotent');
  }
  for (const success of [false, true]) {
    const env = environment();
    const id = env.create(), video = env.videos[0];
    let settle;
    video.play = () => new Promise((resolve, reject) => { settle = success ? resolve : reject; });
    env.context._JS_Video_Play(id, false);
    env.destroy(id);
    settle(success ? undefined : Object.assign(new Error('fixture denied'), { name: 'NotAllowedError' }));
    await new Promise(resolve => setImmediate(resolve));
    check(env.nativeFrames.size === 0 && env.timers.size === 0 && env.gestures.size === 0
      && !env.context.jsVideoPendingBlockedVideos[id] && !env.context.videoInstances[id],
      'Settled native play ' + (success ? 'success' : 'denial') + ' cannot revive a retired decoder/source');
  }
  console.log(JSON.stringify({ passed: true, checks, framework: !!framework,
    scope: 'Actual pinned/project helpers: simulated per-element permission reproduces silent held-thumb fresh-element handoff and fixes same-element iOS lounge/billboard continuity; mute eligibility, revocation, stale source state, bounded empty-element pool, exception/disposal cleanup; existing desktop/iOS callback/readiness and 1000 lifecycle checks. No physical browser, speaker or memory acceptance' }));
}
latePlayback().catch(error => { console.error(error); process.exitCode = 1; });
