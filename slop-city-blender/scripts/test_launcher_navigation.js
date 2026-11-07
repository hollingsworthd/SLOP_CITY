#!/usr/bin/env node
"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(path.join(__dirname, "../Unity/Assets/WebGLTemplates/SlopCity/index.html"), "utf8");
assert.match(template, /#stage \{[^}]*padding-left: env\(safe-area-inset-left\)[^}]*padding-right: env\(safe-area-inset-right\)/,
  "the normal WebGL stage must keep touch controls inside horizontal safe areas");
assert.match(template, /#stage:fullscreen \{[^}]*padding: env\(safe-area-inset-top\) env\(safe-area-inset-right\) env\(safe-area-inset-bottom\) env\(safe-area-inset-left\)/,
  "the fullscreen WebGL stage must keep touch controls inside every safe-area edge");
assert.match(template, /#unity-canvas \{[^}]*width: 100%; height: 100%/,
  "the canvas must fill the safe stage so its resize path measures usable geometry");
const scripts = [...template.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)].filter(match => !/\bsrc=/.test(match[1]));
const launcher = scripts.find(match => match[2].includes('const canvas = document.getElementById("unity-canvas")'));
assert.ok(launcher, "the real Unity launcher inline script must be present");
const source = launcher[2].replace(/^#.*$/gm, "").replace(/\{\{\{[\s\S]*?\}\}\}/g, token => token.includes("JSON.stringify") ? '"fixture"' : "fixture");

function harness({ cleanupThrows = false, reloadThrows = false, foreignContext = false, graphicsThrows = false } = {}) {
  const calls = [], listeners = new Map(), nodes = new Map(), intervals = new Map();
  let nextInterval = 1, resolveInstance;
  const state = { config: null, reloads: 0, readyEvents: 0 };
  function node(id) {
    if (nodes.has(id)) return nodes.get(id);
    const events = new Map();
    const item = { id, hidden: false, disabled: false, value: "720", textContent: "", style: {}, dataset: {},
      classList: { toggle() {}, add() {} }, setAttribute() {}, focus() {}, remove() {},
      getBoundingClientRect() { return { width: 640, height: 360 }; },
      getContext() { return { MAX_VIEWPORT_DIMS: 1, MAX_TEXTURE_SIZE: 2, MAX_RENDERBUFFER_SIZE: 3,
        getParameter(key) { return key === 1 ? [8192, 8192] : 8192; }, getExtension() { return { loseContext() {} }; } }; },
      addEventListener(type, callback) { events.set(type, callback); },
      fire(type, event = {}) { if (events.has(type)) return events.get(type).call(item, event); }
    };
    nodes.set(id, item);
    return item;
  }
  const document = { hidden: false, documentElement: node("html"), body: { appendChild(item) { state.loaderScript = item; } },
    getElementById: node, querySelector() { return node("query"); },
    createElement(name) { return node("created-" + name + "-" + nodes.size); },
    addEventListener() {}, removeEventListener() {}, dispatchEvent(event) { if (event.type === "slopcityready") state.readyEvents++; } };
  const context = vm.createContext({ document, navigator: { maxTouchPoints: 0 }, location: {
    search: "", protocol: "https:", reload() { state.reloads++; if (reloadThrows) throw new Error("reload rejected"); }
  }, console: { warn(message) { calls.push("warning:" + message); }, error(message) { calls.push("error:" + message); } },
    setInterval(fn, ms) { const id = nextInterval++; intervals.set(id, { fn, ms }); return id; },
    clearInterval(id) { intervals.delete(id); calls.push("clear-watch"); },
    requestAnimationFrame() { return 0; },
    cancelAnimationFrame() {},
    WebAssembly: {}, fetch() {}, ReadableStream: function () {}, AbortController: function () {}, Worker: function () {},
    CustomEvent: class { constructor(type) { this.type = type; } },
    createUnityInstance(canvas, config) { state.config = config; return new Promise(resolve => { resolveInstance = resolve; }); }
  });
  context.window = context;
  context.devicePixelRatio = 1;
  context.slopCityInlineVideoPolicyVersion = 3;
  context.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  context.addEventListener = (type, callback) => listeners.set(type, callback);
  context.removeEventListener = (type, callback) => { if (listeners.get(type) === callback) listeners.delete(type); };
  context.slopCityChunks = { abortAll() { calls.push("abort-downloads"); if (cleanupThrows) throw new Error("download cleanup failed"); } };
  context.slopCityStopStartup = () => { calls.push("abort-startup"); if (cleanupThrows) throw new Error("startup cleanup failed"); };
  vm.runInContext(source, context, { filename: "SlopCity/index.html" });
  const module = { ctx: { canvas: foreignContext ? node("unrelated-canvas") : node("unity-canvas"),
      getExtension(name) { assert.equal(name, "WEBGL_lose_context"); calls.push("owned-context-extension");
        if (graphicsThrows) throw new Error("graphics cleanup failed");
        return { loseContext() { calls.push("lose-owned-context"); } }; } },
    slopCityDisposeOwnedVideoDecoders() { calls.push("dispose-owned-videos"); if (cleanupThrows) throw new Error("video cleanup failed"); },
    pauseMainLoop() { calls.push("pause-frames"); } };
  const instance = { Module: module, Quit() { calls.push("quit-needs-frame"); return new Promise(() => {}); } };
  return { calls, state, module, nodes, intervals,
    emit(type, event = {}) { listeners.get(type)(event); },
    launch({ deferLoader = false } = {}) { node("launch").fire("click"); assert.equal(intervals.size, 1); if (!deferLoader) { state.loaderScript.onload(); assert.ok(state.config); } },
    loadScript() { state.loaderScript.onload?.(); },
    initialize() { state.config.onRuntimeInitialized.call(module); },
    async complete() { resolveInstance(instance); await Promise.resolve(); await Promise.resolve(); }
  };
}

(async function () {
  const idle = harness();
  idle.emit("pagehide", { persisted: true }); idle.emit("pageshow", { persisted: true });
  assert.deepEqual(idle.calls, ["abort-downloads", "abort-startup"]); assert.equal(idle.state.reloads, 0);
  idle.launch();

  const beforeLoader = harness(); beforeLoader.launch({ deferLoader: true }); beforeLoader.emit("pagehide"); beforeLoader.loadScript();
  assert.equal(beforeLoader.state.config, null, "late loader must not start downloads or Unity");

  const pending = harness(); pending.launch(); pending.emit("pagehide");
  assert.equal(pending.intervals.size, 0);
  assert.deepEqual(pending.calls, ["abort-downloads", "clear-watch", "abort-startup"]);
  assert.throws(() => pending.initialize(), /cancelled because this page was left/);
  assert.deepEqual(pending.calls.slice(-4), ["dispose-owned-videos", "pause-frames", "owned-context-extension", "lose-owned-context"]);
  await pending.complete();
  assert.equal(pending.state.readyEvents, 0);
  assert.equal(pending.calls.filter(call => call === "dispose-owned-videos").length, 1);
  assert.ok(!pending.calls.includes("quit-needs-frame"));

  const opening = harness(); opening.launch(); opening.initialize(); opening.emit("pagehide");
  assert.deepEqual(opening.calls.slice(-6), ["abort-startup", "abort-downloads", "dispose-owned-videos", "pause-frames", "owned-context-extension", "lose-owned-context"]);
  await opening.complete(); assert.equal(opening.state.readyEvents, 0);

  const running = harness(); running.launch(); running.initialize(); await running.complete();
  assert.equal(running.state.readyEvents, 1);
  running.calls.length = 0;
  running.emit("pagehide", { persisted: true }); running.emit("pagehide", { persisted: true });
  assert.deepEqual(running.calls, ["abort-downloads", "abort-startup", "abort-startup", "abort-downloads", "dispose-owned-videos", "pause-frames", "owned-context-extension", "lose-owned-context", "abort-downloads"]);
  running.emit("pageshow", { persisted: false }); assert.equal(running.state.reloads, 0);
  running.emit("pageshow", { persisted: true }); assert.equal(running.state.reloads, 1);
  assert.equal(running.nodes.get("gate").hidden, false);
  assert.equal(running.nodes.get("launch").hidden, true);
  assert.equal(running.nodes.get("fullscreen").disabled, true);
  assert.ok(!running.calls.includes("quit-needs-frame"));

  const unrelated = harness({ foreignContext: true }); unrelated.launch(); unrelated.initialize(); await unrelated.complete(); unrelated.emit("pagehide");
  assert.ok(!unrelated.calls.includes("owned-context-extension"));
  assert.ok(!unrelated.calls.includes("lose-owned-context"));
  const graphicsFailure = harness({ graphicsThrows: true }); graphicsFailure.launch(); graphicsFailure.initialize(); await graphicsFailure.complete(); graphicsFailure.emit("pagehide");
  assert.ok(graphicsFailure.calls.includes("pause-frames"));
  assert.ok(graphicsFailure.calls.includes("warning:Navigation graphics cleanup failed."));
  graphicsFailure.emit("pageshow", { persisted: true }); assert.equal(graphicsFailure.state.reloads, 1);

  const failing = harness({ cleanupThrows: true, reloadThrows: true });
  failing.launch(); failing.initialize(); await failing.complete(); failing.emit("pagehide");
  assert.ok(failing.calls.includes("abort-startup"));
  assert.ok(failing.calls.includes("dispose-owned-videos"));
  assert.ok(failing.calls.includes("pause-frames"));
  assert.equal(failing.calls.filter(call => call.startsWith("warning:")).length, 5);
  failing.emit("pageshow", { persisted: true });
  assert.equal(failing.nodes.get("error").hidden, false);
  assert.match(failing.nodes.get("error-message").textContent, /Reload the page/);
  assert.match(failing.nodes.get("error-detail").textContent, /reload rejected/);
  console.log("PASS: full launcher navigation, pending/late startup, owned teardown, cached return and cleanup fault injection.");
})().catch(error => { console.error(error); process.exitCode = 1; });
