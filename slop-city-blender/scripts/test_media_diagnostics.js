#!/usr/bin/env node
// QA must release retired detached decoders even if a host retains currentSrc.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Unity/Assets/WebGLTemplates/SlopCity/slop-city-media-diagnostics.js'), 'utf8');
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
function environment({ weak = true, query = '?qa=1', baseline = false } = {}) {
  const nodes = [], timers = [];
  let clock = 0, nativeSourceSets = 0;
  class MediaElement {}
  Object.defineProperty(MediaElement.prototype, 'src', {
    configurable: true, enumerable: true,
    get() { return this.rawSource; },
    set(value) { nativeSourceSets++; this.rawSource = this.currentSrc = String(value); }
  });
  const nativeSourceSetter = Object.getOwnPropertyDescriptor(MediaElement.prototype, 'src').set;
  class Clock extends Date {
    constructor(...args) { super(...(args.length ? args : [clock])); }
    static now() { return clock; }
  }
  class Document {
    constructor() { this.body = { appendChild(node) { nodes.push(node); } }; }
    createElement(tag) {
      if (tag === 'video') return Object.assign(Object.create(MediaElement.prototype), {
        tagName: 'VIDEO', namespaceURI: 'http://www.w3.org/1999/xhtml', rawSource: null,
        currentSrc: '', currentTime: 0, duration: 59, volume: 1, muted: true,
        paused: true, loop: true, readyState: 4, videoWidth: 320, videoHeight: 480,
        playsInline: true, isConnected: false,
        getAttribute(name) { assert.equal(name, 'src'); return this.rawSource; }
      });
      assert.equal(tag, 'pre');
      return { style: { setProperty() {} }, setAttribute(name, value) { this[name] = value; } };
    }
    createElementNS(namespace, tag) { return this.createElement(tag); }
    querySelectorAll() { return []; }
  }
  const nativeCreate = Document.prototype.createElement, nativeCreateNS = Document.prototype.createElementNS;
  const document = new Document();
  const window = { Document, HTMLMediaElement: MediaElement, location: { search: query }, setInterval(fn, ms) { timers.push({ fn, ms }); } };
  if (weak) window.WeakRef = WeakRef;
  const context = vm.createContext({ window, document, URLSearchParams, Date: Clock });
  vm.runInContext(baseline ? source.replace('      if (video.slopCityDestroyed === true) { untrack(video, id); return; }\n', '') : source, context);
  function sample() { timers.forEach(timer => timer.fn()); return nodes.length ? JSON.parse(nodes[0].textContent) : null; }
  function video(name) {
    const decoder = document.createElement('video');
    decoder.rawSource = decoder.currentSrc = 'StreamingAssets/' + name + '.mp4';
    return decoder;
  }
  function retire(decoder) {
    decoder.slopCityDestroyed = true;
    decoder.rawSource = null;
    // Mirrors the actual IAB: currentSrc still names the old source after load.
    decoder.readyState = 0; decoder.videoWidth = decoder.videoHeight = 0;
  }
  return { document, window, nodes, timers, sample, video, retire, nativeCreate, nativeCreateNS, Document, MediaElement,
    nativeSourceSetter, sourceSets: () => nativeSourceSets, advance: ms => { clock += ms; } };
}
for (const weak of [false, true]) {
  const old = environment({ weak, baseline: true });
  const retired = old.video('old'); old.sample(); old.retire(retired);
  check(old.sample().count === 1, 'Previous telemetry retains destroyed stale-currentSrc entry with WeakRef=' + weak);
  const env = environment({ weak });
  const active = Array.from({ length: 4 }, (_, i) => env.video('active-' + i));
  check(env.sample().count === 4, 'QA tracks four active detached decoders with WeakRef=' + weak);
  const garbage = [];
  for (let cycle = 0; cycle < 1000; cycle++) {
    const decoder = env.video('retired-' + cycle);
    garbage.push(decoder);
    check(env.sample().count === 5, 'A newly created source is tracked before its retirement');
    env.retire(decoder);
    check(env.sample().count === 4, 'Destroyed decoder is pruned without waiting for currentSrc or garbage collection');
  }
  // External references keep old decoders alive deterministically in both modes.
  // Mutating those objects afterward cannot change a registry that removed them.
  for (const decoder of garbage) { decoder.slopCityDestroyed = false; decoder.rawSource = decoder.currentSrc; }
  check(env.sample().count === 4, 'Retired entries were deleted, including strong-reference fallback entries');
  check(env.nodes.length === 1 && env.timers.length === 1 && env.timers[0].ms === 250,
    'Repeated QA sampling retains one overwritten snapshot and one sampling interval');
  check(active.every(decoder => decoder.currentSrc === decoder.rawSource && decoder.readyState === 4),
    'Telemetry leaves active playback state untouched');
  const cleared = env.video('legacy-destroy'); env.sample(); cleared.rawSource = '';
  check(env.sample().count === 4, 'Legacy raw src empty teardown remains pruned');

  const reused = env.video('same-shell');
  for (let cycle = 0; cycle < 1000; cycle++) {
    check(env.sample().count === 5, 'Reused source has exactly one registry entry');
    env.retire(reused); env.advance(500);
    check(env.sample().count === 4, 'Source-free shell is pruned after more than one sampling interval');
    reused.src = ''; // Teardown cannot re-register a destroyed, empty shell.
    check(env.sample().count === 4, 'Retired empty source assignment stays untracked');
    delete reused.slopCityDestroyed;
    reused.src = 'StreamingAssets/reused-' + cycle + '.mp4';
    reused.src = reused.src; // Repeat assignments must not duplicate tracking.
    const snapshot = env.sample();
    check(snapshot.count === 5 && snapshot.videos.some(row => row.src === 'reused-' + cycle + '.mp4'),
      'Same element source reuse restores truthful QA output without duplicate entries');
  }
  check(env.sourceSets() === 3000, 'Tracking preserves each underlying source setter call exactly once');
  env.retire(reused); env.advance(500);
  check(env.sample().count === 4 && env.nodes.length === 1 && env.timers.length === 1,
    'Repeated same-element reuse returns to the bounded active registry and one sampler');
}
for (const query of ['', '?qa=0', '?qa=true', '?qa=01']) {
  const env = environment({ query, weak: false });
  const decoder = env.video('ordinary'); env.retire(decoder);
  check(env.Document.prototype.createElement === env.nativeCreate
    && env.Document.prototype.createElementNS === env.nativeCreateNS
    && Object.getOwnPropertyDescriptor(env.MediaElement.prototype, 'src').set === env.nativeSourceSetter
    && env.nodes.length === 0 && env.timers.length === 0 && !env.window.slopCityMediaDiagnosticsVersion,
    query + ' installs no telemetry hooks, interval, snapshot or decoder tracking');
}
console.log(JSON.stringify({ passed: true, checks,
  scope: 'QA-only stale currentSrc pruning and same-element reuse after500ms over1000cycles each, with/without WeakRef; bounded active registry, chained source setter and no ordinary-play hooks; no measured browser RAM' }));
