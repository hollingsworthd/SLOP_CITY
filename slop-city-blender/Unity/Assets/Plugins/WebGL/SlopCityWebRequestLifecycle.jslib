// Original game-owned adapter; Unity's implementation stays in the installed
// engine. Fence callbacks before they can republish a retired request's body.
// Pinned Send selects these Module transports synchronously, then observes the
// returned promise through exactly one then(...).catch(...). An engine upgrade
// must re-audit this contract; no global Fetch implementation is replaced.
if (typeof LibraryManager.library.JS_WebRequest_Send !== 'function'
  || typeof LibraryManager.library.JS_WebRequest_Release !== 'function'
  || LibraryManager.library.JS_WebRequest_Send.length !== 6
  || !/Module\.fetchWithProgress/.test(LibraryManager.library.JS_WebRequest_Send.toString())
  || !/requestOptions\.init\.onProgress\s*=\s*HandleProgress/.test(LibraryManager.library.JS_WebRequest_Send.toString())) {
  throw new Error('Slop City request lifecycle requires the audited Unity WebRequest library first.');
}
mergeInto(LibraryManager.library, {
  $slopCityNativeWebRequestSend: LibraryManager.library.JS_WebRequest_Send,
  $slopCityNativeWebRequestSend__deps: LibraryManager.library.JS_WebRequest_Send__deps.slice(),
  $slopCityNativeWebRequestRelease: LibraryManager.library.JS_WebRequest_Release,
  $slopCityNativeWebRequestRelease__deps: LibraryManager.library.JS_WebRequest_Release__deps.slice(),
  $slopCityWebRequestTokens: {},

  $slopCityOwnsWebRequest__deps: ['$wr', '$slopCityWebRequestTokens'],
  $slopCityOwnsWebRequest: function (token) {
    return !token.retired && slopCityWebRequestTokens[token.id] === token
      && wr.requests[token.id] === token.options;
  },

  $slopCityReleaseWebRequestBuffer__deps: ['free'],
  $slopCityReleaseWebRequestBuffer: function (token) {
    // A ReceiveData callback may dispose its request reentrantly. Its native
    // progress stack must finish using the borrowed pointer before it is freed.
    if (token.progressDepth) { token.releaseBuffer = true; return; }
    token.releaseBuffer = false;
    var pointer = token.options.tempBuffer;
    token.options.tempBuffer = null;
    token.options.tempBufferSize = 0;
    if (pointer) _free(pointer);
  },

  $slopCityRetireWebRequest__deps: ['$slopCityReleaseWebRequestBuffer'],
  $slopCityRetireWebRequest: function (token) {
    token.retired = true;
    try {
      if (!token.finished && token.controller && !token.controller.signal.aborted)
        token.controller.abort();
    } finally { slopCityReleaseWebRequestBuffer(token); }
  },

  $slopCityGuardWebRequestTransport__deps: ['$slopCityOwnsWebRequest', '$slopCityReleaseWebRequestBuffer'],
  $slopCityGuardWebRequestTransport: function (token, transport, receiver, args) {
    // cachedFetch may synchronously delegate to fetchWithProgress. Wrap this
    // request once, but leave every unrelated/reentrant request untouched.
    if (args[1] !== token.options.init || token.transportStarted)
      return transport.apply(receiver, args);
    token.transportStarted = true;
    var progress = token.options.init.onProgress;
    token.options.init.onProgress = function () {
      if (!slopCityOwnsWebRequest(token) || token.finished || token.controller.signal.aborted) return;
      token.progressDepth++;
      try { return progress.apply(this, arguments); }
      finally {
        token.progressDepth--;
        if (token.releaseBuffer) slopCityReleaseWebRequestBuffer(token);
      }
    };
    var result;
    try { result = transport.apply(receiver, args); }
    catch (error) { token.finished = true; slopCityReleaseWebRequestBuffer(token); throw error; }
    // This deliberately implements only pinned Send's private transport ABI.
    // Native body parsing, cache selection, callbacks and error codes remain
    // native; no second body buffer or never-settling cancellation promise.
    return {
      then: function (success) {
        var completion = Promise.resolve(result).then(function (response) {
          if (!slopCityOwnsWebRequest(token)) return;
          if (token.controller.signal.aborted) {
            var error = new Error('Request aborted.');
            error.name = 'AbortError';
            throw error;
          }
          token.finished = true;
          // Native completion otherwise frees this streaming scratch after its
          // callback without clearing the pointer. Retire it once beforehand;
          // the completed callback borrows no progress data. Full-body native
          // allocation/copy/free stays entirely inside the native helper.
          slopCityReleaseWebRequestBuffer(token);
          return success(response);
        });
        return {
          catch: function (failure) {
            return completion.catch(function (error) {
              if (!slopCityOwnsWebRequest(token)) return;
              token.finished = true;
              slopCityReleaseWebRequestBuffer(token);
              return failure(error);
            });
          }
        };
      }
    };
  },

  JS_WebRequest_Send__proxy: 'sync',
  JS_WebRequest_Send__sig: 'viiiiii',
  JS_WebRequest_Send__deps: ['$wr', '$slopCityNativeWebRequestSend', '$slopCityWebRequestTokens', '$slopCityRetireWebRequest', '$slopCityGuardWebRequestTransport', '$slopCityOwnsWebRequest'],
  JS_WebRequest_Send: function (id, ptr, length, arg, response, progress) {
    var options = wr.requests[id];
    if (!options) return;
    var previous = slopCityWebRequestTokens[id];
    var token = { id: id, options: options, controller: wr.abortControllers[id],
      retired: false, finished: false, progressDepth: 0, releaseBuffer: false, transportStarted: false };
    slopCityWebRequestTokens[id] = token;
    if (previous) slopCityRetireWebRequest(previous);
    // Aborting the previous transport may synchronously start another request.
    if (!slopCityOwnsWebRequest(token)) return;
    var names = ['fetchWithProgress', 'cachedFetch'];
    var saved = [], hooks = [];
    try {
      names.forEach(function (name, index) {
        var native = Module[name];
        saved[index] = native;
        if (typeof native !== 'function') return;
        var hook = function () { return slopCityGuardWebRequestTransport(token, native, this, arguments); };
        hooks[index] = hook;
        Module[name] = hook;
        if (Module[name] !== hook) throw new Error('Request transport cannot be fenced.');
      });
      return slopCityNativeWebRequestSend(id, ptr, length, arg, response, progress);
    } finally {
      names.forEach(function (name, index) {
        if (hooks[index] && Module[name] === hooks[index]) Module[name] = saved[index];
      });
    }
  },

  JS_WebRequest_Release__proxy: 'sync',
  JS_WebRequest_Release__sig: 'vi',
  JS_WebRequest_Release__deps: ['$slopCityNativeWebRequestRelease', '$slopCityWebRequestTokens', '$slopCityRetireWebRequest'],
  JS_WebRequest_Release: function (id) {
    var token = slopCityWebRequestTokens[id];
    // Remove the native identity before abort listeners can synchronously
    // create a new generation at this ID; never erase that replacement later.
    if (token) token.retired = true;
    if (slopCityWebRequestTokens[id] === token) delete slopCityWebRequestTokens[id];
    try { return slopCityNativeWebRequestRelease(id); }
    finally { if (token) slopCityRetireWebRequest(token); }
  }
});
