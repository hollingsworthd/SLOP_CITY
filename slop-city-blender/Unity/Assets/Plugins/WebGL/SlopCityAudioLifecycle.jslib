// Unity queues channel/clip objects while iOS suspends or interrupts WebAudio.
// Stopping only marks the first entry, and releasing does not remove any: a
// long background session can retain retired PCM buffers until context resume.
if (typeof LibraryManager.library.JS_Sound_Stop !== 'function'
  || typeof LibraryManager.library.JS_Sound_ReleaseInstance !== 'function'
  || typeof LibraryManager.library.JS_Sound_Init !== 'function') {
  throw new Error('Slop City audio lifecycle requires Unity Audio.js first.');
}
mergeInto(LibraryManager.library, {
  $slopCityNativeSoundInit: LibraryManager.library.JS_Sound_Init,
  $slopCityNativeSoundInit__deps: LibraryManager.library.JS_Sound_Init__deps.slice(),
  $slopCityNativeSoundStop: LibraryManager.library.JS_Sound_Stop,
  $slopCityNativeSoundStop__deps: LibraryManager.library.JS_Sound_Stop__deps.slice(),
  $slopCityNativeSoundRelease: LibraryManager.library.JS_Sound_ReleaseInstance,
  $slopCityNativeSoundRelease__deps: LibraryManager.library.JS_Sound_ReleaseInstance__deps.slice(),
  $slopCityAudioDisposed: false,
  $slopCityAudioIntervals: [],
  $slopCityAudioDeinitializers: [],

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
    if (slopCityAudioDisposed) return;
    slopCityAudioDisposed = true;
    var firstError;
    function retire(action) { try { action(); } catch (error) { if (!firstError) firstError = error; } }
    var context = WEBAudio.audioContext;
    if (context) context.onstatechange = null;
    WEBAudio.soundsPendingContextResume.length = 0;
    slopCityAudioIntervals.splice(0).forEach(function (id) { retire(function () { Module.clearInterval(id); }); });
    slopCityAudioDeinitializers.splice(0).forEach(function (cleanup) {
      var index = Module.deinitializers.indexOf(cleanup);
      if (index >= 0) Module.deinitializers.splice(index, 1);
      retire(cleanup);
    });
    Object.keys(WEBAudio.audioInstances).forEach(function (id) {
      var object = WEBAudio.audioInstances[id];
      // A stopped source's queued onended callback must never enter a retired
      // Wasm player. Clips do not have callbacks or a channel source.
      if (object) {
        object.callback = null;
        if (object.source) object.source.onended = null;
      }
      retire(function () { if (object && object.source) _JS_Sound_Stop(id, 0); });
      retire(function () { _JS_Sound_ReleaseInstance(id); });
      delete WEBAudio.audioInstances[id];
    });
    WEBAudio.audioWebEnabled = 0; WEBAudio.contextIsRunning = false;
    WEBAudio.audioCache.splice(0).forEach(function (element) {
      retire(function () { element.pause(); });
      retire(function () { element.removeAttribute('src'); element.load(); });
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
    // Keep native channel/node and compressed-object-URL cleanup unchanged.
    slopCityNativeSoundRelease(instance);
  }
});
