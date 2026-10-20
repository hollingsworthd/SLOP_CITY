// Unity queues channel/clip objects while iOS suspends or interrupts WebAudio.
// Stopping only marks the first entry, and releasing does not remove any: a
// long background session can retain retired PCM buffers until context resume.
if (typeof LibraryManager.library.JS_Sound_Stop !== 'function'
  || typeof LibraryManager.library.JS_Sound_ReleaseInstance !== 'function'
  || typeof LibraryManager.library.JS_Sound_Init !== 'function'
  || typeof LibraryManager.library.$jsAudioCreateChannel !== 'function'
  || typeof LibraryManager.library.$jsAudioCreateUncompressedSoundClip !== 'function') {
  throw new Error('Slop City audio lifecycle requires Unity Audio.js first.');
}
mergeInto(LibraryManager.library, {
  $slopCityNativeSoundInit: LibraryManager.library.JS_Sound_Init,
  $slopCityNativeSoundInit__deps: LibraryManager.library.JS_Sound_Init__deps.slice(),
  $slopCityNativeSoundStop: LibraryManager.library.JS_Sound_Stop,
  $slopCityNativeSoundStop__deps: LibraryManager.library.JS_Sound_Stop__deps.slice(),
  $slopCityNativeSoundRelease: LibraryManager.library.JS_Sound_ReleaseInstance,
  $slopCityNativeSoundRelease__deps: LibraryManager.library.JS_Sound_ReleaseInstance__deps.slice(),
  $slopCityNativeAudioCreateChannel: LibraryManager.library.$jsAudioCreateChannel,
  $slopCityNativeAudioCreateChannel__deps: LibraryManager.library.$jsAudioCreateChannel__deps.slice(),
  $slopCityNativeAudioCreatePcmClip: LibraryManager.library.$jsAudioCreateUncompressedSoundClip,
  $slopCityNativeAudioCreatePcmClip__deps: LibraryManager.library.$jsAudioCreateUncompressedSoundClip__deps.slice(),
  $slopCityAudioDisposed: false,
  $slopCityAudioIntervals: [],
  $slopCityAudioDeinitializers: [],

  $jsAudioCreateUncompressedSoundClip__deps: ['$slopCityNativeAudioCreatePcmClip', '$slopCityAudioDisposed'],
  $jsAudioCreateUncompressedSoundClip: function (buffer, error) {
    var clip = slopCityNativeAudioCreatePcmClip(buffer, error), pcm = buffer, retired = false;
    buffer = null; // The wrapper activation has only one retained PCM owner.
    // Native release() is a no-op. decodeAudioData may finish after an instance
    // was released; its callback must not restore PCM on that retired owner.
    Object.defineProperty(clip, 'buffer', {
      configurable: true,
      get: function () { return pcm; },
      set: function (value) { pcm = retired || slopCityAudioDisposed ? null : value; }
    });
    clip.release = function () { retired = true; pcm = null; };
    return clip;
  },

  $slopCityRetireCompressedAudioSource: function (source) {
    if (!source || !source.mediaElement) return;
    // Unity's disconnectSource pauses and disconnects the node, but never calls
    // its release() or cancels its scheduled start. That start can otherwise
    // play a detached element after its AudioSource has been stopped/destroyed.
    source.slopCityRetired = true;
    if (source.playTimeout != null) clearTimeout(source.playTimeout);
    source.playTimeout = null;
    source._startPlayback = function () {};
    source.start = function () {};
    source.pauseRequested = true;
    source.isStopped = true;
    var firstError;
    function retire(action) { try { action(); } catch (error) { if (!firstError) firstError = error; } }
    retire(function () { source.onended = null; });
    retire(function () { source.mediaElement.pause(); });
    retire(function () { source.mediaElement.removeAttribute('src'); });
    retire(function () { source.mediaElement.load(); });
    if (firstError) throw firstError;
  },

  $jsAudioCreateChannel__deps: ['$slopCityNativeAudioCreateChannel', '$slopCityRetireCompressedAudioSource'],
  $jsAudioCreateChannel: function (callback, userData) {
    var channel = slopCityNativeAudioCreateChannel(callback, userData);
    var disconnect = channel.disconnectSource, release = channel.release, play = channel.playSoundClip;
    channel.disconnectSource = function () {
      var source = this.source, firstError;
      try { disconnect.call(this); } catch (error) { firstError = error; }
      try { slopCityRetireCompressedAudioSource(source); }
      catch (error) {
        // Keep a failed retirement reachable for the next stop/release. Do not
        // admit a new source over it in playSoundClip below.
        if (!this.source) this.source = source;
        if (!firstError) firstError = error;
      }
      if (firstError) throw firstError;
    };
    channel.playSoundClip = function (clip, startTime, startOffset) {
      if (this.source && this.source.slopCityRetired) {
        try { this.disconnectSource(); }
        catch (error) { console.warn('Retired audio source cleanup failed.', error); return; }
      }
      return play.call(this, clip, startTime, startOffset);
    };
    channel.release = function () {
      var firstError;
      try { release.call(this); } catch (error) { firstError = error; }
      // Pinned Unity releases gain/panner but leaves both spatial blend nodes
      // connected. All four nodes belong to this channel, including 3D sounds.
      try { this.gain.disconnect(); } catch (error) { if (!firstError) firstError = error; }
      try { this.panner.disconnect(); } catch (error) { if (!firstError) firstError = error; }
      try { this.spatialBlendDryGain.disconnect(); } catch (error) { if (!firstError) firstError = error; }
      try { this.spatialBlendWetGain.disconnect(); } catch (error) { if (!firstError) firstError = error; }
      if (firstError) throw firstError;
    };
    return channel;
  },

  JS_Sound_Init__proxy: 'sync',
  JS_Sound_Init__sig: 'v',
  JS_Sound_Init__deps: ['$slopCityNativeSoundInit', '$slopCityAudioDisposed', '$slopCityAudioIntervals', '$slopCityAudioDeinitializers', '$slopCityDisposeOwnedAudio'],
  JS_Sound_Init: function () {
    if (slopCityAudioDisposed) return;
    var start = Module.deinitializers.length, original = Module.setInterval;
    // Capture only this synchronous native init's timer/listener ownership.
    // Do not clear other engine timers or run unrelated deinitializers.
    var ownedInterval = function () {
      var id = original.apply(this, arguments);
      if (id !== undefined) slopCityAudioIntervals.push(id);
      return id;
    };
    Module.setInterval = ownedInterval;
    try { return slopCityNativeSoundInit(); }
    finally {
      if (Module.setInterval === ownedInterval) Module.setInterval = original;
      Array.prototype.push.apply(slopCityAudioDeinitializers, Module.deinitializers.slice(start));
    }
  },

  $slopCityDisposeOwnedAudio__deps: ['$WEBAudio', '$slopCityAudioDisposed', '$slopCityAudioIntervals', '$slopCityAudioDeinitializers', 'JS_Sound_Stop', 'JS_Sound_ReleaseInstance'],
  $slopCityDisposeOwnedAudio__postset: "Module['slopCityDisposeOwnedAudio'] = slopCityDisposeOwnedAudio;",
  $slopCityDisposeOwnedAudio: function () {
    // Disposal blocks new admissions immediately, but a refused native release
    // remains owned and may be retried by the terminal/pagehide cleanup.
    slopCityAudioDisposed = true;
    var firstError;
    function retire(action) { try { action(); return true; } catch (error) { if (!firstError) firstError = error; return false; } }
    var context = WEBAudio.audioContext;
    if (context) retire(function () { context.onstatechange = null; });
    WEBAudio.soundsPendingContextResume.length = 0;
    slopCityAudioIntervals.slice().forEach(function (id) {
      if (retire(function () { Module.clearInterval(id); })) slopCityAudioIntervals.splice(slopCityAudioIntervals.indexOf(id), 1);
    });
    slopCityAudioDeinitializers.slice().forEach(function (cleanup) {
      if (retire(cleanup)) {
        var index = Module.deinitializers.indexOf(cleanup);
        if (index >= 0) Module.deinitializers.splice(index, 1);
        slopCityAudioDeinitializers.splice(slopCityAudioDeinitializers.indexOf(cleanup), 1);
      }
    });
    Object.keys(WEBAudio.audioInstances).forEach(function (id) {
      var object = WEBAudio.audioInstances[id];
      // A stopped source's queued onended callback must never enter a retired
      // Wasm player. Clips do not have callbacks or a channel source.
      if (object) {
        object.callback = null;
        retire(function () { if (object.source) object.source.onended = null; });
      }
      retire(function () { if (object && object.source) _JS_Sound_Stop(id, 0); });
      retire(function () { _JS_Sound_ReleaseInstance(id); });
      // Native release removes its instance only after it succeeds. Never
      // erase that last owner after a source/node cleanup exception.
    });
    WEBAudio.audioWebEnabled = 0; WEBAudio.contextIsRunning = false;
    WEBAudio.audioCache.slice().forEach(function (element) {
      var clean = retire(function () { element.pause(); });
      clean = retire(function () { element.removeAttribute('src'); }) && clean;
      clean = retire(function () { element.load(); }) && clean;
      if (clean) WEBAudio.audioCache.splice(WEBAudio.audioCache.indexOf(element), 1);
    });
    Object.keys(WEBAudio.pendingAudioSources || {}).forEach(function (id) { delete WEBAudio.pendingAudioSources[id]; });
    if (context && context.state !== 'closed' && typeof context.close === 'function') {
      retire(function () {
        Promise.resolve(context.close()).catch(function (error) { console.warn('Session audio context cleanup failed.', error); });
      });
    }
    if (firstError) throw firstError;
  },

  $slopCityRemovePendingSound__deps: ['$WEBAudio'],
  $slopCityRemovePendingSound: function (object) {
    if (!object) return;
    var queue = WEBAudio.soundsPendingContextResume;
    for (var i = queue.length - 1; i >= 0; --i) {
      if (queue[i].channel === object || queue[i].clip === object) queue.splice(i, 1);
    }
  },

  JS_Sound_Stop__proxy: 'sync',
  JS_Sound_Stop__sig: 'vid',
  JS_Sound_Stop__deps: ['$WEBAudio', '$slopCityNativeSoundStop', '$slopCityRemovePendingSound'],
  JS_Sound_Stop: function (channelInstance, delay) {
    if (WEBAudio.audioWebEnabled == 0) return;
    var channel = WEBAudio.audioInstances[channelInstance];
    if (!channel) return;
    if (delay === 0) {
      slopCityRemovePendingSound(channel);
      // Also retire a suspended channel's paused-source buffer. Merely removing
      // its resume entry leaves that buffer reachable through the live channel.
      channel.stop(0);
      // Native disconnectSource deliberately skips paused mock nodes. An
      // immediate stop cancels their future resume, so release that PCM owner.
      if (channel.source && channel.source.isPausedMockNode) delete channel.source;
    } else if (!WEBAudio.contextIsRunning) {
      WEBAudio.soundsPendingContextResume.forEach(function (sound) {
        if (sound.channel === channel) sound.stopDelay = delay;
      });
    } else {
      slopCityNativeSoundStop(channelInstance, delay);
    }
  },

  JS_Sound_ReleaseInstance__proxy: 'async',
  JS_Sound_ReleaseInstance__sig: 'vi',
  JS_Sound_ReleaseInstance__deps: ['$WEBAudio', '$slopCityNativeSoundRelease', '$slopCityRemovePendingSound'],
  JS_Sound_ReleaseInstance: function (instance) {
    slopCityRemovePendingSound(WEBAudio.audioInstances[instance]);
    // Preserve native instance removal and compressed-clip URL revocation;
    // channel.release additionally retires its owned source and all four nodes.
    slopCityNativeSoundRelease(instance);
  }
});
