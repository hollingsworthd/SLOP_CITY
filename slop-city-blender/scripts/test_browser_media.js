#!/usr/bin/env node
// Exercise host capability, permission recovery and clipboard guidance without
// claiming to emulate TikTok's native WebView configuration.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../Unity/Assets/WebGLTemplates/SlopCity/slop-city-browser-media.js'), 'utf8');
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }
function setup(inline, clipboard) {
  const ids = ['media-support', 'media-support-title', 'media-support-message', 'media-help', 'media-copy-status', 'media-copy', 'media-dismiss', 'stage', 'unity-canvas'];
  const elements = Object.fromEntries(ids.map(id => [id, {
    hidden: id === 'media-support', textContent: '', events: {}, attributes: {},
    classList: { add(value) { this.value = value; } },
    setAttribute(name, value) { this.attributes[name] = value; },
    addEventListener(name, fn) { this.events[name] = fn; },
    appendChild(node) { this.child = node; }, focus() { this.focused = true; }
  }]));
  const events = {};
  const document = { getElementById(id) { return elements[id]; }, addEventListener(name, fn) { events[name] = fn; } };
  vm.runInNewContext(source, { window: { slopCityCanPlayInlineVideo: () => inline }, document, navigator: { clipboard } });
  return { elements, event: (name, detail) => events[name]({detail}), click: id => elements[id].events.click() };
}
(async () => {
  let env = setup(false);
  let e = env.elements;
  check(!e['media-support'].hidden, 'Inline-only host refusal explained before downloading game');
  check(e['media-support-title'].textContent.includes('Safari or Chrome'), 'Fallback names ordinary browsers');
  check(e['media-support-message'].textContent.includes('still explore'), 'No forced redirect or game block');
  check(e['media-help'].attributes['aria-expanded'] === 'true', 'Initial help expanded state');
  env.click('media-dismiss');
  check(e['media-support'].hidden, 'Player can dismiss and continue');
  env.event('slopcityready');
  check(e.stage.child === e['media-support'] && e['media-support'].classList.value === 'floating', 'Help remains reachable outside hidden launch gate');
  env.click('media-help');
  check(!e['media-support'].hidden, 'Footer reopens in-game help');
  env.event('slopcityvideoplayback', {pendingCount: 1});
  check(e['media-help'].textContent.includes('blocked'), 'Blocked inline host never mislabeled tap-recoverable');
  env.event('slopcityvideoplayback', {pendingCount: 0});
  check(!e['media-support'].hidden, 'Queue empty does not hide inline restriction');
  await env.click('media-copy');
  check(e['media-copy-status'].textContent.includes('Type SLOPCITY.org'), 'Unavailable clipboard supplies manual address');

  let copied;
  env = setup(true, {writeText: async text => { copied = text; }}); e = env.elements;
  check(e['media-support'].hidden, 'Supported browsers start without warning');
  env.event('slopcityready');
  env.event('slopcityvideoplayback', {pendingCount: 2});
  check(e['media-help'].textContent === 'Enable media', 'Recoverable queue offers real gesture control');
  env.click('media-help');
  check(!e['media-support'].hidden, 'Pending help can be read');
  env.event('slopcityvideoplayback', {pendingCount: 0});
  check(e['media-support'].hidden && e['media-help'].textContent === 'Media help', 'Cleared queue removes pending UI');
  env.event('slopcityvideoblocked');
  check(!e['media-support'].hidden, 'Unexpected presentation refusal opens useful guidance once');
  env.click('media-dismiss');
  env.event('slopcityvideoblocked');
  check(e['media-support'].hidden, 'Further blocked decoders do not reopen dismissed help');
  check(e['unity-canvas'].focused, 'Dismiss returns focus to game');
  await env.click('media-copy');
  check(copied === 'https://slopcity.org/', 'Copies canonical public URL without debug parameters');
  check(e['media-copy-status'].textContent.startsWith('Copied.'), 'Copy success feedback');
  env = setup(true, {writeText: async () => { throw new Error('NotAllowedError'); }});
  await env.click('media-copy');
  check(env.elements['media-copy-status'].textContent.includes('Type SLOPCITY.org'), 'Clipboard permission denial is contained');
  console.log(JSON.stringify({passed: true, checks, scope: 'Capability-based help and recoverable-permission UI; not a physical TikTok WebView test'}));
})().catch(error => { console.error(error); process.exitCode = 1; });
