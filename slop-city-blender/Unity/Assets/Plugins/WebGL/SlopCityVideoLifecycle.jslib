// Unity's Chromium frame workaround schedules recurring native callbacks, but
// its destroy helper never cancels them. Preserve the pinned create semantics
// while giving both its first-frame request and recurrence explicit ownership.
if (typeof LibraryManager.library.JS_Video_Destroy !== 'function'
  || typeof LibraryManager.library.JS_Video_IsReady !== 'function') {
  throw new Error('Slop City video lifecycle requires Unity Video.js first.');
}
mergeInto(LibraryManager.library, {
  // An audio grant belongs to the HTML media element. Retiring every phone
  // decoder must release its source, without throwing that grant away during
  // a held walking gesture. One source-free element is not an active decoder.
  $slopCityReusableIOSVideo: null,
  $slopCityRetiredVideoDecoders: {},
  $slopCityVideoAdmissionLimit: 480,
  $slopCityDisposingVideoDecoders: false,
  $slopCityNativeVideoDestroy: LibraryManager.library.JS_Video_Destroy,
  $slopCityNativeVideoDestroy__deps: LibraryManager.library.JS_Video_Destroy__deps.slice(),
  $slopCityNativeVideoIsReady: LibraryManager.library.JS_Video_IsReady,
  $slopCityNativeVideoIsReady__deps: LibraryManager.library.JS_Video_IsReady__deps.slice(),

  $slopCityUsesIOSVideo: function () {
    var info = typeof Module !== 'undefined' && Module.SystemInfo;
    var nav = typeof navigator !== 'undefined' ? navigator : {};
    return !!((info && /^(iPhoneOS|iPadOS)$/.test(info.os))
      || /iPhone|iPad|iPod/.test(nav.userAgent || '')
      || (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1));
  },

  $slopCityAdmitVideoMetadata__deps: ['$slopCityUsesMobileVideo', '$slopCityVideoAdmissionLimit'],
  $slopCityAdmitVideoMetadata: function (video) {
    if (video.slopCityRejectedDimensions) {
      if (!video.slopCityDimensionErrorReported && typeof video.onerror === 'function') {
        video.slopCityDimensionErrorReported = true;
        video.onerror({ target: { error: { code: 4 } } });
      }
      return false;
    }
    if (video.slopCityDestroyed || !(video.readyState >= 1)) return false;
    var width = video.videoWidth, height = video.videoHeight;
    if (!(width > 0 && height > 0)) return false;
    var edge = slopCityUsesMobileVideo(video) ? slopCityVideoAdmissionLimit : 4096;
    if (width > edge || height > edge || width * height > 8388608) {
      // Metadata is the earliest browser dimension boundary. Prevent play and
      // texture creation, then abort resource selection instead of retaining a
      // paused oversized decoder. Packaging bounds the encoded source itself.
      video.slopCityRejectedDimensions = true;
      video.isReady = false;
      video.pause();
      video.removeAttribute('src');
      video.load();
      return false;
    }
    return true;
  },

  JS_Video_IsReady__proxy: 'sync',
  JS_Video_IsReady__sig: 'ii',
  JS_Video_IsReady__deps: ['$videoInstances', '$slopCityUsesIOSVideo', '$slopCityNativeVideoIsReady', '$slopCityAdmitVideoMetadata', '$slopCityUsesMobileVideo'],
  JS_Video_IsReady: function (id) {
    var candidate = videoInstances[id];
    if (!candidate || !slopCityAdmitVideoMetadata(candidate)) return false;
    if (!slopCityUsesMobileVideo(candidate)) return slopCityNativeVideoIsReady(id);
    // Keep Unity's iOS metadata readiness workaround for desktop-UA iPads too.
    // This permits startup; existing dimensions, seeking and native upload
    // guards still decide whether a picture can be copied to the world display.
    var video = videoInstances[id];
    if (!video) return false;
    if (!video.isReady && video.readyState >= video.HAVE_METADATA) video.isReady = true;
    return video.isReady;
  },

  $slopCityCancelCreatedVideoFrame: function (v) {
    var request = v.slopCityCreatedFrameRequest;
    if (request && typeof v.cancelVideoFrameCallback === 'function') v.cancelVideoFrameCallback(request.id);
    delete v.slopCityCreatedFrameRequest;
  },

  $slopCityRetireVideoDecoder__deps: ['$videoInstances', '$slopCityNativeVideoDestroy', '$slopCityCancelCreatedVideoFrame', '$slopCityReleaseVideoTexture', '$slopCityClearVideoAttemptTimer', '$slopCityCancelVideoPictureWatch', '$slopCityCancelVideoFrame', '$jsVideoRemovePendingBlockedVideo'],
  $slopCityRetireVideoDecoder: function (id, video) {
    var complete = true;
    function clean(action) { try { action(); } catch (_) { complete = false; } }
    video.slopCityDestroyed = true;
    // Keep the exact decoder owner until *every* native retirement step succeeds.
    // One failed cancellation/GL call must never leave hidden playback running.
    clean(function () { if (video.slopCitySourceAdmissionTimer) clearTimeout(video.slopCitySourceAdmissionTimer); delete video.slopCitySourceAdmissionTimer; });
    clean(function () { slopCityCancelCreatedVideoFrame(video); });
    clean(function () { slopCityCancelVideoFrame(video); });
    clean(function () { slopCityClearVideoAttemptTimer(video.slopCityPlaybackIntent); });
    clean(function () { slopCityCancelVideoPictureWatch(video.slopCityPlaybackIntent); });
    clean(function () { jsVideoRemovePendingBlockedVideo(id); });
    delete video.slopCityPlaybackIntent;
    clean(function () { if (video.loopEndPollInterval) clearInterval(video.loopEndPollInterval); delete video.loopEndPollInterval; });
    clean(function () { slopCityReleaseVideoTexture(video); });
    clean(function () {
      if (video.slopCityLoadedDataListener) video.removeEventListener('loadeddata', video.slopCityLoadedDataListener);
      delete video.slopCityLoadedDataListener;
    });
    clean(function () {
      if (video.slopCityMetadataListener) {
        video.removeEventListener('loadedmetadata', video.slopCityMetadataListener);
        video.removeEventListener('resize', video.slopCityMetadataListener);
      }
      delete video.slopCityMetadataListener;
    });
    clean(function () { if (videoInstances[id] === video) slopCityNativeVideoDestroy(id); });
    // Retain Unity's callback/identity cleanup even if its helper was interrupted.
    clean(function () { video.onended = video.onerror = video.oncanplay = video.onseeked = null; delete video.onendedCallback; delete video.onendedRef; });
    delete videoInstances[id];
    clean(function () { video.pause(); });
    clean(function () { video.muted = true; });
    clean(function () { video.removeAttribute('src'); });
    clean(function () {
      if (!video.slopCitySourceClearRequested) { video.load(); video.slopCitySourceClearRequested = true; }
    });
    if (!video.slopCityRetiredAt) video.slopCityRetiredAt = Date.now();
    // load() is asynchronous. No replacement source is admitted until its old
    // resource selection is observably empty, even after native Destroy returns.
    try { return complete && video.paused && !video.getAttribute('src') && !video.currentSrc && video.readyState === 0; }
    catch (_) { return false; }
  },

  $slopCityCanReuseVideoElement: function (video) {
    // Missing/refused policy state cannot grant reuse. This query never clears
    // the inline guard's permanent refusal or transfers an element's audio grant.
    try {
      return typeof window.slopCityCanReuseInlineVideo === 'function'
        && window.slopCityCanReuseInlineVideo(video) === true;
    } catch (_) { return false; }
  },

  $slopCityDrainRetiredVideoDecoders__deps: ['$slopCityRetiredVideoDecoders', '$slopCityRetireVideoDecoder', '$slopCityReusableIOSVideo', '$slopCityUsesIOSVideo', '$slopCityDisposingVideoDecoders', '$slopCityCanReuseVideoElement'],
  $slopCityDrainRetiredVideoDecoders: function () {
    Object.keys(slopCityRetiredVideoDecoders).forEach(function (id) {
      var video = slopCityRetiredVideoDecoders[id];
      if (!slopCityRetireVideoDecoder(id, video)) return;
      delete slopCityRetiredVideoDecoders[id];
      if (!slopCityDisposingVideoDecoders && !slopCityReusableIOSVideo && slopCityUsesIOSVideo()
        && slopCityCanReuseVideoElement(video))
        slopCityReusableIOSVideo = video;
    });
    return Object.keys(slopCityRetiredVideoDecoders).length === 0;
  },

  $slopCityDisposeOwnedVideoDecoders__deps: ['$videoInstances', 'JS_Video_Destroy', '$slopCityReusableIOSVideo', '$slopCityDisposingVideoDecoders', '$slopCityDrainRetiredVideoDecoders', '$slopCityReleaseVideoGraphics'],
  $slopCityDisposeOwnedVideoDecoders__postset: "Module['slopCityDisposeOwnedVideoDecoders'] = slopCityDisposeOwnedVideoDecoders;",
  $slopCityDisposeOwnedVideoDecoders: function () {
    slopCityDisposingVideoDecoders = true;
    slopCityReusableIOSVideo = null;
    var firstError;
    try {
      Object.keys(videoInstances).forEach(function (id) {
        try { _JS_Video_Destroy(id); }
        catch (error) { if (!firstError) firstError = error; }
      });
      try { if (!slopCityDrainRetiredVideoDecoders()) throw new Error('Video decoders are still retiring.'); } catch (error) { if (!firstError) firstError = error; }
      try { slopCityReleaseVideoGraphics(); } catch (error) { if (!firstError) firstError = error; }
    } finally {
      slopCityReusableIOSVideo = null;
      slopCityDisposingVideoDecoders = false;
    }
    if (firstError) throw firstError;
  },

  JS_Video_Create__proxy: 'sync',
  JS_Video_Create__sig: 'ii',
  JS_Video_Create__deps: ['$videoInstances', '$videoInstanceIdCounter', '$hasSRGBATextures', '$UTF8ToString', '$jsIsWebkit', '$slopCityDisposeOwnedVideoDecoders', '$slopCityUsesIOSVideo', '$slopCityReusableIOSVideo', '$slopCityAdmitVideoMetadata', '$slopCityUsesMobileVideo', '$slopCityDrainRetiredVideoDecoders', '$slopCityFailVideoPlayback', '$slopCityCanReuseVideoElement'],
  JS_Video_Create: function (url) {
    var str = UTF8ToString(url);
    slopCityDrainRetiredVideoDecoders();
    var ios = slopCityUsesIOSVideo();
    var video = ios && slopCityReusableIOSVideo;
    if (video) {
      slopCityReusableIOSVideo = null;
      // A late presentation event may refuse the source-free cached element.
      if (!slopCityCanReuseVideoElement(video)) video = null;
    }
    var retiredAt = video ? video.slopCityRetiredAt || 0 : 0;
    if (video) {
      // Only the same element's confirmed audio grant survives. Every source
      // intent, retry, picture identity and native Unity bookmark starts fresh.
      Object.keys(video).forEach(function (key) {
        if (key.indexOf('slopCity') === 0 && key !== 'slopCityAudioUnlocked') delete video[key];
      });
      ['isReady', 'isLoaded', 'lastUpdateTextureTime', 'lastSeenPlaybackTime',
        'previousUploadedWidth', 'previousUploadedHeight', 'loopEndPollInterval',
        'enabledTracks', 'onendedRef'].forEach(function (key) { delete video[key]; });
      video.loop = false;
      video.playbackRate = 1;
      video.volume = 1;
    } else video = document.createElement('video');
    if (ios) {
      // Physical iPhone A/B terminated with RVFC, but survived a full clip loop
      // without it. Avoid its native frame queue only for our iOS decoders;
      // Unity's existing readiness/currentTime path still plays and seeks.
      Object.defineProperty(video, 'requestVideoFrameCallback', {
        value: undefined, writable: true, configurable: true
      });
    }
    video.style.display = 'none';
    var id = ++videoInstanceIdCounter;
    videoInstances[id] = video;
    function current() { return videoInstances[id] === video && !video.slopCityDestroyed; }
    function loadedDataListener() {
      if (video.slopCityLoadedDataListener !== loadedDataListener) return;
      video.removeEventListener('loadeddata', loadedDataListener);
      delete video.slopCityLoadedDataListener;
      if (current()) video.isLoaded = true;
    }
    video.slopCityLoadedDataListener = loadedDataListener;
    video.addEventListener('loadeddata', loadedDataListener);
    if (typeof video.requestVideoFrameCallback === 'function') {
      function requestFrame(first) {
        if (!current()) return;
        var request = {};
        video.slopCityCreatedFrameRequest = request;
        request.id = video.requestVideoFrameCallback(function () {
          if (!current() || video.slopCityCreatedFrameRequest !== request) return;
          delete video.slopCityCreatedFrameRequest;
          video.slopCityDecodedFrame = (video.slopCityDecodedFrame || 0) + 1;
          if (first) video.isLoaded = true;
          // Keep Unity's UUM-111608 compositor workaround. Also track presented
          // frames on desktop WebKit: currentTime can advance between decoded frames.
          requestFrame(false);
        });
      }
      requestFrame(true);
    }
    // Configure resource selection before assigning the URL. Metadata-only
    // preparation avoids eager full-clip buffering before the single slot plays.
    video.preload = slopCityUsesMobileVideo(video) ? 'metadata' : 'auto';
    function metadataListener() {
      if (current()) slopCityAdmitVideoMetadata(video);
    }
    video.slopCityMetadataListener = metadataListener;
    video.addEventListener('loadedmetadata', metadataListener);
    video.addEventListener('resize', metadataListener);
    video.muted = true;
    video.setAttribute('muted', '');
    video.setAttribute('playsinline', '');
    video.crossOrigin = 'anonymous';
    var admissionStartedAt = Date.now();
    function admitSource() {
      if (!current()) return;
      // load() aborts the old source asynchronously. Wait for its observable
      // empty state and a quiet interval before attaching the replacement.
      if (!slopCityDrainRetiredVideoDecoders() || (ios && retiredAt && (Date.now() - retiredAt < 250
        || video.readyState !== 0 || video.currentSrc))) {
        if (Date.now() - admissionStartedAt >= 30000) {
          delete video.slopCitySourceAdmissionTimer;
          slopCityFailVideoPlayback(id, video, video.slopCityPlaybackIntent);
          return;
        }
        video.slopCitySourceAdmissionTimer = setTimeout(admitSource, 50);
        return;
      }
      delete video.slopCitySourceAdmissionTimer;
      video.src = str;
    }
    admitSource();
    if (hasSRGBATextures == null) {
      hasSRGBATextures = true;
      if (Module.SystemInfo.browser === 'Firefox' && Module.SystemInfo.browserVersion < 108) hasSRGBATextures = false;
      if (jsIsWebkit()) hasSRGBATextures = false;
    }
    return id;
  },

  JS_Video_Destroy__proxy: 'sync',
  JS_Video_Destroy__sig: 'vi',
  JS_Video_Destroy__deps: ['$videoInstances', '$slopCityRetiredVideoDecoders', '$slopCityDrainRetiredVideoDecoders'],
  JS_Video_Destroy: function (id) {
    var video = videoInstances[id] || slopCityRetiredVideoDecoders[id];
    if (!video) return;
    slopCityRetiredVideoDecoders[id] = video;
    // Retirement owns partial failures and blocks new source admission. It can
    // be retried without allocating another active decoder beside that owner.
    slopCityDrainRetiredVideoDecoders();
  }
});
