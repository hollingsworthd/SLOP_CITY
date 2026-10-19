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
    if (!(video.readyState >= 1)) return false;
    var width = video.videoWidth, height = video.videoHeight;
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
    delete v.slopCityCreatedFrameRequest;
    if (request && typeof v.cancelVideoFrameCallback === 'function') v.cancelVideoFrameCallback(request.id);
  },

  $slopCityDisposeOwnedVideoDecoders__deps: ['$videoInstances', 'JS_Video_Destroy', '$slopCityReusableIOSVideo', '$slopCityDisposingVideoDecoders'],
  $slopCityDisposeOwnedVideoDecoders__postset: "Module['slopCityDisposeOwnedVideoDecoders'] = slopCityDisposeOwnedVideoDecoders;",
  $slopCityDisposeOwnedVideoDecoders: function () {
    // pagehide cannot wait for another Unity frame to retire detached decoders.
    slopCityDisposingVideoDecoders = true;
    slopCityReusableIOSVideo = null;
    var firstError;
    try {
      Object.keys(videoInstances).forEach(function (video) {
        try { _JS_Video_Destroy(video); }
        catch (error) { if (!firstError) firstError = error; }
      });
    } finally {
      slopCityReusableIOSVideo = null;
      slopCityDisposingVideoDecoders = false;
    }
    if (firstError) throw firstError;
  },

  JS_Video_Create__proxy: 'sync',
  JS_Video_Create__sig: 'ii',
  JS_Video_Create__deps: ['$videoInstances', '$videoInstanceIdCounter', '$hasSRGBATextures', '$UTF8ToString', '$jsIsWebkit', '$slopCityDisposeOwnedVideoDecoders', '$slopCityUsesIOSVideo', '$slopCityReusableIOSVideo', '$slopCityAdmitVideoMetadata', '$slopCityUsesMobileVideo'],
  JS_Video_Create: function (url) {
    var str = UTF8ToString(url);
    var ios = slopCityUsesIOSVideo();
    var video = ios && slopCityReusableIOSVideo;
    var retiredAt = video ? video.slopCityRetiredAt || 0 : 0;
    if (video) {
      slopCityReusableIOSVideo = null;
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
    function admitSource() {
      if (!current()) return;
      // load() aborts the old source asynchronously. Wait for its observable
      // empty state and a quiet interval before attaching the replacement.
      if (ios && retiredAt && (Date.now() - retiredAt < 250
        || video.readyState !== 0 || video.currentSrc)) {
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
  JS_Video_Destroy__deps: ['$videoInstances', '$slopCityNativeVideoDestroy', '$slopCityCancelCreatedVideoFrame', '$slopCityReleaseVideoTexture', '$slopCityUsesIOSVideo', '$slopCityReusableIOSVideo', '$slopCityDisposingVideoDecoders'],
  JS_Video_Destroy: function (id) {
    var video = videoInstances[id];
    if (!video) return;
    video.slopCityDestroyed = true;
    if (video.slopCitySourceAdmissionTimer) clearTimeout(video.slopCitySourceAdmissionTimer);
    delete video.slopCitySourceAdmissionTimer;
    slopCityCancelCreatedVideoFrame(video);
    slopCityReleaseVideoTexture(video);
    if (video.slopCityLoadedDataListener) {
      video.removeEventListener('loadeddata', video.slopCityLoadedDataListener);
      delete video.slopCityLoadedDataListener;
    }
    if (video.slopCityMetadataListener) {
      video.removeEventListener('loadedmetadata', video.slopCityMetadataListener);
      video.removeEventListener('resize', video.slopCityMetadataListener);
      delete video.slopCityMetadataListener;
    }
    // Retain Unity's loop/gesture/callback cleanup and identity removal.
    slopCityNativeVideoDestroy(id);
    // Assigning src="" alone does not explicitly run media resource selection.
    // Empty the source and load to abort decoding/downloads on retired players.
    video.pause();
    video.muted = true;
    video.removeAttribute('src');
    video.load();
    video.slopCityRetiredAt = Date.now();
    // Reuse the same iOS element even before an audio grant: repeated street
    // handoffs must not manufacture a new WebKit media element each time.
    // Never carry permission to a different element, retain a playing source,
    // or keep more than one empty shell. Teardown drops even this final shell.
    if (!slopCityDisposingVideoDecoders && !slopCityReusableIOSVideo
      && slopCityUsesIOSVideo())
      slopCityReusableIOSVideo = video;
  }
});
