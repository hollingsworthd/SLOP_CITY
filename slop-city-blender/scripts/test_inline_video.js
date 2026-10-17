#!/usr/bin/env node
// Production guard regressions for Unity's detached decoder and WebKit hosts.
// This DOM fixture verifies dispatch, capabilities and presentation suppression;
// it is not a physical iPhone/iPad playback or rendering test.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const source = fs.readFileSync(path.join(__dirname,
  "../Unity/Assets/WebGLTemplates/SlopCity/slop-city-inline-video.js"), "utf8");
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }

function environment({ userAgent = "Mozilla/5.0 Chrome/141.0 AppleWebKit/537.36", inline = true, matchMediaThrows = false, failPatch = false } = {}) {
  const counts = { play: 0, pause: 0, fullscreen: 0, presentation: 0, pip: 0, remote: 0, exit: 0, source: 0 };
  const playResult = Promise.resolve("playing"), fullscreenResult = Promise.resolve("fullscreen");
  let playArgument;
  class Node {
    constructor() { this.children = []; this.parentNode = null; this.listeners = {}; }
    addEventListener(name, fn) { (this.listeners[name] ||= []).push(fn); }
    dispatch(name) { for (const fn of this.listeners[name] || []) fn(); }
    querySelectorAll(selector) {
      const results = [];
      function visit(node) {
        for (const child of node.children) {
          if (selector === "video" && child.localName === "video") results.push(child);
          visit(child);
        }
      }
      visit(this); return results;
    }
    cloneNode(deep) {
      const cloned = rawElement(this.localName || "div", this.namespaceURI);
      cloned.attributes = new Map(this.attributes || []);
      if (deep) cloned.children = this.children.map(child => child.cloneNode(true));
      return cloned;
    }
  }
  class Element extends Node {
    constructor(name, namespace = "http://www.w3.org/1999/xhtml") {
      super(); this.localName = name.toLowerCase(); this.namespaceURI = namespace; this.attributes = new Map();
      this.style = { setProperty(name, value, priority) { this[name] = value; this[name + "Priority"] = priority; } };
    }
    setAttribute(name, value) { this.attributes.set(String(name).toLowerCase(), String(value)); }
    setAttributeNS(namespace, name, value) { this.attributes.set(String(name).toLowerCase(), String(value)); }
    toggleAttribute(name, force) {
      const present = this.attributes.has(name);
      const add = arguments.length > 1 ? force : !present;
      if (add) this.attributes.set(name, ""); else this.attributes.delete(name);
      return add;
    }
    removeAttribute(name) { this.attributes.delete(String(name).toLowerCase()); }
    hasAttribute(name) { return this.attributes.has(name.toLowerCase()); }
    getAttribute(name) { return this.attributes.get(name.toLowerCase()); }
    requestFullscreen(argument) { counts.fullscreen++; this.fullscreenArgument = argument; return fullscreenResult; }
    webkitRequestFullscreen(argument) { counts.fullscreen++; this.fullscreenArgument = argument; return "legacy fullscreen"; }
    webkitRequestFullScreen(argument) { return this.webkitRequestFullscreen(argument); }
    set innerHTML(value) { this.html = value; this.children = [parsedVideo()]; this.children[0].parentNode = this; }
    get innerHTML() { return this.html; }
    set outerHTML(value) { this.html = value; this.parentNode.children = [parsedVideo()]; this.parentNode.children[0].parentNode = this.parentNode; }
    get outerHTML() { return this.html; }
    insertAdjacentHTML(position, html) { this.adjacentPosition = position; this.innerHTML = html; }
  }
  class HTMLMediaElement extends Element {
    constructor(name) { super(name); this.muted = false; this.volume = 0.65; }
    play(argument) {
      if (this.localName === "video") {
        check(this.playsInline && this.hasAttribute("webkit-playsinline"), "Inline before native play");
        check(!this.controls && !this.autoplay, "No native controls or autoplay before native play");
      }
      counts.play++; playArgument = argument;
      if (argument === "throw") throw new Error("native playback unavailable");
      if (argument === "denied") throw Object.assign(new Error("The request is not allowed."), {name:"NotAllowedError"});
      return playResult;
    }
    pause() { counts.pause++; }
    load() { this.loadCalled = true; }
    set src(value) {
      if (this.localName === "video") {
        check(this.playsInline && this.hasAttribute("webkit-playsinline"), "Inline before native source setter");
        counts.source++;
      }
      this.source = value;
    }
    get src() { return this.source; }
    set srcObject(value) { this.src = value; }
    get srcObject() { return this.source; }
  }
  function reflected(prototype, name, attribute) {
    Object.defineProperty(prototype, name, { configurable: true,
      get() { return this.attributes.has(attribute); },
      set(value) { if (value) this.attributes.set(attribute, ""); else this.attributes.delete(attribute); }
    });
  }
  reflected(HTMLMediaElement.prototype, "controls", "controls");
  reflected(HTMLMediaElement.prototype, "autoplay", "autoplay");
  reflected(HTMLMediaElement.prototype, "disableRemotePlayback", "disableremoteplayback");
  class HTMLVideoElement extends HTMLMediaElement {
    constructor() { super("video"); this.webkitPresentationMode = "inline"; }
    webkitEnterFullscreen() { counts.fullscreen++; this.webkitDisplayingFullscreen = true; }
    webkitEnterFullScreen() { counts.fullscreen++; this.webkitDisplayingFullscreen = true; }
    webkitExitFullscreen() { counts.exit++; this.webkitDisplayingFullscreen = false; }
    webkitSetPresentationMode(mode) { counts.presentation++; this.webkitPresentationMode = mode; }
    requestPictureInPicture() { counts.pip++; return Promise.resolve(); }
    webkitShowPlaybackTargetPicker() { counts.remote++; }
  }
  reflected(HTMLVideoElement.prototype, "playsInline", "playsinline");
  reflected(HTMLVideoElement.prototype, "disablePictureInPicture", "disablepictureinpicture");
  reflected(HTMLVideoElement.prototype, "webkitWirelessVideoPlaybackDisabled", "wirelessdisabled");
  function rawElement(name, namespace) {
    if (name.toLowerCase() === "video" && (!namespace || namespace === "http://www.w3.org/1999/xhtml")) return new HTMLVideoElement();
    if (name.toLowerCase() === "audio") return new HTMLMediaElement("audio");
    return new Element(name, namespace);
  }
  function parsedVideo() {
    const video = new HTMLVideoElement();
    // Parsing initializes native attributes without invoking JS setters.
    video.attributes.set("controls", ""); video.attributes.set("autoplay", ""); video.source = "parsed.mp4";
    return video;
  }
  class Document extends Node {
    constructor() { super(); this.documentElement = new Element("html"); this.children = [this.documentElement]; }
    createElement(name, options) { const element = rawElement(name); element.options = options; return element; }
    createElementNS(namespace, name, options) { const element = rawElement(name, namespace); element.options = options; return element; }
    exitFullscreen() { counts.exit++; return Promise.resolve(); }
    webkitExitFullscreen() { counts.exit++; }
    exitPictureInPicture() { counts.exit++; return Promise.resolve(); }
    dispatchEvent(event) { this.lastEvent = event; this.dispatch(event.type); }
  }
  const document = new Document();
  const window = { Node, Element, Document, HTMLMediaElement, HTMLVideoElement,
    navigator: { userAgent }, matchMedia(query) {
      check(query === "(-webkit-video-playable-inline)", "WebKit capability uses its documented media query");
      if (matchMediaThrows) throw new Error("host query unavailable");
      return { matches: inline };
    }, CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
    MutationObserver: class { constructor(fn) { this.callback = fn; window.observer = this; } observe(root, options) { this.root = root; this.options = options; } }
  };
  const existing = new HTMLVideoElement(); document.documentElement.children.push(existing);
  const context = vm.createContext({ document, window, WeakSet, Set, Promise, Error });
  if (failPatch) Object.defineProperty(HTMLMediaElement.prototype, "play", { writable: false, configurable: false });
  let installationError;
  try { vm.runInContext(source, context); }
  catch (error) { if (!failPatch) throw error; installationError = error; }
  return { document, window, context, counts, playResult, fullscreenResult, existing, rawElement,
    installationError, playArgument: () => playArgument, reload: () => vm.runInContext(source, context) };
}

