// Unity retries audible video on a gesture. A denied retry must stay queued,
// without escaping the event handler or reporting a decoded frame as ready.
mergeInto(LibraryManager.library, {
  $slopCityVideoRetryInFlight: {},

  $slopCityUsesMobileVideo: function (v) {
    if (typeof v.slopCityMobilePlayback === 'boolean') return v.slopCityMobilePlayback;
    var info = typeof Module !== 'undefined' && Module.SystemInfo;
    var nav = typeof navigator !== 'undefined' ? navigator : {};
    v.slopCityMobilePlayback = !!((info && (info.mobile || /^(iPhoneOS|iPadOS|Android)$/.test(info.os)))
      || /Android|iPhone|iPad|iPod/.test(nav.userAgent || '')
      || (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1));
    return v.slopCityMobilePlayback;
  },

  $slopCityApplyVideoMute__deps: ['$slopCityUsesMobileVideo', '$slopCityVideoRetryInFlight'],
  $slopCityApplyVideoMute: function (video, v) {
    if (slopCityUsesMobileVideo(v) && !v.slopCityAudioUnlocked) {
      var attempt = slopCityVideoRetryInFlight[video];
      // Only an owned audible play attempt may unmute a locked decoder.
      // The mixer's first nonzero gain after muted startup is not permission.
      var unlocking = attempt && attempt.video === v && attempt.audible
        && attempt.intent === v.slopCityPlaybackIntent;
      // If the spatial mix falls silent while play() waits, that gesture no
      // longer authorizes a later mixer-driven unmute on the same promise.
      if (unlocking && (v.slopCityRequestedMuted || v.volume <= 0)) attempt.audibleCancelled = true;
      v.muted = !unlocking || !!attempt.audibleCancelled || !!v.slopCityRequestedMuted;
    } else v.muted = v.slopCityMutedFallback ? true : v.slopCityRequestedMuted;
  },

  $slopCityClearVideoAttemptTimer: function (attempt) {
    if (attempt && attempt.timer) { clearTimeout(attempt.timer); delete attempt.timer; }
    if (attempt && attempt.mutedFallbackAttempt && attempt.mutedFallbackAttempt.timer) {
      clearTimeout(attempt.mutedFallbackAttempt.timer);
      delete attempt.mutedFallbackAttempt.timer;
    }
  },

  $slopCityCancelVideoPictureWatch: function (intent) {
    if (!intent || !intent.pictureWatch) return;
    clearTimeout(intent.pictureWatch.timer);
    delete intent.pictureWatch;
  },

  $slopCityWatchVideoPicture__deps: ['$videoInstances', '$slopCityCancelVideoPictureWatch'],
  $slopCityWatchVideoPicture: function (video, v, intent, expired, consumesRetry) {
    // A resolved play promise is permission to play, not proof that Unity has
    // received pixels. Keep one source-bound watch through audio queue changes.
    if (intent.pictureWatch) {
      // An audible denial may already require the one muted recovery before
      // the initial deadline. Keep its elapsed budget, but do not retry again.
      if (consumesRetry) intent.pictureWatch.expired = expired;
      return;
    }
    if (!intent.pending && v.slopCityUploadedPictureSource === intent.source) {
      intent.pictureStarted = true;
      return;
    }
    var watch = { startedAt: Date.now(), progressAt: Date.now(), expired: expired };
    intent.pictureWatch = watch;
    function snapshot() {
      var buffered = v.buffered;
      return { ready: v.readyState, time: v.currentTime,
        buffered: buffered.length ? buffered.end(buffered.length - 1) : 0 };
    }
    var progress = snapshot();
    function poll() {
      if (videoInstances[video] !== v || v.slopCityPlaybackIntent !== intent
        || intent.pictureWatch !== watch || v.src !== intent.source) {
        if (intent.pictureWatch === watch) slopCityCancelVideoPictureWatch(intent);
        return;
      }
      if (!intent.pending && v.slopCityUploadedPictureSource === intent.source) {
        intent.pictureStarted = true;
        slopCityCancelVideoPictureWatch(intent);
        return;
      }
      var now = Date.now(), next = snapshot();
      if (next.ready > progress.ready || next.buffered > progress.buffered || next.time !== progress.time)
        watch.progressAt = now;
      progress = next;
      if (now - watch.progressAt >= 10000 || now - watch.startedAt >= 30000) {
        slopCityCancelVideoPictureWatch(intent);
        watch.expired();
      } else watch.timer = setTimeout(poll,
        Math.min(1000, 10000 - (now - watch.progressAt), 30000 - (now - watch.startedAt)));
    }
    watch.timer = setTimeout(poll, 1000);
  },

  $slopCityStopUnconfirmedVideo__deps: ['$videoInstances', '$slopCityVideoRetryInFlight', '$slopCityClearVideoAttemptTimer', '$slopCityCancelVideoPictureWatch', '$jsVideoAddPendingBlockedVideo'],
  $slopCityStopUnconfirmedVideo: function (video, v, intent) {
    if (videoInstances[video] !== v || v.slopCityPlaybackIntent !== intent || v.src !== intent.source) return;
    slopCityCancelVideoPictureWatch(intent);
    slopCityClearVideoAttemptTimer(intent);
    slopCityClearVideoAttemptTimer(slopCityVideoRetryInFlight[video]);
    delete slopCityVideoRetryInFlight[video];
    intent.pending = false;
    if (intent.mutedFallbackAttempt) intent.mutedFallbackAttempt.pending = false;
    // The single automatic picture retry is spent. Ordinary Unity polling
    // must wait for a real gesture instead of starting another silent loop.
    intent.deniedAudible = false;
    intent.startupRetryEligible = false;
    intent.mutedResumeAttempted = true;
    v.muted = true;
    v.pause();
    jsVideoAddPendingBlockedVideo(video, v);
  },

  $slopCityCancelVideoFrame: function (v) {
    if (!v || !v.slopCityFrameRequest) return;
    var request = v.slopCityFrameRequest;
    delete v.slopCityFrameRequest;
    if (typeof v.cancelVideoFrameCallback === 'function') v.cancelVideoFrameCallback(request.id);
  },

  $slopCityRequestVideoFrame__deps: ['$videoInstances', '$slopCityCancelVideoFrame'],
  $slopCityRequestVideoFrame: function (video, v, intent) {
    if (videoInstances[video] !== v || typeof v.requestVideoFrameCallback !== 'function') return;
    slopCityCancelVideoFrame(v);
    var request = {};
    v.slopCityFrameRequest = request;
    request.id = v.requestVideoFrameCallback(function () {
      if (videoInstances[video] !== v || v.slopCityFrameRequest !== request
        || (intent && v.slopCityPlaybackIntent !== intent)) return;
      delete v.slopCityFrameRequest;
      v.isLoaded = true;
    });
  },

  $slopCityVideoRetryGesture__deps: ['$jsVideoAttemptToPlayBlockedVideos'],
  $slopCityVideoRetryGesture: function (event) {
    // Touch activation is granted when a tap completes, rather than at
    // touchstart in every host. Keep native play in that trusted event stack.
    if (!event || event.isTrusted !== true) return;
    if (event.type === 'pointerup' && event.pointerType === 'mouse') return;
    if (event.type === 'keydown' && (event.repeat || event.ctrlKey || event.metaKey || event.altKey
      || /^(Escape|Esc|Shift|Control|Alt|Meta|CapsLock)$/.test(event.key))) return;
    jsVideoAttemptToPlayBlockedVideos();
  },

  $slopCityTryMutedVideoFallback__deps: ['$videoInstances', '$jsVideoPendingBlockedVideos', '$slopCityVideoRetryInFlight', '$slopCityRequestVideoFrame', '$slopCityClearVideoAttemptTimer', '$slopCityUsesMobileVideo', '$slopCityWatchVideoPicture', '$slopCityCancelVideoPictureWatch', '$slopCityStopUnconfirmedVideo'],
  $slopCityTryMutedVideoFallback: function (video, v) {
    var intent = v.slopCityPlaybackIntent;
    // A sound-permission queue must not prevent recovery of an already playing
    // picture after a native browser interruption. Allow one muted restart per
    // intent (rearmed by a successful trusted retry), never an autoplay loop.
    var resumePicture = slopCityUsesMobileVideo(v) && intent && intent.pictureStarted
      && !intent.mutedResumeAttempted && v.paused && !v.ended;
    var retryStartup = slopCityUsesMobileVideo(v) && intent && intent.startupRetryEligible
      && !intent.startupRetryAttempted;
    if (!intent || (!intent.deniedAudible && !resumePicture && !retryStartup)
      || (intent.mutedFallbackAttempt && (!resumePicture || intent.mutedFallbackAttempt.pending))
      || (intent.source !== undefined && v.src !== intent.source)
      || videoInstances[video] !== v || jsVideoPendingBlockedVideos[video] !== v
      || (slopCityVideoRetryInFlight[video] && slopCityVideoRetryInFlight[video].video === v)) return;
    if (resumePicture) intent.mutedResumeAttempted = true;
    // A slow initial play gets one muted picture retry. Consume this before
    // native play so Unity's ordinary polling cannot start another attempt.
    if (retryStartup) intent.startupRetryAttempted = true;
    // Audible denial must not freeze an otherwise autoplay-eligible world picture.
    // Try muted once for this intent; keep audible recovery on the trusted queue.
    var attempt = { pending: true };
    intent.mutedFallbackAttempt = attempt;
    v.slopCityMutedFallback = attempt;
    v.muted = true;
    function current() {
      return attempt.pending && videoInstances[video] === v && v.slopCityPlaybackIntent === intent
        && intent.mutedFallbackAttempt === attempt && jsVideoPendingBlockedVideos[video] === v
        && (intent.source === undefined || v.src === intent.source);
    }
    function settled(success) {
      if (!current()) return;
      slopCityClearVideoAttemptTimer(attempt);
      attempt.pending = false;
      if (success) {
        if (!v.paused) intent.pictureStarted = true;
        slopCityRequestVideoFrame(video, v, intent);
      } else if (slopCityUsesMobileVideo(v) && v.slopCityUploadedPictureSource !== intent.source) {
        slopCityStopUnconfirmedVideo(video, v, intent);
      } else slopCityCancelVideoPictureWatch(intent);
    }
    attempt.timer = setTimeout(function () {
      if (!current()) return;
      settled(false);
      v.pause();
    }, 10000);
    if (slopCityUsesMobileVideo(v)) slopCityWatchVideoPicture(video, v, intent,
      function () { slopCityStopUnconfirmedVideo(video, v, intent); }, true);
    try {
      var result = v.play();
      if (result && typeof result.then === 'function') result.then(function () { settled(true); }).catch(function () { settled(false); });
      else settled(true);
    } catch (error) { settled(false); }
  },

  JS_Video_SetMute__proxy: 'sync',
  JS_Video_SetMute__sig: 'vii',
  JS_Video_SetMute__deps: ['$videoInstances', '$jsVideoAllAudioTracksAreDisabled', '$slopCityTryMutedVideoFallback', '$slopCityApplyVideoMute', '$slopCityUsesMobileVideo', '$jsVideoAddPendingBlockedVideo', '$jsVideoPlayPendingBlockedVideo'],
  JS_Video_SetMute: function (video, muted) {
    var v = videoInstances[video];
    v.slopCityRequestedMuted = muted || jsVideoAllAudioTracksAreDisabled(v);
    // Off-gesture unmute can pause WebKit's muted fallback. Record the mixer's
    // current request and apply it inside the existing trusted retry instead.
    slopCityApplyVideoMute(video, v);
    if (slopCityUsesMobileVideo(v) && !v.slopCityAudioUnlocked && !v.slopCityRequestedMuted
      && v.slopCityPlaybackIntent && !v.slopCityPlaybackIntent.pending && !v.paused) {
      jsVideoAddPendingBlockedVideo(video, v);
      // Some mobile browsers already grant audio after the launch tap. Ask
      // once per element, with the same rejection recovery as a trusted retry.
      if (!v.slopCityAutomaticAudioAttempted && v.volume > 0)
        jsVideoPlayPendingBlockedVideo(video, true);
    }
    if (v.slopCityRequestedMuted || slopCityUsesMobileVideo(v)) slopCityTryMutedVideoFallback(video, v);
  },

  JS_Video_Play__proxy: 'sync',
  JS_Video_Play__sig: 'vii',
  JS_Video_Play__deps: ['$videoInstances', 'JS_Video_SetLoop', '$jsVideoAllAudioTracksAreDisabled', '$jsVideoPendingBlockedVideos', '$jsVideoAddPendingBlockedVideo', '$jsVideoRemovePendingBlockedVideo', '$slopCityVideoRetryInFlight', '$slopCityRequestVideoFrame', '$slopCityClearVideoAttemptTimer', '$slopCityTryMutedVideoFallback', '$slopCityApplyVideoMute', '$slopCityUsesMobileVideo', '$slopCityWatchVideoPicture', '$slopCityCancelVideoPictureWatch'],
  JS_Video_Play: function (video, muted) {
    var v = videoInstances[video];
    if (!v) return;
    // Keep the latest audio request even while an existing play waits.
    v.slopCityRequestedMuted = muted || jsVideoAllAudioTracksAreDisabled(v);
    slopCityApplyVideoMute(video, v);
    // Unity polls paused players. A denied or unresolved play must wait for its
    // existing attempt or trusted gesture, instead of creating more promises.
    var previous = v.slopCityPlaybackIntent;
    if ((previous && previous.pending)
      || (slopCityVideoRetryInFlight[video] && slopCityVideoRetryInFlight[video].video === v)) return;
    if (jsVideoPendingBlockedVideos[video] === v) {
      if (v.slopCityRequestedMuted || slopCityUsesMobileVideo(v)) slopCityTryMutedVideoFallback(video, v);
      return;
    }
    if (previous && previous.retryAfter > Date.now()) return;
    // Pause, destroy and clip replacement invalidate asynchronous completions.
    var mobile = slopCityUsesMobileVideo(v);
    var intent = { video: v, pending: true, attemptedMuted: v.muted,
      source: mobile ? v.src : undefined };
    jsVideoRemovePendingBlockedVideo(video);
    v.slopCityPlaybackIntent = intent;
    function denied(error) {
      if (videoInstances[video] !== v || v.slopCityPlaybackIntent !== intent
        || (mobile && v.src !== intent.source)) return;
      slopCityClearVideoAttemptTimer(intent);
      slopCityCancelVideoPictureWatch(intent);
      intent.pending = false;
      intent.retryAfter = Date.now() + 5000;
      if (error && error.name === 'NotAllowedError') {
        intent.deniedAudible = !intent.attemptedMuted;
        if (slopCityUsesMobileVideo(v)) v.slopCityAudioUnlocked = false;
        jsVideoAddPendingBlockedVideo(video, v);
        if (v.slopCityRequestedMuted || slopCityUsesMobileVideo(v)) slopCityTryMutedVideoFallback(video, v);
      }
    }
    function succeeded() {
      if (videoInstances[video] !== v || v.slopCityPlaybackIntent !== intent
        || (mobile && v.src !== intent.source)) return;
      slopCityClearVideoAttemptTimer(intent);
      intent.pending = false;
      if (!v.paused) intent.pictureStarted = true;
      slopCityRequestVideoFrame(video, v, intent);
      if (slopCityUsesMobileVideo(v) && !v.slopCityAudioUnlocked && !v.slopCityRequestedMuted)
        jsVideoAddPendingBlockedVideo(video, v);
    }
    function expireStartup() {
      slopCityClearVideoAttemptTimer(intent);
      // Audio may have begun after play() resolved while the first picture was
      // still missing. Invalidate that attempt before giving the source its retry.
      if (mobile) jsVideoRemovePendingBlockedVideo(video);
      // Invalidate the unresolved completion before aborting native playback.
      // Mobile cold startup gets one muted retry; sound still needs its grant.
      v.slopCityPlaybackIntent = mobile
        ? { video: v, pending: false, source: intent.source, startupRetryEligible: true }
        : { video: v, pending: false };
      v.pause();
      jsVideoAddPendingBlockedVideo(video, v);
      if (mobile) slopCityTryMutedVideoFallback(video, v);
    }
    if (mobile) {
      slopCityWatchVideoPicture(video, v, intent, expireStartup);
    } else {
      // Preserve the desktop timeout and gesture-only recovery unchanged.
      intent.timer = setTimeout(function () {
        if (videoInstances[video] !== v || v.slopCityPlaybackIntent !== intent || !intent.pending) return;
        expireStartup();
      }, 10000);
    }
    try {
      var promise = v.play();
      if (promise && typeof promise.then === 'function') promise.then(succeeded).catch(denied);
      else succeeded();
    } catch (error) { denied(error); }
    _JS_Video_SetLoop(video, v.loop);
  },

  JS_Video_Pause__proxy: 'sync',
  JS_Video_Pause__sig: 'vi',
  JS_Video_Pause__deps: ['$videoInstances', '$jsVideoRemovePendingBlockedVideo', '$slopCityClearVideoAttemptTimer', '$slopCityCancelVideoPictureWatch'],
  JS_Video_Pause: function (video) {
    var v = videoInstances[video];
    if (v) {
      slopCityClearVideoAttemptTimer(v.slopCityPlaybackIntent);
      slopCityCancelVideoPictureWatch(v.slopCityPlaybackIntent);
      delete v.slopCityPlaybackIntent;
      v.pause();
    }
    jsVideoRemovePendingBlockedVideo(video);
    if (v && v.loopEndPollInterval) clearInterval(v.loopEndPollInterval);
  },

  $slopCityNotifyVideoPlayback__deps: ['$jsVideoPendingBlockedVideos'],
  $slopCityNotifyVideoPlayback: function () {
    var pendingCount = Object.keys(jsVideoPendingBlockedVideos).length;
    if (typeof window.CustomEvent !== 'function' || !document.dispatchEvent) return;
    try {
      document.dispatchEvent(new window.CustomEvent('slopcityvideoplayback', {
        detail: { pendingCount: pendingCount, state: pendingCount ? 'waiting' : 'ready' }
      }));
    } catch (error) { /* Optional launcher status cannot interrupt playback. */ }
  },

  $jsVideoAddPendingBlockedVideo__deps: ['$jsVideoPendingBlockedVideos', '$slopCityVideoRetryGesture', '$slopCityNotifyVideoPlayback', '$slopCityUsesMobileVideo'],
  $jsVideoAddPendingBlockedVideo: function (video, v) {
    if (slopCityUsesMobileVideo(v) && jsVideoPendingBlockedVideos[video] === v) return;
    if (Object.keys(jsVideoPendingBlockedVideos).length === 0) {
      ['mousedown', 'touchstart', 'touchend', 'pointerup', 'keydown', 'click'].forEach(function (type) {
        window.addEventListener(type, slopCityVideoRetryGesture, true);
      });
    }
    jsVideoPendingBlockedVideos[video] = v;
    slopCityNotifyVideoPlayback();
  },

  $jsVideoPlayPendingBlockedVideo__deps: ['$jsVideoPendingBlockedVideos', '$jsVideoRemovePendingBlockedVideo', '$slopCityVideoRetryInFlight', '$slopCityRequestVideoFrame', '$slopCityClearVideoAttemptTimer', '$slopCityTryMutedVideoFallback', '$slopCityUsesMobileVideo', '$slopCityWatchVideoPicture', '$slopCityStopUnconfirmedVideo'],
  $jsVideoPlayPendingBlockedVideo: function (video, automatic) {
    var pendingVideo = jsVideoPendingBlockedVideos[video];
    if (!pendingVideo) return;
    var previous = slopCityVideoRetryInFlight[video];
    if (previous && previous.video === pendingVideo) return;
    var mobile = slopCityUsesMobileVideo(pendingVideo);
    var intent = pendingVideo.slopCityPlaybackIntent;
    if (automatic && (!mobile || pendingVideo.slopCityAutomaticAudioAttempted)) return;
    if (mobile && (!intent || intent.pending)) return;
    // A play promise can resolve before the first world picture. Keep automatic
    // unmute from interrupting that startup; real gestures remain available.
    if (automatic && pendingVideo.slopCityUploadedPictureSource !== intent.source) return;
    // Mixer polling must let a pending muted picture retry finish. A real tap
    // may still replace that retry synchronously to request sound permission.
    if (automatic && intent.mutedFallbackAttempt && intent.mutedFallbackAttempt.pending) return;
    // No need to interrupt an already moving, intentionally silent picture.
    // Keep audio recovery for the next gesture with a nonzero spatial mix.
    if (mobile && pendingVideo.slopCityRequestedMuted && !pendingVideo.paused) return;
    // A token identifies this attempt even if pause/destroy removes and requeues
    // the same decoder before the native promise settles.
    var attempt = { video: pendingVideo, intent: intent,
      audible: mobile && !pendingVideo.slopCityRequestedMuted && pendingVideo.volume > 0 };
    if (mobile && attempt.audible) pendingVideo.slopCityAutomaticAudioAttempted = true;
    slopCityVideoRetryInFlight[video] = attempt;
    function current() {
      return jsVideoPendingBlockedVideos[video] === pendingVideo
        && slopCityVideoRetryInFlight[video] === attempt
        && (!mobile || pendingVideo.slopCityPlaybackIntent === intent);
    }
    function succeeded() {
      if (!current()) return;
      if (mobile) {
        if (!pendingVideo.paused) {
          intent.pictureStarted = true;
          if (!automatic) intent.mutedResumeAttempted = false;
        }
        if (attempt.audible && !attempt.audibleCancelled && !pendingVideo.paused
          && !pendingVideo.muted && !pendingVideo.slopCityRequestedMuted
          && pendingVideo.volume > 0) pendingVideo.slopCityAudioUnlocked = true;
        else {
          // A successful muted retry grants picture playback, not sound.
          slopCityClearVideoAttemptTimer(attempt);
          delete slopCityVideoRetryInFlight[video];
          if (!pendingVideo.slopCityAudioUnlocked) pendingVideo.muted = true;
          slopCityRequestVideoFrame(video, pendingVideo, intent);
          return;
        }
      }
      jsVideoRemovePendingBlockedVideo(video, mobile);
      slopCityRequestVideoFrame(video, pendingVideo, pendingVideo.slopCityPlaybackIntent);
    }
    function failed(error) {
      if (!current()) return;
      slopCityClearVideoAttemptTimer(attempt);
      delete slopCityVideoRetryInFlight[video];
      // Permission denial can change on the next gesture. Terminal failures,
      // including the inline-only guard, retain the world poster and stop retrying.
      if (!error || error.name !== 'NotAllowedError') jsVideoRemovePendingBlockedVideo(video);
      else if (pendingVideo.slopCityMutedFallback || (mobile && attempt.audible)) {
        // One trusted audible retry may fail without freezing its muted picture.
        if (mobile) pendingVideo.slopCityAudioUnlocked = false;
        pendingVideo.muted = true;
        var intent = pendingVideo.slopCityPlaybackIntent;
        if (intent) {
          if (mobile && attempt.audible) intent.deniedAudible = true;
          delete intent.mutedFallbackAttempt;
        }
        slopCityTryMutedVideoFallback(video, pendingVideo);
      }
    }
    attempt.timer = setTimeout(function () {
      if (!current()) return;
      slopCityClearVideoAttemptTimer(attempt);
      delete slopCityVideoRetryInFlight[video];
      pendingVideo.pause();
      // Invalidate this audible completion before restoring a previously moving
      // muted picture. This restoration gets one attempt, never a polling loop.
      if (pendingVideo.slopCityMutedFallback || (mobile && attempt.audible)) {
        if (mobile) pendingVideo.slopCityAudioUnlocked = false;
        pendingVideo.muted = true;
        var intent = pendingVideo.slopCityPlaybackIntent;
        if (intent) {
          if (mobile && attempt.audible) intent.deniedAudible = true;
          delete intent.mutedFallbackAttempt;
        }
        slopCityTryMutedVideoFallback(video, pendingVideo);
      }
      // Keep the pending entry, so only the next trusted gesture retries audio.
    }, automatic ? 1500 : 10000);
    if (mobile) slopCityWatchVideoPicture(video, pendingVideo, intent,
      function () { slopCityStopUnconfirmedVideo(video, pendingVideo, intent); });
    try {
      if (pendingVideo.slopCityMutedFallback) {
        slopCityClearVideoAttemptTimer(pendingVideo.slopCityMutedFallback);
        pendingVideo.slopCityMutedFallback.pending = false;
        pendingVideo.muted = !!pendingVideo.slopCityRequestedMuted;
      }
      if (mobile) pendingVideo.muted = !attempt.audible;
      // Call native play now. Trusted retries stay in the gesture stack; the
      // single automatic attempt lets the browser decide its existing grant.
      var result = pendingVideo.play();
      if (result && typeof result.then === 'function') result.then(succeeded).catch(failed);
      else succeeded();
    } catch (error) { failed(error); }
  },

  $jsVideoRemovePendingBlockedVideo__deps: ['$videoInstances', '$jsVideoPendingBlockedVideos', '$slopCityVideoRetryGesture', '$slopCityVideoRetryInFlight', '$slopCityNotifyVideoPlayback', '$slopCityCancelVideoFrame', '$slopCityClearVideoAttemptTimer', '$slopCityCancelVideoPictureWatch'],
  $jsVideoRemovePendingBlockedVideo: function (video, preservePictureWatch) {
    // Unity's unchanged destroy helper calls this before clearing its source.
    if (!preservePictureWatch)
      slopCityCancelVideoPictureWatch(videoInstances[video] && videoInstances[video].slopCityPlaybackIntent);
    slopCityClearVideoAttemptTimer(videoInstances[video] && videoInstances[video].slopCityPlaybackIntent);
    slopCityClearVideoAttemptTimer(slopCityVideoRetryInFlight[video]);
    slopCityCancelVideoFrame(videoInstances[video]);
    if (jsVideoPendingBlockedVideos[video] !== videoInstances[video])
      slopCityCancelVideoFrame(jsVideoPendingBlockedVideos[video]);
    if (videoInstances[video]) delete videoInstances[video].slopCityMutedFallback;
    delete jsVideoPendingBlockedVideos[video];
    delete slopCityVideoRetryInFlight[video];
    if (Object.keys(jsVideoPendingBlockedVideos).length === 0) {
      ['mousedown', 'touchstart', 'touchend', 'pointerup', 'keydown', 'click'].forEach(function (type) {
        window.removeEventListener(type, slopCityVideoRetryGesture, true);
      });
    }
    slopCityNotifyVideoPlayback();
  }
});
