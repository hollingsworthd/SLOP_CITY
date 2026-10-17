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
  class Document {
    constructor() { this.body = { appendChild(node) { nodes.push(node); } }; }
    createElement(tag) {
      if (tag === 'video') return {
        tagName: 'VIDEO', namespaceURI: 'http://www.w3.org/1999/xhtml', rawSource: null,
        currentSrc: '', currentTime: 0, duration: 59, volume: 1, muted: true,
        paused: true, loop: true, readyState: 4, videoWidth: 320, videoHeight: 480,
        playsInline: true, isConnected: false,
        getAttribute(name) { assert.equal(name, 'src'); return this.rawSource; }
      };
      assert.equal(tag, 'pre');
      return { style: { setProperty() {} }, setAttribute(name, value) { this[name] = value; } };
    }
    createElementNS(namespace, tag) { return this.createElement(tag); }
    querySelectorAll() { return []; }
  }
  const nativeCreate = Document.prototype.createElement, nativeCreateNS = Document.prototype.createElementNS;
  const document = new Document();
  const window = { Document, location: { search: query }, setInterval(fn, ms) { timers.push({ fn, ms }); } };
  if (weak) window.WeakRef = WeakRef;
  const context = vm.createContext({ window, document, URLSearchParams, Date });
  vm.runInContext(baseline ? source.replace('      if (video.slopCityDestroyed === true) { entries.delete(id); return; }\n', '') : source, context);
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
  return { document, window, nodes, timers, sample, video, retire, nativeCreate, nativeCreateNS, Document };
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
}
for (const query of ['', '?qa=0', '?qa=true', '?qa=01']) {
  const env = environment({ query, weak: false });
  const decoder = env.video('ordinary'); env.retire(decoder);
  check(env.Document.prototype.createElement === env.nativeCreate
    && env.Document.prototype.createElementNS === env.nativeCreateNS
    && env.nodes.length === 0 && env.timers.length === 0 && !env.window.slopCityMediaDiagnosticsVersion,
    query + ' installs no telemetry hooks, interval, snapshot or decoder tracking');
}
console.log(JSON.stringify({ passed: true, checks,
  scope: 'QA-only stale currentSrc baseline reproduction and explicit destroyed-decoder pruning, with/without WeakRef over 1000 cycles each; active playback unchanged and no ordinary-play hooks; no measured browser RAM' }));
