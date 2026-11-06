// Explicit QA telemetry for detached Unity decoders. Load after the inline policy.
// Ordinary play installs no hooks, timers, output, or decoder references.
(function () {
  "use strict";
  if (new URLSearchParams(window.location.search).get("qa") !== "1"
      || window.slopCityMediaDiagnosticsVersion) return;

  const entries = new Map();
  const seen = new WeakSet();
  const weak = typeof window.WeakRef === "function";
  // QA telemetry must never become the owner keeping a detached decoder alive.
  // Browsers without WeakRef simply run without this optional diagnostic.
  if (!weak) return;
  let nextId = 1;
  let output;
  let interval = null;
  let lastSnapshot = "";

  function track(element) {
    if (!element || String(element.tagName).toLowerCase() !== "video"
        || (element.namespaceURI && element.namespaceURI !== "http://www.w3.org/1999/xhtml")
        || seen.has(element)) return element;
    seen.add(element);
    entries.set(nextId++, { ref: new window.WeakRef(element),
      createdAt: Date.now(), hadSource: false });
    return element;
  }

  // Chain the already installed policy wrappers. Never alter play, source, mute,
  // volume, presentation, or insertion; the guard remains responsible for them.
  const owner = window.Document ? window.Document.prototype : document;
  ["createElement", "createElementNS"].forEach(function (name) {
    const original = owner[name];
    if (typeof original !== "function") return;
    const descriptor = Object.getOwnPropertyDescriptor(owner, name);
    Object.defineProperty(owner, name, Object.assign({}, descriptor || {
      configurable: true, enumerable: false, writable: true
    }, { value: function () { return track(original.apply(this, arguments)); } }));
  });
  if (document.querySelectorAll) document.querySelectorAll("video").forEach(track);

  // iOS can reuse the same source-free element after decoder retirement, so a
  // later source assignment must restore QA tracking without keeping that shell
  // strongly reachable here. Chain the inline policy's existing setter.
  const media = window.HTMLMediaElement && window.HTMLMediaElement.prototype;
  const source = media && Object.getOwnPropertyDescriptor(media, "src");
  if (source && source.set && source.configurable) {
    Object.defineProperty(media, "src", Object.assign({}, source, {
      set: function (value) {
        source.set.call(this, value);
        if (value && this.slopCityDestroyed !== true) track(this);
      }
    }));
  }

  function untrack(video, id) {
    entries.delete(id);
    if (video) seen.delete(video);
  }

  function finite(value) { return Number.isFinite(value) ? value : null; }
  function shortName(source) {
    // Report only a filename, never origin, credentials, query, or fragment.
    const path = String(source).split(/[?#]/, 1)[0]
      .replace(/^[a-z][a-z0-9+.-]*:\/\/[^/]*/i, "");
    return path.slice(path.lastIndexOf("/") + 1).slice(0, 160);
  }
  function sample() {
    const videos = [];
    entries.forEach(function (entry, id) {
      const video = entry.ref.deref();
      if (!video) { untrack(video, id); return; }
      // Decoder teardown is authoritative. Some browsers retain currentSrc
      // after removeAttribute('src')/load(), so it cannot prove a live resource.
      if (video.slopCityDestroyed === true) { untrack(video, id); return; }
      // Unity destroys a decoder by assigning src="". Its resolved .src can
      // then be the page URL, so consult the raw attribute first.
      const attribute = video.getAttribute("src");
      const source = attribute === null ? (video.currentSrc || "") : attribute;
      if (source) entry.hadSource = true;
      if (!source && (entry.hadSource || Date.now() - entry.createdAt > 5000)) {
        untrack(video, id); return;
      }
      videos.push({ id: id, src: shortName(source), currentTime: finite(video.currentTime),
        duration: finite(video.duration), volume: finite(video.volume), muted: !!video.muted,
        paused: !!video.paused, loop: !!video.loop, readyState: video.readyState,
        width: video.videoWidth, height: video.videoHeight,
        rejectedDimensions: !!video.slopCityRejectedDimensions,
        encodedWidth: video.slopCityEncodedWidth || null, encodedHeight: video.slopCityEncodedHeight || null,
        playsInline: !!video.playsInline, connected: !!video.isConnected,
        nativeFullscreen: !!video.webkitDisplayingFullscreen,
        presentationMode: video.webkitPresentationMode || "inline" });
    });
    var rejectedCount = videos.reduce(function (total, row) { return total + (row.rejectedDimensions ? 1 : 0); }, 0);
    var mobileMedia = window.slopCityMobileMediaQa || null;
    var heap = typeof Module !== "undefined" && Module.HEAPU8 && Module.HEAPU8.buffer
      ? Module.HEAPU8.buffer.byteLength : null;
    if (!output) {
      const parent = document.body || document.documentElement;
      if (!parent) return;
      output = document.createElement("pre");
      output.id = "slop-city-media-diagnostics";
      output.hidden = true;
      output.setAttribute("aria-hidden", "true");
      output.style.setProperty("display", "none", "important");
      parent.appendChild(output);
    }
    const snapshot = { version: 1,
      count: videos.length, rejectedDimensionVideos: rejectedCount, wasmHeapBytes: heap,
      mobileMedia: mobileMedia, videos: videos };
    // A stable report means no DOM/string churn. The next changed source, media
    // state, or decoder list writes a fresh authoritative snapshot.
    const fingerprint = JSON.stringify(snapshot);
    if (fingerprint === lastSnapshot) return;
    lastSnapshot = fingerprint;
    // Keep one canonical text value. The private operator shell reads text first
    // and retains an attribute fallback for already-published runtimes.
    output.textContent = JSON.stringify(Object.assign({ checkedAtUtc: new Date().toISOString() }, snapshot));
  }
  function stopSampling() {
    if (interval !== null) { clearInterval(interval); interval = null; }
  }
  function startSampling() {
    if (document.hidden || interval !== null) return;
    sample();
    interval = window.setInterval(sample, 1000);
  }
  function onVisibilityChange() { if (document.hidden) stopSampling(); else startSampling(); }
  window.slopCityMediaDiagnosticsVersion = 1;
  document.addEventListener("visibilitychange", onVisibilityChange);
  startSampling();
  window.slopCityDisposeMediaDiagnostics = function () {
    stopSampling();
    document.removeEventListener("visibilitychange", onVisibilityChange);
    entries.clear();
    if (output) {
      output.textContent = "";
      output.remove();
      output = null;
    }
    lastSnapshot = "";
  };
})();
