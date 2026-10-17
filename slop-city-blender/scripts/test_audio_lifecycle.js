#!/usr/bin/env node
'use strict';
// Run pinned Unity Audio.js stop/play/release and context-state handler with the
// project patch. Only native audio nodes are mocked; queue processing is real.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const zlib = require('node:zlib');
const engine = fs.readFileSync(path.resolve(__dirname, '../../.tools/Unity-6000.3.26f1/Unity.app/Contents/PlaybackEngines/WebGLSupport/BuildTools/lib/Audio.js'), 'utf8');
const plugin = fs.readFileSync(path.resolve(__dirname, '../Unity/Assets/Plugins/WebGL/SlopCityAudioLifecycle.jslib'), 'utf8');
const frameworkFlag = process.argv.indexOf('--framework');
const framework = frameworkFlag < 0 ? null : zlib.gunzipSync(fs.readFileSync(process.argv[frameworkFlag + 1])).toString();
function helper(marker, source = engine) {
  assert.ok(source.includes(marker), 'Missing helper ' + marker);
  const start = source.indexOf('function', source.indexOf(marker)), open = source.indexOf('{', start);
  let depth = 0, quote = '', comment = '';
  for (let i = open; i < source.length; i++) {
    const c = source[i], n = source[i + 1];
    if (comment === 'line') { if (c === '\n') comment = ''; continue; }
    if (comment === 'block') { if (c === '*' && n === '/') { comment = ''; i++; } continue; }
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = ''; continue; }
    if (c === '/' && n === '/') { comment = 'line'; i++; continue; }
    if (c === '/' && n === '*') { comment = 'block'; i++; continue; }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '{') depth++; else if (c === '}' && --depth === 0) return source.slice(start, i + 1);
  }
  throw Error('Unclosed ' + marker);
}
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
function environment(patched = true) {
  let time = 0;
  const audioContext = { state: 'suspended', currentTime: 0, resume: () => Promise.resolve() };
  const context = { WEBAudio: { audioInstances: {}, audioWebEnabled: 1, soundsPendingContextResume: [], audioCache: [] },
    window: { addEventListener() {}, removeEventListener() {} }, AudioContext: function () { return audioContext; },
    Audio: function () {}, _GetFakemodTimeInSeconds: () => time,
    Module: { setInterval() {}, clearInterval() {}, deinitializers: [] }, console: { log() {}, error() {} }, alert: assert.fail,
    LibraryManager: { library: {} }, mergeInto: (target, values) => Object.assign(target, values) };
  vm.createContext(context);
  for (const name of ['Init', 'Stop', 'Play', 'ReleaseInstance']) {
    context.LibraryManager.library['JS_Sound_' + name] = vm.runInContext('(' + helper('JS_Sound_' + name + ':') + ')', context);
    context.LibraryManager.library['JS_Sound_' + name + '__deps'] = ['$WEBAudio'];
  }
  if (patched) {
    vm.runInContext(plugin, context);
    for (const [name, value] of Object.entries(context.LibraryManager.library))
      if (name.startsWith('$') && !name.includes('__')) context[name.slice(1)] = value;
    if (framework) {
      for (const name of ['slopCityNativeSoundStop', 'slopCityNativeSoundRelease', 'slopCityRemovePendingSound'])
        vm.runInContext(name + '=' + helper('function ' + name + '(', framework) + ';', context);
      for (const name of ['Init', 'Stop', 'Play', 'ReleaseInstance'])
        context.LibraryManager.library['JS_Sound_' + name] = vm.runInContext('('
          + helper('function _JS_Sound_' + name + '(', framework) + ')', context);
    }
  }
  for (const name of ['Init', 'Stop', 'Play', 'ReleaseInstance']) context['_JS_Sound_' + name] = context.LibraryManager.library['JS_Sound_' + name];
  context._JS_Sound_Init();
  function state(next, seconds = 1) { time += seconds; audioContext.currentTime += seconds; audioContext.state = next; audioContext.onstatechange(); }
  const events = [];
  vm.runInContext('nativeChannelStopFactory=function(channel){channel.stop='
    + helper('channel.stop = function') + ';channel.disconnectSource='
    + helper('channel.disconnectSource = function') + ';return channel;};', context);
  function channel(id) {
    const result = context.nativeChannelStopFactory({ source: null,
      playSoundClip(clip, start, offset) { events.push(['play', id, clip, start, offset]); this.source = { buffer: clip.buffer,
        playbackPausedAtPosition: offset, stop(when) { events.push(['stop', id, when - audioContext.currentTime]); }, disconnect() {} }; },
      release() { events.push(['release', id]); this.source = null; },
      isPaused() { return !this.source || this.source.paused; },
      pause() { this.source.paused = true; this.source.isPausedMockNode = true; },
      resume(offset) { events.push(['resume', id, offset]); this.source.paused = false; this.source.isPausedMockNode = false; } });
    return context.WEBAudio.audioInstances[id] = result;
  }
  function clip(id) { return context.WEBAudio.audioInstances[id] = { buffer: { pcm: id }, release() { events.push(['releaseClip', id]); } }; }
  return { context, events, state, channel, clip, queue: context.WEBAudio.soundsPendingContextResume,
    stop: context._JS_Sound_Stop, play: context._JS_Sound_Play, release: context._JS_Sound_ReleaseInstance };
}
const baseline = environment(false); baseline.channel(2); baseline.clip(1);
for (let i = 0; i < 100; i++) { baseline.play(1, 2, 0, 0); baseline.stop(2, 0); }
baseline.release(1); baseline.release(2);
check(baseline.queue.length === 100, 'Pinned stopped/released suspended audio retains 100 queue entries');
const fixed = environment(); fixed.channel(2); fixed.clip(1);
for (let i = 0; i < 100; i++) { fixed.play(1, 2, 0, 0); fixed.stop(2, 0); }
check(fixed.queue.length === 0, 'Stopped suspended audio retains no queue entries');
fixed.play(1, 2, 4, 2); fixed.play(1, 2, 5, 3);
check(fixed.queue.length === 1 && fixed.queue[0].offset === 5, 'Channel replacement keeps only the latest queued playback');
fixed.release(1);
check(fixed.queue.length === 0, 'Releasing clip prunes every pending reference');
fixed.state('running');
check(!fixed.events.some(e => e[0] === 'play'), 'Released clip never starts at resume');
const multiple = environment(); const ch = multiple.channel(2), clip = multiple.clip(1);
const other = multiple.channel(3); multiple.clip(4);
for (let i = 0; i < 3; i++) multiple.queue.push({ channel: ch, clip, stopDelay: -1, offset: i, startTime: 0 });
multiple.play(4, 3, 0, 0); multiple.release(2);
check(multiple.queue.length === 1 && multiple.queue[0].channel === other, 'Channel release prunes all entries while preserving another channel');
multiple.state('running');
check(multiple.events.filter(e => e[0] === 'play').length === 1, 'Surviving channel starts normally');
const interrupted = environment(); const paused = interrupted.channel(2); interrupted.clip(1);
interrupted.state('running'); interrupted.play(1, 2, 2, 0); interrupted.state('interrupted');
check(interrupted.queue.length === 1 && paused.source.paused, 'Pinned interruption pauses and queues active audio');
interrupted.stop(2, 0);
check(interrupted.queue.length === 0 && !paused.source, 'Immediate stop frees paused-source buffer as well as queued references');
interrupted.state('running');
check(!interrupted.events.some(e => e[0] === 'resume'), 'Stopped interrupted audio never resumes');
const delayed = environment(); delayed.channel(2); delayed.clip(1); delayed.play(1, 2, 0, 0); delayed.stop(2, 10);
delayed.state('running', 2);
check(delayed.events.some(e => e[0] === 'play') && delayed.events.some(e => e[0] === 'stop' && e[2] === 8), 'Delayed stop still accounts for suspended time at resume');
const normal = environment(); normal.channel(2); normal.clip(1); normal.state('running'); normal.play(1, 2, 7, 2); normal.stop(2, 3);
check(normal.events.some(e => e[0] === 'play' && e[4] === 7) && normal.events.some(e => e[0] === 'stop' && e[2] === 3), 'Running playback and delayed stop preserve native behavior');
normal.state('suspended'); normal.state('running');
check(normal.events.some(e => e[0] === 'resume'), 'Healthy background/foreground resumes through pinned context handler');
normal.stop(999, 0); normal.release(999); check(true, 'Late stop/release after removal is harmless');
console.log(JSON.stringify({ passed: true, checks, framework: !!framework,
  scope: 'Pinned Unity suspended/interrupted queue paths; no physical iPhone validation' }));
