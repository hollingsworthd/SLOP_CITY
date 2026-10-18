// Unity queues channel/clip objects while iOS suspends or interrupts WebAudio.
// Stopping only marks the first entry, and releasing does not remove any: a
// long background session can retain retired PCM buffers until context resume.
if (typeof LibraryManager.library.JS_Sound_Stop !== 'function'
  || typeof LibraryManager.library.JS_Sound_ReleaseInstance !== 'function') {
  throw new Error('Slop City audio lifecycle requires Unity Audio.js first.');
}
mergeInto(LibraryManager.library, {
  $slopCityNativeSoundStop: LibraryManager.library.JS_Sound_Stop,
  $slopCityNativeSoundStop__deps: LibraryManager.library.JS_Sound_Stop__deps.slice(),
  $slopCityNativeSoundRelease: LibraryManager.library.JS_Sound_ReleaseInstance,
  $slopCityNativeSoundRelease__deps: LibraryManager.library.JS_Sound_ReleaseInstance__deps.slice(),

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
