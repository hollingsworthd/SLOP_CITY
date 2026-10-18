#!/usr/bin/env node
// Exercise the production bridge together with the pinned Unity video helpers.
// Optional --framework verifies and executes the helpers from the rebuilt export.
// This tests exception/queue lifecycle, not physical iPhone playback.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const zlib = require('node:zlib');
const root = path.resolve(__dirname, '../..');
const pluginPath = path.join(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityVideoPlayback.jslib');
const enginePath = path.join(root, '.tools/Unity-6000.3.26f1/Unity.app/Contents/PlaybackEngines/WebGLSupport/BuildTools/lib/Video.js');
const engine = fs.readFileSync(enginePath, 'utf8');
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }
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

let framework;
if (process.argv.length > 2) {
  assert.equal(process.argv[2], '--framework');
  assert.ok(process.argv[3], '--framework needs a path');
  const bytes = fs.readFileSync(process.argv[3]);
  framework = (bytes[0] === 0x1f && bytes[1] === 0x8b ? zlib.gunzipSync(bytes) : bytes).toString('utf8');
}
const names = ['jsVideoAddPendingBlockedVideo', 'jsVideoPlayPendingBlockedVideo',
  'jsVideoRemovePendingBlockedVideo', 'jsVideoAttemptToPlayBlockedVideos', 'jsVideoAllAudioTracksAreDisabled'];
const coreNames = ['JS_Video_Play', 'JS_Video_Pause', 'JS_Video_Destroy', 'JS_Video_SetMute'];
function environment({ bridge = true } = {}) {
  const listeners = [], events = [], removals = [], loopCalls = [], clearedIntervals = [];
  const timers = new Map();
  let nextTimer = 1;
  const window = {
    CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
    addEventListener(type, fn, capture) {
      if (!listeners.some(item => item.type === type && item.fn === fn && item.capture === capture))
        listeners.push({ type, fn, capture });
    },
    removeEventListener(type, fn, capture) {
      removals.push({ type, capture });
      const index = listeners.findIndex(item => item.type === type && item.fn === fn && item.capture === capture);
      if (index >= 0) listeners.splice(index, 1);
    }
  };
  const context = vm.createContext({ window, document: { dispatchEvent(event) { events.push(event); } },
    Promise, Error, Object, loopCalls, clearInterval(id) { clearedIntervals.push(id); },
    setTimeout(fn) { const id = nextTimer++; timers.set(id, fn); return id; },
    clearTimeout(id) { timers.delete(id); },
    LibraryManager: { library: {} }, mergeInto(target, fields) { Object.assign(target, fields); } });
  vm.runInContext('var videoInstances = {}; var jsVideoPendingBlockedVideos = {}; var slopCityVideoRetryInFlight = {}; function _JS_Video_SetLoop(id, loop) { loopCalls.push({ id: id, loop: loop }); }', context);
  for (const name of names) vm.runInContext('globalThis.' + name + ' = ' + helper(engine, '$' + name + ':') + ';', context);
  for (const name of coreNames) vm.runInContext('globalThis._' + name + ' = ' + helper(engine, name + ':') + ';', context);
  if (bridge) {
    vm.runInContext(fs.readFileSync(pluginPath, 'utf8'), context);
    const library = context.LibraryManager.library;
    for (const [key, value] of Object.entries(library)) {
      if (key.startsWith('$') && !key.includes('__')) context[key.slice(1)] = value;
      else if (!key.includes('__') && typeof value === 'function') context['_' + key] = value;
    }
    if (framework) {
      for (const name of [...names, 'slopCityNotifyVideoPlayback', 'slopCityVideoRetryGesture',
        'slopCityCancelVideoFrame', 'slopCityRequestVideoFrame', 'slopCityClearVideoAttemptTimer', 'slopCityTryMutedVideoFallback',
        'slopCityCancelCreatedVideoFrame', 'slopCityNativeVideoDestroy', 'slopCityReleaseVideoTexture'])
        vm.runInContext('globalThis.' + name + ' = ' + helper(framework, 'function ' + name + '(') + ';', context);
      for (const name of coreNames)
        vm.runInContext('globalThis._' + name + ' = ' + helper(framework, 'function _' + name + '(') + ';', context);
      check(helper(framework, 'function jsVideoPlayPendingBlockedVideo(').includes('slopCityVideoRetryInFlight'), 'Export includes retry tokens');
      check(helper(framework, 'function jsVideoRemovePendingBlockedVideo(').includes('slopCityNotifyVideoPlayback'), 'Export includes queue cleanup/status');
      check(helper(framework, 'function jsVideoAddPendingBlockedVideo(').includes('slopCityNotifyVideoPlayback'), 'Export includes pending status');
      check(helper(framework, 'function _JS_Video_Play(').includes('slopCityPlaybackIntent')
        && helper(framework, 'function _JS_Video_Pause(').includes('slopCityPlaybackIntent'), 'Export includes initial-play and pause intent guards');
    }
  }
  return { context, listeners, events, removals, loopCalls, clearedIntervals, timers, gesture(type = 'touchstart', fields = {}) {
    const event = Object.assign({ type, isTrusted: true, pointerType: 'touch', key: 'Enter' }, fields);
    context.fixtureGestureType = type;
    try {
      for (const item of [...listeners]) if (item.type === type) item.fn(event);
    } finally { delete context.fixtureGestureType; }
  } };
}
function decoder(play) { return { play, paused: true, frames: 0, pauses: 0, frameRequests: new Map(),
  requestVideoFrameCallback(fn) {
    const id = ++this.frames;
    this.frameCallback = () => { this.frameRequests.delete(id); fn(); };
    this.frameRequests.set(id, this.frameCallback);
    return id;
  },
  cancelVideoFrameCallback(id) { this.frameRequests.delete(id); }, pause() { this.pauses++; },
  removeAttribute(name) { if (name === 'src') this.src = ''; }, load() {} }; }