async function main() {
  const env = environment(), { document, window, counts } = env;
  check(window.slopCityInlineVideoPolicyVersion === 3, "Successful installation publishes the launcher policy version");
  const partial = environment({ failPatch: true });
  check(partial.installationError && partial.installationError.name === "TypeError"
    && partial.window.slopCityInlineVideoPolicyVersion !== 3, "A refused prototype patch cannot publish a successful launcher policy version");
  check(env.existing.playsInline && !env.existing.controls, "Existing videos are configured");
  const canvas = document.createElement("canvas", { is: "preserved" });
  check(!canvas.hasAttribute("playsinline") && canvas.options.is === "preserved", "Non-video creation and options remain unchanged");
  const video = document.createElement("VIDEO");
  video.src = "StreamingAssets/Media/broadcast.mp4"; // Actual Unity order: src, then muted/playsinline, then play.
  video.muted = true; video.setAttribute("muted", ""); video.setAttribute("playsinline", "");
  video.muted = false;
  check(video.controls === false && video.disablePictureInPicture && video.disableRemotePlayback, "Video native UI and external playback disabled");
  check(video.getAttribute("x-webkit-airplay") === "deny" && video.webkitWirelessVideoPlaybackDisabled, "AirPlay presentation disabled");
  check(video.style.display === "none" && video.style.displayPriority === "important" && video.style.pointerEvents === "none", "Decoder cannot become a visible tappable DOM player");
  check(video.muted === false && video.volume === 0.65, "Unity retains audio volume and mute control");
  video.playsInline = false; video.controls = true; video.autoplay = true;
  video.disablePictureInPicture = false; video.disableRemotePlayback = false;
  video.removeAttribute("playsinline"); video.removeAttribute("webkit-playsinline");
  video.setAttribute("controls", ""); video.setAttribute("autoplay", "");
  video.toggleAttribute("playsinline", false); video.toggleAttribute("autoplay", true);
  video.setAttributeNS(null, "controls", "");
  check(video.playsInline && video.hasAttribute("webkit-playsinline") && !video.controls && !video.autoplay
    && video.disablePictureInPicture && video.disableRemotePlayback, "Later assignments cannot remove inline policy or enable native UI");
  check(video.play("preserved") === env.playResult && counts.play === 1 && env.playArgument() === "preserved", "Native play promise, arguments and single call preserved");
  await assert.rejects(video.play("throw"), /native playback unavailable/); checks++;
  await assert.rejects(video.play("denied"), { name: "NotAllowedError" }); checks++;
  check(window.HTMLMediaElement.prototype.play.call(video, "prototype") === env.playResult, "Borrowed prototype play follows policy");
  const namespaced = document.createElementNS("http://www.w3.org/1999/xhtml", "video");
  namespaced.src = "namespaced.mp4";
  check(namespaced.playsInline, "Namespaced HTML video creation is configured before source");
  const prototypeCreated = window.Document.prototype.createElement.call(document, "video");
  check(prototypeCreated.playsInline, "Prototype document creation cannot bypass policy");
  const svgVideo = document.createElementNS("http://www.w3.org/2000/svg", "video");
  check(!svgVideo.hasAttribute("playsinline"), "Non-HTML namespace remains untouched");
  const raw = env.rawElement("video"); raw.src = "raw.mp4"; raw.srcObject = { stream: "source" };
  check(raw.playsInline && raw.disablePictureInPicture, "Constructor-created media is guarded at native source setters");
  const attributeSource = env.rawElement("video"); attributeSource.setAttribute("src", "attribute-source.mp4");
  check(attributeSource.playsInline && !attributeSource.autoplay, "Attribute source assignment also applies decoder policy first");
  const cloned = video.cloneNode(true); cloned.src = "clone.mp4";
  check(cloned.playsInline && !cloned.controls && !cloned.autoplay, "Cloned detached videos are configured");
  const holder = document.createElement("div"); holder.innerHTML = '<video controls autoplay src="parsed.mp4"></video>';
  const parsed = holder.children[0];
  check(parsed.playsInline && !parsed.controls && !parsed.autoplay, "Parser-created videos lose autoplay/native controls synchronously");
  const parent = document.createElement("div"); parent.children.push(holder); holder.parentNode = parent;
  holder.outerHTML = '<video autoplay></video>';
  check(parent.children[0].playsInline && !parent.children[0].autoplay, "Replacement markup videos are configured");
  holder.insertAdjacentHTML("beforeend", "<video controls></video>");
  check(holder.children[0].playsInline && !holder.children[0].controls, "Inserted markup videos are configured");
  const observed = env.rawElement("video"); observed.attributes.set("autoplay", "");
  window.observer.callback([{ addedNodes: [observed] }]);
  check(observed.playsInline && !observed.autoplay, "Late inserted videos get decoder policy");
  const audio = document.createElement("audio"); audio.controls = true; audio.autoplay = true;
  check(audio.controls && audio.autoplay && audio.play("audio") === env.playResult, "Audio element behavior remains unchanged");
  assert.throws(() => audio.play("throw"), /native playback unavailable/); checks++;

  const nativeFullscreenBefore = counts.fullscreen;
  await assert.rejects(video.requestFullscreen(), { name: "NotSupportedError" }); checks++;
  await assert.rejects(window.Element.prototype.requestFullscreen.call(video), { name: "NotSupportedError" }); checks++;
  video.webkitRequestFullscreen(); video.webkitRequestFullScreen();
  video.webkitEnterFullscreen(); video.webkitEnterFullScreen();
  check(counts.fullscreen === nativeFullscreenBefore, "Modern, prefixed and borrowed native-video fullscreen requests never reach native APIs");
  video.webkitSetPresentationMode("fullscreen"); video.webkitSetPresentationMode("picture-in-picture");
  check(counts.presentation === 0, "WebKit external presentation requests are blocked before entry");
  video.webkitSetPresentationMode("inline");
  check(counts.presentation === 1 && video.webkitPresentationMode === "inline", "Explicit return to inline remains allowed");
  await assert.rejects(video.requestPictureInPicture(), { name: "NotSupportedError" }); checks++;
  video.webkitShowPlaybackTargetPicker();
  check(counts.pip === 0 && counts.remote === 0, "Native PiP and remote playback picker never open");
  check(canvas.requestFullscreen("game") === env.fullscreenResult && canvas.fullscreenArgument === "game", "Explicit game canvas fullscreen promise and arguments preserved");
  const stage = document.createElement("div");
  check(stage.webkitRequestFullscreen("stage") === "legacy fullscreen" && stage.fullscreenArgument === "stage", "Explicit prefixed game-stage fullscreen preserved");
  const beforeExit = counts.exit;
  document.fullscreenElement = canvas; document.dispatch("fullscreenchange");
  document.fullscreenElement = stage; document.dispatch("fullscreenchange");
  check(counts.exit === beforeExit, "Fullscreen fallback never exits the game stage or canvas");
  document.fullscreenElement = video; document.dispatch("fullscreenchange");
  check(counts.exit === beforeExit + 1 && counts.pause > 0, "Unexpected decoder fullscreen pauses before requesting exit");
  const playsBeforeBlockedRetry = counts.play;
  await assert.rejects(video.play(), { name: "NotSupportedError" }); checks++;
  check(counts.play === playsBeforeBlockedRetry, "A promoted decoder stays paused instead of repeatedly reopening native presentation");

  const legacy = document.createElement("video"); legacy.webkitDisplayingFullscreen = true;
  legacy.dispatch("webkitbeginfullscreen");
  check(!legacy.webkitDisplayingFullscreen && window.slopCityInlineVideoPolicy.blockedCount === 2, "Unexpected WebKit fullscreen is paused and remembered");
  const presentation = document.createElement("video"); presentation.webkitPresentationMode = "picture-in-picture";
  presentation.dispatch("webkitpresentationmodechanged");
  check(presentation.webkitPresentationMode === "inline", "Unexpected WebKit presentation is paused and returned inline");
  const create = window.Document.prototype.createElement, play = window.HTMLMediaElement.prototype.play;
  env.reload();
  check(window.Document.prototype.createElement === create && window.HTMLMediaElement.prototype.play === play, "Repeated script load does not stack prototype hooks");

  const iPhoneUA = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1";
  const supported = environment({ userAgent: iPhoneUA, inline: true });
  const supportedVideo = supported.document.createElement("video"); supportedVideo.src = "iphone.mp4";
  check(supportedVideo.play() === supported.playResult && supported.counts.play === 1, "An inline-capable iPhone host retains native decoder playback");
  const unsupported = environment({ userAgent: iPhoneUA, inline: false });
  const unsupportedVideo = unsupported.document.createElement("video"); unsupportedVideo.src = "blocked.mp4";
  await assert.rejects(unsupportedVideo.play(), { name: "NotSupportedError" }); checks++;
  check(unsupported.counts.play === 0 && unsupported.counts.pause === 1, "Fullscreen-only iPhone host is rejected before native play, with decoder paused");
  check(unsupported.document.lastEvent.type === "slopcityvideoblocked" && unsupported.window.slopCityInlineVideoPolicy.blockedCount === 1,
    "Fail-closed reason is available to the game's fallback UI");
  await assert.rejects(unsupported.window.HTMLMediaElement.prototype.play.call(unsupportedVideo), { name: "NotSupportedError" }); checks++;
  check(unsupported.counts.play === 0 && unsupported.window.slopCityInlineVideoPolicy.blockedCount === 1, "Prototype playback cannot bypass the fullscreen-only host rejection");
  const iPad = environment({ userAgent: iPhoneUA.replace("iPhone", "iPad"), inline: true });
  check(iPad.document.createElement("video").play() === iPad.playResult, "Inline-capable iPad playback remains supported");
  const queryFailure = environment({ userAgent: iPhoneUA, matchMediaThrows: true });
  await assert.rejects(queryFailure.document.createElement("video").play(), { name: "NotSupportedError" }); checks++;
  check(queryFailure.counts.play === 0, "An inaccessible WebKit capability check fails closed without a native play call");
  const chromium = environment({ inline: false });
  check(chromium.document.createElement("video").play() === chromium.playResult, "An unsupported WebKit-only query does not disable Chromium playback");
  console.log(JSON.stringify({ passed: true, checks,
    scope: "production detached decoder, prototype/source/parser/clone paths, WebKit capability fail-closed, presentation APIs and game fullscreen; no physical iPhone/iPad validation" }));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
