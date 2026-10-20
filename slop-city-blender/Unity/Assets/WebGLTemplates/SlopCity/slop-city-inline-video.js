// Unity's detached videos decode frames for in-game RenderTextures only.
// WebKit hosts can require native fullscreen despite playsinline; refuse play
// in that case. Never open a native video surface and try to close it afterward.
(function () {
  "use strict";
  if (window.slopCityInlineVideoInstalled) return;
  window.slopCityInlineVideoInstalled = true;

  const configured = new WeakSet();
  const blocked = new WeakSet();
  const HTML_NAMESPACE = "http://www.w3.org/1999/xhtml";
  const media = window.HTMLMediaElement && window.HTMLMediaElement.prototype;
  const videos = window.HTMLVideoElement && window.HTMLVideoElement.prototype;
  const elements = window.Element && window.Element.prototype;
  const nativeSet = elements ? elements.setAttribute : null;
  const nativeRemove = elements ? elements.removeAttribute : null;
  const nativePause = media ? media.pause : null;
  const policy = window.slopCityInlineVideoPolicy = { blockedCount: 0, reason: "" };
  const forcedAttributes = new Set(["playsinline", "webkit-playsinline", "disablepictureinpicture", "disableremoteplayback"]);

  function isVideo(element) {
    return element && String(element.localName).toLowerCase() === "video"
      && (!element.namespaceURI || element.namespaceURI === HTML_NAMESPACE);
  }
  function set(video, name) { (nativeSet || video.setAttribute).call(video, name, ""); }
  function remove(video, name) { (nativeRemove || video.removeAttribute).call(video, name); }
  function inlineAvailable() {
    const ua = (window.navigator && window.navigator.userAgent) || "";
    const webkit = /AppleWebKit\//.test(ua) && !/(Chrome\/|Chromium\/|Edg\/|OPR\/|Android)/.test(ua);
    // WebKit documents this query for hosts that force video fullscreen.
    // Do not interpret an unsupported vendor query as a ban in other engines.
    if (!webkit) return true;
    try {
      return typeof window.matchMedia === "function"
        && window.matchMedia("(-webkit-video-playable-inline)").matches;
    } catch (error) { return false; }
  }
  window.slopCityCanPlayInlineVideo = inlineAvailable;

  function applyInline(video) {
    video.playsInline = true;
    video.controls = false;
    video.autoplay = false;
    video.disablePictureInPicture = true;
    video.disableRemotePlayback = true;
    if ("webkitWirelessVideoPlaybackDisabled" in video) video.webkitWirelessVideoPlaybackDisabled = true;
    forcedAttributes.forEach(function (name) { set(video, name); });
    (nativeSet || video.setAttribute).call(video, "x-webkit-airplay", "deny");
    remove(video, "controls"); remove(video, "autoplay");
    if (video.style) {
      video.style.pointerEvents = "none";
      if (video.style.setProperty) video.style.setProperty("display", "none", "important");
      else video.style.display = "none";
    }
  }
  function stop(video, reason) {
    if (!blocked.has(video)) {
      blocked.add(video); policy.blockedCount++; policy.reason = reason;
      if (typeof window.CustomEvent === "function" && document.dispatchEvent)
        document.dispatchEvent(new window.CustomEvent("slopcityvideoblocked", { detail: { reason: reason } }));
    }
    if (nativePause) nativePause.call(video);
    else if (video.pause) video.pause();
    applyInline(video);
  }
  function refuse(video, reason) {
    stop(video, reason);
    const error = new Error(reason);
    // Unity retries NotAllowedError on the next gesture; this policy is permanent
    // for the decoder, so expose a different error instead of creating retries.
    error.name = "NotSupportedError";
    return Promise.reject(error);
  }
  function externalPresentation(video) {
    return video.webkitDisplayingFullscreen || (video.webkitPresentationMode && video.webkitPresentationMode !== "inline");
  }
  // Policy refusal is permanent for this element. Decoder retirement may clear
  // its source, but must not put that refused element back into the iOS pool.
  window.slopCityCanReuseInlineVideo = function (video) {
    return !!(isVideo(video) && !blocked.has(video) && !externalPresentation(video));
  };
  function recover(video) {
    stop(video, "Video paused: this browser attempted an external video player.");
    try {
      if (video.webkitDisplayingFullscreen && video.webkitExitFullscreen) video.webkitExitFullscreen();
      if (video.webkitPresentationMode && video.webkitPresentationMode !== "inline" && video.webkitSetPresentationMode)
        video.webkitSetPresentationMode("inline");
    } catch (error) { /* Keep the decoder paused; never resume into native presentation. */ }
  }
  function configure(video) {
    if (!isVideo(video)) return video;
    applyInline(video);
    if (configured.has(video)) return video;
    configured.add(video);
    video.addEventListener("webkitbeginfullscreen", function () { recover(video); });
    video.addEventListener("webkitpresentationmodechanged", function () {
      if (externalPresentation(video)) recover(video);
    });
    video.addEventListener("enterpictureinpicture", function () {
      recover(video);
      if (document.pictureInPictureElement === video && document.exitPictureInPicture) {
        const result = document.exitPictureInPicture();
        if (result && result.catch) result.catch(function () {});
      }
    });
    if (!media) {
      const play = video.play;
      video.play = guardedPlay(play);
    }
    return video;
  }
  function guardedPlay(play) {
    return function () {
      if (!isVideo(this)) return play.apply(this, arguments);
      configure(this);
      if (!inlineAvailable()) return refuse(this, "Video paused: this browser requires fullscreen playback.");
      if (blocked.has(this) || externalPresentation(this))
        return refuse(this, "Video paused: only in-game display playback is allowed.");
      // WebKit can throw synchronously as well as reject its play promise.
      // Keep the native call in the gesture stack, but give Unity one consistent
      // failure path; a denied decoder must never become a global game error.
      try { return play.apply(this, arguments); }
      catch (error) { return Promise.reject(error); }
    };
  }
  function patchMethod(prototype, name, wrap) {
    if (!prototype || typeof prototype[name] !== "function") return;
    const original = prototype[name];
    Object.defineProperty(prototype, name, { configurable: true, writable: true, value: wrap(original) });
  }
  function forceProperty(prototype, name, value) {
    if (!prototype) return;
    const descriptor = Object.getOwnPropertyDescriptor(prototype, name);
    if (!descriptor || !descriptor.set || !descriptor.configurable) return;
    Object.defineProperty(prototype, name, Object.assign({}, descriptor, {
      set: function (requested) { descriptor.set.call(this, isVideo(this) ? value : requested); }
    }));
  }
  forceProperty(videos, "playsInline", true);
  forceProperty(videos, "disablePictureInPicture", true);
  forceProperty(videos, "webkitWirelessVideoPlaybackDisabled", true);
  ["controls", "autoplay"].forEach(function (name) { forceProperty(media, name, false); });
  forceProperty(media, "disableRemotePlayback", true);
  patchMethod(elements, "setAttribute", function (original) {
    return function (name, value) {
      const key = String(name).toLowerCase();
      if (isVideo(this)) {
        if (key === "src") configure(this);
        if (key === "controls" || key === "autoplay") return nativeRemove.call(this, name);
        if (forcedAttributes.has(key)) value = "";
        if (key === "x-webkit-airplay") value = "deny";
      }
      return original.call(this, name, value);
    };
  });
  patchMethod(elements, "setAttributeNS", function (original) {
    return function (namespace, name, value) {
      if (isVideo(this) && !namespace) return this.setAttribute(name, value);
      return original.apply(this, arguments);
    };
  });
  patchMethod(elements, "toggleAttribute", function (original) {
    return function (name) {
      const key = String(name).toLowerCase();
      if (isVideo(this)) {
        if (key === "controls" || key === "autoplay") { nativeRemove.call(this, name); return false; }
        if (forcedAttributes.has(key)) { nativeSet.call(this, name, ""); return true; }
      }
      return original.apply(this, arguments);
    };
  });
  patchMethod(elements, "removeAttribute", function (original) {
    return function (name) {
      const key = String(name).toLowerCase();
      if (isVideo(this) && forcedAttributes.has(key)) return nativeSet.call(this, name, "");
      return original.call(this, name);
    };
  });
  if (media) {
    patchMethod(media, "play", guardedPlay);
    patchMethod(media, "load", function (original) {
      return function () { if (isVideo(this)) configure(this); return original.apply(this, arguments); };
    });
    ["src", "srcObject"].forEach(function (name) {
      const descriptor = Object.getOwnPropertyDescriptor(media, name);
      if (!descriptor || !descriptor.set || !descriptor.configurable) return;
      Object.defineProperty(media, name, Object.assign({}, descriptor, {
        set: function (value) { if (isVideo(this)) configure(this); descriptor.set.call(this, value); }
      }));
    });
  }
  ["requestFullscreen", "webkitRequestFullscreen", "webkitRequestFullScreen", "mozRequestFullScreen", "msRequestFullscreen"]
    .forEach(function (name) {
      patchMethod(elements, name, function (original) {
        return function () {
          if (!isVideo(this)) return original.apply(this, arguments);
          configure(this);
          if (name === "requestFullscreen") return Promise.reject(Object.assign(new Error("Video fullscreen is disabled."), { name: "NotSupportedError" }));
          return undefined;
        };
      });
    });
  ["webkitEnterFullscreen", "webkitEnterFullScreen", "webkitShowPlaybackTargetPicker"].forEach(function (name) {
    patchMethod(videos, name, function () { return function () { configure(this); }; });
  });
  patchMethod(videos, "webkitSetPresentationMode", function (original) {
    return function (mode) { configure(this); if (mode === "inline") return original.call(this, mode); };
  });
  patchMethod(videos, "requestPictureInPicture", function () {
    return function () { configure(this); return Promise.reject(Object.assign(new Error("External video presentation is disabled."), { name: "NotSupportedError" })); };
  });

  function configureTree(root) {
    if (isVideo(root)) configure(root);
    if (root && root.querySelectorAll) root.querySelectorAll("video").forEach(configure);
    if (root && root.content && root.content.querySelectorAll) root.content.querySelectorAll("video").forEach(configure);
    return root;
  }
  const documents = window.Document ? window.Document.prototype : document;
  ["createElement", "createElementNS"].forEach(function (name) {
    patchMethod(documents, name, function (original) {
      return function () { return configureTree(original.apply(this, arguments)); };
    });
  });
  patchMethod(window.Node && window.Node.prototype, "cloneNode", function (original) {
    return function () { return configureTree(original.apply(this, arguments)); };
  });
  // Parsed markup creates videos without createElement. Configure them
  // synchronously before the caller can load/play the new subtree.
  ["innerHTML", "outerHTML"].forEach(function (name) {
    if (!elements) return;
    const descriptor = Object.getOwnPropertyDescriptor(elements, name);
    if (!descriptor || !descriptor.set || !descriptor.configurable) return;
    Object.defineProperty(elements, name, Object.assign({}, descriptor, {
      set: function (value) {
        const parent = this.parentNode;
        descriptor.set.call(this, value);
        configureTree(name === "outerHTML" ? parent : this);
      }
    }));
  });
  patchMethod(elements, "insertAdjacentHTML", function (original) {
    return function () { const result = original.apply(this, arguments); configureTree(this.parentNode || this); return result; };
  });
  configureTree(document);
  if (typeof window.MutationObserver === "function" && document.documentElement) {
    const observer = new window.MutationObserver(function (records) {
      records.forEach(function (record) {
        if (record.addedNodes) record.addedNodes.forEach(configureTree);
      });
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
  }
  ["fullscreenchange", "webkitfullscreenchange"].forEach(function (name) {
    document.addEventListener(name, function () {
      const element = document.fullscreenElement || document.webkitFullscreenElement;
      if (!isVideo(element)) return; // Preserve explicit game stage/canvas fullscreen.
      recover(element);
      const exit = document.exitFullscreen || document.webkitExitFullscreen;
      if (exit) {
        const result = exit.call(document);
        if (result && result.catch) result.catch(function () {});
      }
    });
  });
  // The launcher accepts this version only after every policy hook installed.
  // A host that refuses a prototype patch must not launch with a partial guard.
  window.slopCityInlineVideoPolicyVersion = 3;
})();