function denial(name = 'NotAllowedError') { return Object.assign(new Error('fixture'), { name }); }
const flush = () => new Promise(resolve => setImmediate(resolve));
function queue(env, id, video) { env.context.videoInstances[id] = video; env.context.jsVideoAddPendingBlockedVideo(id, video); }

async function main() {
  const baseline = environment({ bridge: false });
  queue(baseline, 1, decoder(() => { throw denial(); }));
  assert.throws(() => baseline.gesture(), { name: 'NotAllowedError' }); checks++;
  // Reproduce a handoff whose audible play is denied but muted play is allowed.
  const fallback = environment(); let fallbackCalls = 0;
  const picture = decoder(() => {
    fallbackCalls++;
    if (!picture.muted && !fallback.context.fixtureGestureType) return Promise.reject(denial());
    picture.paused = false; return Promise.resolve();
  });
  fallback.context.videoInstances[1] = picture;
  fallback.context._JS_Video_Play(1, false); await flush();
  for (let n = 0; n < 600; n++) fallback.context._JS_Video_Play(1, false);
  check(fallbackCalls === 1, 'Audible denial remains deduplicated before muted fallback request');
  fallback.context._JS_Video_SetMute(1, true); await flush();
  check(fallbackCalls === 2 && !picture.paused && picture.muted,
    'Muted handoff picture advances without another trusted tap');
  check(fallback.context.jsVideoPendingBlockedVideos[1] === picture && fallback.listeners.length === 6,
    'Advancing muted fallback retains trusted audible recovery');
  for (let n = 0; n < 600; n++) {
    fallback.context._JS_Video_Play(1, true);
    fallback.context._JS_Video_SetMute(1, false);
  }
  check(fallbackCalls === 2 && picture.muted && !picture.slopCityRequestedMuted,
    'Mixer unmute is deferred while fallback remains moving, without promise churn');
  fallback.gesture('touchend'); await flush();
  check(fallbackCalls === 3 && !picture.muted && !fallback.context.jsVideoPendingBlockedVideos[1]
    && !picture.slopCityMutedFallback, 'Trusted retry applies requested audio and clears fallback/queue');
  fallback.context._JS_Video_Pause(1);
  const mutedDenied = environment(); let mutedDeniedCalls = 0;
  const never = decoder(() => { mutedDeniedCalls++; return Promise.reject(denial()); });
  mutedDenied.context.videoInstances[1] = never;
  mutedDenied.context._JS_Video_Play(1, true); await flush();
  for (let n = 0; n < 600; n++) mutedDenied.context._JS_Video_Play(1, true);
  check(mutedDeniedCalls === 1 && !never.slopCityMutedFallback,
    'Original muted autoplay denial gets no redundant fallback and still waits for gesture');
  mutedDenied.context._JS_Video_Pause(1);
  for (const action of ['pause', 'destroy', 'replace']) {
    const canceled = environment(); let resolveFallback; let calls = 0;
    const video = decoder(() => {
      calls++; return calls === 1 ? Promise.reject(denial()) : new Promise(resolve => { resolveFallback = resolve; });
    });
    canceled.context.videoInstances[1] = video;
    canceled.context._JS_Video_Play(1, false); await flush();
    canceled.context._JS_Video_SetMute(1, true);
    check(calls === 2 && canceled.timers.size === 1, 'Pending muted fallback owns one timeout for ' + action);
    if (action === 'replace') {
      canceled.context._JS_Video_Pause(1);
      canceled.context.videoInstances[1] = decoder(() => Promise.resolve());
    } else canceled.context[action === 'pause' ? '_JS_Video_Pause' : '_JS_Video_Destroy'](1);
    resolveFallback(); await flush();
    check(canceled.timers.size === 0 && video.frames === 0 && !canceled.context.jsVideoPendingBlockedVideos[1],
      'Muted fallback completion after ' + action + ' cannot resurrect playback/readiness');
  }
  const bounded = environment(); let boundedCalls = 0;
  const refusesBoth = decoder(() => { boundedCalls++; return Promise.reject(denial()); });
  bounded.context.videoInstances[1] = refusesBoth;
  bounded.context._JS_Video_Play(1, false); await flush();
  bounded.context._JS_Video_SetMute(1, true); await flush();
  for (let n = 0; n < 600; n++) bounded.context._JS_Video_Play(1, true);
  check(boundedCalls === 2 && bounded.context.jsVideoPendingBlockedVideos[1] === refusesBoth,
    'Denied muted fallback remains bounded to one additional attempt per denied audible intent');
  bounded.context._JS_Video_Pause(1);
  const delayedMute = environment(); let finishMuted; let delayedCalls = 0;
  const waits = decoder(() => {
    delayedCalls++; return delayedCalls === 1 ? Promise.reject(denial()) : new Promise(resolve => { finishMuted = resolve; });
  });
  delayedMute.context.videoInstances[1] = waits;
  delayedMute.context._JS_Video_Play(1, false); await flush();
  delayedMute.context._JS_Video_SetMute(1, true);
  const timeout = [...delayedMute.timers.values()][0]; timeout();
  finishMuted(); await flush();
  check(waits.pauses === 1 && waits.frames === 0 && delayedMute.timers.size === 0,
    'Muted fallback timeout invalidates a later completion and pauses just its decoder');
  check(delayedMute.context.jsVideoPendingBlockedVideos[1] === waits,
    'Timed-out fallback retains trusted recovery without more automatic attempts');
  delayedMute.context._JS_Video_Pause(1);
  const trustedDenial = environment(); let trustedCalls = 0;
  const recoverPicture = decoder(() => {
    trustedCalls++;
    if (!recoverPicture.muted) return Promise.reject(denial());
    recoverPicture.paused = false; return Promise.resolve();
  });
  trustedDenial.context.videoInstances[1] = recoverPicture;
  trustedDenial.context._JS_Video_Play(1, false); await flush();
  trustedDenial.context._JS_Video_SetMute(1, true); await flush();
  trustedDenial.context._JS_Video_SetMute(1, false);
  trustedDenial.gesture('touchend'); await flush(); await flush();
  check(trustedCalls === 4 && recoverPicture.muted && !recoverPicture.paused,
    'One failed trusted audible retry restores the muted picture with one bounded attempt');
  for (let n = 0; n < 600; n++) trustedDenial.context._JS_Video_Play(1, true);
  check(trustedCalls === 4 && trustedDenial.context.jsVideoPendingBlockedVideos[1] === recoverPicture,
    'Muted recovery after trusted denial retains audible queue without poll churn');
  trustedDenial.context._JS_Video_Pause(1);
  const overlapping = environment(); let fallbackOverlapCalls = 0, finishTrusted;
  const overlap = decoder(() => {
    fallbackOverlapCalls++;return fallbackOverlapCalls === 1 ? Promise.reject(denial()) : new Promise(resolve => { finishTrusted = resolve; });
  });
  overlapping.context.videoInstances[1] = overlap;
  overlapping.context._JS_Video_Play(1, false); await flush();
  overlapping.gesture('touchend');overlapping.context._JS_Video_SetMute(1, true);
  check(fallbackOverlapCalls === 2 && !overlap.slopCityMutedFallback,
    'Muted mixer request cannot start fallback concurrently with unresolved trusted audible attempt');
  overlapping.context._JS_Video_Pause(1);finishTrusted();await flush();
  const inlineRefusal = environment(); let inlineCalls = 0;
  const prohibited = decoder(() => { inlineCalls++; return Promise.reject(denial('NotSupportedError')); });
  inlineRefusal.context.videoInstances[1] = prohibited;
  inlineRefusal.context._JS_Video_Play(1, false);await flush();
  inlineRefusal.context._JS_Video_SetMute(1, true);
  check(inlineCalls === 1 && !prohibited.slopCityMutedFallback && !inlineRefusal.context.jsVideoPendingBlockedVideos[1],
    'Fullscreen/inline-only terminal refusal never receives a muted bypass attempt');
  inlineRefusal.context._JS_Video_Pause(1);
  const trustedTimeout = environment(); let finishLateTrusted; let timeoutCalls = 0;
  const timeoutPicture = decoder(() => {
    timeoutCalls++;
    if (timeoutCalls === 1) return Promise.reject(denial());
    if (timeoutCalls === 3) return new Promise(resolve => { finishLateTrusted = resolve; });
    timeoutPicture.paused = false; return Promise.resolve();
  });
  trustedTimeout.context.videoInstances[1] = timeoutPicture;
  trustedTimeout.context._JS_Video_Play(1, false); await flush();
  trustedTimeout.context._JS_Video_SetMute(1, true); await flush();
  trustedTimeout.context._JS_Video_SetMute(1, false);trustedTimeout.gesture('touchend');
  [...trustedTimeout.timers.values()][0]();await flush();
  check(timeoutCalls === 4 && !timeoutPicture.paused && timeoutPicture.muted,
    'Timed-out trusted audible retry restores its preceding muted picture once');
  const readyFramesBeforeLate = timeoutPicture.frames;
  finishLateTrusted();await flush();
  check(trustedTimeout.context.jsVideoPendingBlockedVideos[1] === timeoutPicture
    && timeoutPicture.slopCityMutedFallback && timeoutPicture.frames === readyFramesBeforeLate,
    'Late trusted completion after timeout cannot clear restored audible queue or authorize a stale frame');
  for (let n = 0; n < 600; n++) trustedTimeout.context._JS_Video_Play(1, true);
  check(timeoutCalls === 4 && trustedTimeout.timers.size === 0,
    'Trusted timeout recovery creates no accumulating native attempts or timers');
  trustedTimeout.context._JS_Video_Pause(1);
  const env = environment(), c = env.context, library = c.LibraryManager.library;
  const repeated = environment(); let repeatedCalls = 0;
  const refused = decoder(() => { repeatedCalls++; return Promise.reject(denial()); });
  repeated.context.videoInstances[1] = refused;
  for (let i = 0; i < 600; i++) repeated.context._JS_Video_Play(1, false);
  check(repeatedCalls === 1 && refused.frames === 0,
    'Repeated initial play while unresolved invokes native play once and queues no frame callbacks');
  await flush();
  for (let i = 0; i < 600; i++) repeated.context._JS_Video_Play(1, false);
  check(repeatedCalls === 1 && refused.frames === 0
    && repeated.context.jsVideoPendingBlockedVideos[1] === refused,
    'Repeated permission-denied initial play waits for a gesture without callback accumulation');
  repeated.context._JS_Video_Play(1, true);
  check(repeatedCalls === 2 && refused.muted, 'Audible denial permits one separate muted fallback');
  repeated.context._JS_Video_Play(1, false);
  check(repeatedCalls === 2 && refused.muted && !refused.slopCityRequestedMuted, 'Fallback records requested audio without off-gesture unmute or repeated play');
  repeated.context._JS_Video_Pause(1);
  for (const [key, value] of Object.entries(library)) {
    if (!key.endsWith('__deps')) continue;
    check(Array.isArray(value) && value.every(dep => typeof dep === 'string'), key + ' dependencies are library helper names');
    for (const dep of value) check(typeof c[dep.startsWith('$') ? dep.slice(1) : '_' + dep] !== 'undefined', key + ' resolves ' + dep);
  }
  check(library.JS_Video_Play__proxy === 'sync' && library.JS_Video_Play__sig === 'vii'
    && library.JS_Video_Pause__proxy === 'sync' && library.JS_Video_Pause__sig === 'vi', 'Playback/pause preserve Unity signatures and synchronous proxy');
  check(!('JS_Video_Destroy' in library), 'Destroy remains the actual Unity helper');
  let attempts = 0;
  const sync = decoder(() => { attempts++; if (attempts < 3) throw denial(); return Promise.resolve(); });
  queue(env, 1, sync);
  env.gesture(); check(attempts === 1 && c.jsVideoPendingBlockedVideos[1] === sync, 'Synchronous denial is contained and stays queued');
  env.gesture(); check(attempts === 2 && sync.frames === 0, 'Repeated denied gesture stays pending without a ready frame');
  env.gesture(); check(attempts === 3, 'Native playback is invoked synchronously inside the gesture');
  await flush();
  check(!c.jsVideoPendingBlockedVideos[1] && sync.frames === 1, 'Later successful gesture clears queue and requests a frame');
  sync.frameCallback(); check(sync.isLoaded, 'Only the decoded success frame becomes loaded');
  check(env.listeners.length === 0 && env.removals.every(item => item.capture === true), 'Success removes capture-phase gesture listeners');
  check(env.events[0].detail.state === 'waiting' && env.events.at(-1).detail.state === 'ready', 'Queue mutations report waiting/ready');
  check(env.events.every(event => event.type === 'slopcityvideoplayback' && Object.keys(event.detail).sort().join(',') === 'pendingCount,state'), 'Status carries only count/state');

  let asyncAttempts = 0;
  const asynchronous = decoder(() => { asyncAttempts++; return asyncAttempts === 1 ? Promise.reject(denial()) : Promise.resolve(); });
  queue(env, 2, asynchronous); env.gesture(); await flush();
  check(c.jsVideoPendingBlockedVideos[2] === asynchronous && asynchronous.frames === 0, 'Promise denial remains queued');
  env.gesture('mousedown'); await flush();
  check(asyncAttempts === 2 && !c.jsVideoPendingBlockedVideos[2], 'Mouse gesture can recover promise denial');
  for (const name of ['NotSupportedError', 'AbortError', 'NotReadableError']) {
    const terminal = decoder(() => Promise.reject(denial(name)));
    queue(env, 3, terminal); env.gesture(); await flush();
    check(!c.jsVideoPendingBlockedVideos[3] && terminal.frames === 0, name + ' drops retry without marking a frame ready');
  }
  const terminalSync = decoder(() => { throw denial('NotSupportedError'); });
  queue(env, 4, terminalSync); env.gesture();
  check(!c.jsVideoPendingBlockedVideos[4], 'Synchronous terminal failure stops retries');

  let resolve, overlapCalls = 0;
  const pending = decoder(() => { overlapCalls++; return new Promise(r => { resolve = r; }); });
  queue(env, 5, pending); env.gesture(); env.gesture();
  check(overlapCalls === 1, 'Overlapping gestures do not duplicate in-flight decoder playback');
  c._JS_Video_Pause(5); resolve(); await flush();
  check(pending.pauses === 1 && pending.frames === 0 && !c.jsVideoPendingBlockedVideos[5], 'Unity pause invalidates delayed success');

  let destroyedResolve;
  const destroyed = decoder(() => new Promise(r => { destroyedResolve = r; }));
  queue(env, 6, destroyed); env.gesture(); c._JS_Video_Destroy(6); destroyedResolve(); await flush();
  check(!c.videoInstances[6] && destroyed.frames === 0 && destroyed.src === '', 'Unity destroy invalidates delayed success');

  let oldResolve, newResolve;
  const old = decoder(() => new Promise(r => { oldResolve = r; }));
  const replacement = decoder(() => new Promise(r => { newResolve = r; }));
  queue(env, 7, old); env.gesture(); queue(env, 7, replacement); env.gesture(); oldResolve(); await flush();
  check(c.jsVideoPendingBlockedVideos[7] === replacement && old.frames === 0, 'Old completion cannot remove a replaced entry');
  newResolve(); await flush(); check(!c.jsVideoPendingBlockedVideos[7] && replacement.frames === 1, 'Replacement completion succeeds');

  const completions = [];
  const same = decoder(() => new Promise(r => completions.push(r)));
  queue(env, 8, same); env.gesture(); c._JS_Video_Pause(8); queue(env, 8, same); env.gesture();
  completions[0](); await flush();
  check(c.jsVideoPendingBlockedVideos[8] === same && same.frames === 0, 'Generation token ignores old success for requeued same decoder');
  completions[1](); await flush(); check(same.frames === 1 && !c.jsVideoPendingBlockedVideos[8], 'Requeued decoder succeeds on its current attempt');

  const legacy = decoder(() => undefined); queue(env, 9, legacy); env.gesture();
  check(legacy.frames === 1 && !c.jsVideoPendingBlockedVideos[9], 'Legacy void play result is handled without TypeError');
  const multi1 = decoder(() => Promise.reject(denial())), multi2 = decoder(() => Promise.resolve());
  queue(env, 10, multi1); queue(env, 11, multi2); env.gesture(); await flush();
  check(c.jsVideoPendingBlockedVideos[10] === multi1 && !c.jsVideoPendingBlockedVideos[11] && env.listeners.length === 6, 'Multiple decoders retry independently and keep all gesture listeners while one waits');
  c._JS_Video_Pause(10); check(env.listeners.length === 0, 'Last pause cleans up listeners');

  const initial = decoder(() => Promise.reject(denial())); initial.loop = true; c.videoInstances[12] = initial;
  c._JS_Video_Play(12, false); await flush();
  check(c.jsVideoPendingBlockedVideos[12] === initial && initial.muted === false, 'Actual Unity initial denial feeds production retry queue and preserves mute');
  check(env.loopCalls.at(-1).id === 12 && env.loopCalls.at(-1).loop === true, 'Initial playback retains the selected loop setting');
  initial.loopEndPollInterval = 'fixture-loop-12';
  c._JS_Video_Pause(12);
  check(env.clearedIntervals.includes('fixture-loop-12'), 'Pause retains Unity loop-poll cleanup');
  for (const lifecycle of ['pause', 'destroy', 'replace', 'resume']) {
    let rejectInitial;
    const stale = decoder(() => new Promise((resolve, reject) => { rejectInitial = reject; }));
    c.videoInstances[13] = stale; c._JS_Video_Play(13, true);
    const oldReject = rejectInitial;
    if (lifecycle === 'pause') c._JS_Video_Pause(13);
    else if (lifecycle === 'destroy') c._JS_Video_Destroy(13);
    else if (lifecycle === 'replace') c.videoInstances[13] = decoder(() => Promise.resolve());
    else { c._JS_Video_Pause(13); c._JS_Video_Play(13, true); }
    oldReject(denial()); await flush();
    check(!c.jsVideoPendingBlockedVideos[13], 'Initial denial after ' + lifecycle + ' cannot resurrect stale playback');
    check(stale.frames === 0, 'Unsuccessful initial play after ' + lifecycle + ' queues no frame callback');
    if (c.videoInstances[13]) c._JS_Video_Pause(13);
  }
  let newReject;
  const later = decoder(() => new Promise((resolve, reject) => { newReject = reject; }));
  c.videoInstances[14] = later; c._JS_Video_Play(14, true); c._JS_Video_Pause(14);
  c._JS_Video_Play(14, false); newReject(denial()); await flush();
  check(c.jsVideoPendingBlockedVideos[14] === later && !later.muted, 'New play after pause has a valid new intent and preserves requested audio');
  c._JS_Video_Pause(14);
  const syncInitial = decoder(() => { throw denial(); }); c.videoInstances[15] = syncInitial;
  c._JS_Video_Play(15, false);
  check(c.jsVideoPendingBlockedVideos[15] === syncInitial, 'Synchronous initial denial queues safely');
  c._JS_Video_Pause(15);
  for (const action of ['pause', 'destroy', 'replace', 'resume']) {
    const delayed = environment(), completions = [];
    const video = decoder(() => new Promise(resolve => completions.push(resolve)));
    delayed.context.videoInstances[1] = video;
    delayed.context._JS_Video_Play(1, false);
    if (action === 'destroy' || action === 'replace') delayed.context._JS_Video_Destroy(1);
    else delayed.context._JS_Video_Pause(1);
    if (action === 'replace') delayed.context.videoInstances[1] = decoder(() => Promise.resolve());
    if (action === 'resume') delayed.context._JS_Video_Play(1, true);
    completions[0](); await flush();
    check(video.frames === 0, 'Late initial success after ' + action + ' cannot schedule a frame');
    if (action === 'resume') {
      completions[1](); await flush();
      check(video.frameRequests.size === 1 && video.muted,
        'Current initial success after resume retains readiness and requested mute');
      delayed.context._JS_Video_Pause(1);
    }
  }
  for (const lifecycle of ['pause', 'destroy', 'replace', 'resume']) {
    const frames = environment(), ready = decoder(() => Promise.resolve());
    frames.context.videoInstances[1] = ready;
    frames.context._JS_Video_Play(1, true); await flush();
    check(ready.frameRequests.size === 1, 'Successful initial play owns one cancellable frame request');
    const oldFrame = ready.frameCallback;
    if (lifecycle === 'pause') frames.context._JS_Video_Pause(1);
    else if (lifecycle === 'destroy') frames.context._JS_Video_Destroy(1);
    else if (lifecycle === 'replace') {
      frames.context._JS_Video_Destroy(1);
      frames.context.videoInstances[1] = decoder(() => Promise.resolve());
    } else { frames.context._JS_Video_Pause(1); frames.context._JS_Video_Play(1, false); await flush(); }
    check(ready.frameRequests.size === (lifecycle === 'resume' ? 1 : 0),
      lifecycle + ' cancels the previous native frame request');
    oldFrame(); check(!ready.isLoaded, 'Late cancelled frame after ' + lifecycle + ' cannot mark ready');
    if (lifecycle === 'resume') {
      ready.frameCallback(); check(ready.isLoaded && ready.frameRequests.size === 0,
        'Current resumed frame still becomes ready');
    }
  }
  const capped = environment(), legacyFrames = decoder(() => undefined);
  capped.context.videoInstances[1] = legacyFrames;
  for (let i = 0; i < 600; i++) capped.context._JS_Video_Play(1, false);
  check(legacyFrames.frameRequests.size === 1, 'Repeated legacy successful plays retain one frame callback');
  capped.context._JS_Video_Pause(1);
  check(legacyFrames.frameRequests.size === 0, 'Pause cancels the remaining native callback');
  const sourceBeforeDestroy = helper(engine, 'JS_Video_Destroy:');
  check(sourceBeforeDestroy.includes('delete videoInstances[video]'), 'Actual Unity destroy deletes identity used by initial intent check');
  c.jsVideoPlayPendingBlockedVideo(999); check(env.listeners.length === 0, 'Missing decoder retry is harmless');

  const tap = environment(); let tapAttempts = 0;
  const tapVideo = decoder(() => {
    tapAttempts++;
    return tap.context.fixtureGestureType === 'touchend' ? Promise.resolve() : Promise.reject(denial());
  });
  tap.context.videoInstances[1] = tapVideo;
  tap.context._JS_Video_Play(1, false); await flush();
  check(tap.context.jsVideoPendingBlockedVideos[1] === tapVideo, 'Native initial permission denial queues a completed-tap retry');
  tap.gesture('touchstart'); await flush();
  check(tapAttempts === 2 && tap.context.jsVideoPendingBlockedVideos[1] === tapVideo, 'A host that rejects touchstart remains safely queued');
  tap.gesture('touchend');
  check(tapAttempts === 3, 'Native play runs synchronously in the completed touchend gesture without a mouse event');
  await flush();
  check(!tap.context.jsVideoPendingBlockedVideos[1] && tapVideo.frames > 0 && tap.listeners.length === 0,
    'Completed tap recovers permission and removes every listener');
  const lateTapFrame = tapVideo.frameCallback;
  tap.context._JS_Video_Pause(1);
  check(tapVideo.frameRequests.size === 0, 'Pause cancels frame callback after gesture success');
  lateTapFrame(); check(!tapVideo.isLoaded, 'Late successful gesture frame cannot mark a paused decoder ready');

  for (const type of ['pointerup', 'keydown', 'click']) {
    const input = environment(); let calls = 0;
    const video = decoder(() => { calls++; return Promise.resolve(); }); queue(input, 1, video);
    input.gesture(type); check(calls === 1, type + ' performs native play in the trusted event stack');
    await flush(); check(input.listeners.length === 0, type + ' success cleans all listeners');
  }
  const excluded = environment(); let excludedCalls = 0;
  queue(excluded, 1, decoder(() => { excludedCalls++; return Promise.resolve(); }));
  for (const [type, fields] of [
    ['touchend', { isTrusted: false }], ['click', { isTrusted: false }],
    ['pointerup', { pointerType: 'mouse' }], ['keydown', { key: 'Escape' }],
    ['keydown', { key: 'Shift' }], ['keydown', { key: 'Control' }],
    ['keydown', { key: 'Alt' }], ['keydown', { key: 'Meta' }],
    ['keydown', { key: 'CapsLock' }], ['keydown', { key: 'r', ctrlKey: true }],
    ['keydown', { key: 'r', metaKey: true }], ['keydown', { key: 'Enter', altKey: true }],
    ['keydown', { key: 'w', repeat: true }]
  ]) {
    excluded.gesture(type, fields); check(excludedCalls === 0, type + ' ' + JSON.stringify(fields) + ' does not retry playback');
  }
  excluded.context._JS_Video_Pause(1);
  check(excluded.listeners.length === 0 && excluded.removals.every(item => item.capture === true), 'Pause removes every capture gesture listener');

  for (const action of ['pause', 'destroy']) {
    const lifecycle = environment(); let finish, calls = 0;
    const video = decoder(() => { calls++; return new Promise(resolve => { finish = resolve; }); });
    queue(lifecycle, 1, video);
    lifecycle.gesture('touchend'); lifecycle.gesture('pointerup'); lifecycle.gesture('click'); lifecycle.gesture('keydown');
    check(calls === 1, 'One tap producing multiple event types keeps a single in-flight retry');
    lifecycle.context[action === 'pause' ? '_JS_Video_Pause' : '_JS_Video_Destroy'](1);
    check(lifecycle.listeners.length === 0, action + ' removes all completed-tap listeners');
    finish(); await flush();
    check(video.frames === 0 && !lifecycle.context.jsVideoPendingBlockedVideos[1], action + ' prevents a completed-tap promise from resurrecting playback');
  }
  check(unhandled.length === 0, 'No unhandled promise rejection or escaped retry error');
  console.log(JSON.stringify({ passed: true, checks, framework: !!framework,
    scope: 'production WebGL bridge plus actual pinned Unity helpers, bounded initial and gesture attempts/frame callbacks, trusted completed-tap recovery and stale lifecycle cancellation; no physical iPhone/TikTok WebView validation' }));
}
const unhandled = [];
process.on('unhandledRejection', error => unhandled.push(error));
main().catch(error => { console.error(error); process.exitCode = 1; });
