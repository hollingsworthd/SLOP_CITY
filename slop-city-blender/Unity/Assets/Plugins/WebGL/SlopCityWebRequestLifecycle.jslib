// Original game-owned adapter for the pinned request/transport/callback ABI.
// Own every borrowed Wasm allocation: native Send writes through unchecked
// malloc results and misses cleanup when a response callback throws. Keep the
// native create/abort/release and Module transports; never replace global Fetch.
if (typeof LibraryManager.library.JS_WebRequest_Create !== 'function'
  || typeof LibraryManager.library.JS_WebRequest_Send !== 'function'
  || typeof LibraryManager.library.JS_WebRequest_Release !== 'function'
  || LibraryManager.library.JS_WebRequest_Send.length !== 6
  || !/Module\.fetchWithProgress/.test(LibraryManager.library.JS_WebRequest_Send.toString())
  || !/requestOptions\.init\.onProgress\s*=\s*HandleProgress/.test(LibraryManager.library.JS_WebRequest_Send.toString())) {
  throw new Error('Slop City request lifecycle requires the audited Unity WebRequest library first.');
}
mergeInto(LibraryManager.library, {
  $slopCityNativeWebRequestCreate: LibraryManager.library.JS_WebRequest_Create,
  $slopCityNativeWebRequestCreate__deps: LibraryManager.library.JS_WebRequest_Create__deps.slice(),
  $slopCityNativeWebRequestRelease: LibraryManager.library.JS_WebRequest_Release,
  $slopCityNativeWebRequestRelease__deps: LibraryManager.library.JS_WebRequest_Release__deps.slice(),
  $slopCityWebRequestTokens: {},
  $slopCityWebRequestsDisposed: false,

  JS_WebRequest_Create__proxy: 'sync',
  JS_WebRequest_Create__sig: 'iii',
  JS_WebRequest_Create__deps: ['$slopCityNativeWebRequestCreate', '$slopCityWebRequestsDisposed', '$slopCityAbortOwnedWebRequests'],
  JS_WebRequest_Create: function (url, method) {
    return slopCityWebRequestsDisposed ? 0 : slopCityNativeWebRequestCreate(url, method);
  },

  $slopCityAllocateRequestBytes__deps: ['malloc'],
  $slopCityAllocateRequestBytes: function (size) {
    if (!Number.isSafeInteger(size) || size <= 0 || size > HEAPU8.byteLength)
      throw new Error('Invalid or oversized request buffer.');
    var pointer = _malloc(size);
    // malloc can grow Wasm memory. Read the current heap after it returns,
    // never a view captured before growth or a wrapping pointer+size sum.
    if (!Number.isSafeInteger(pointer) || pointer <= 0 || pointer > HEAPU8.byteLength - size)
      throw new Error('Request memory allocation failed.');
    return pointer;
  },

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

  $slopCityStopRequestSession__deps: ['$slopCityAbortOwnedWebRequests'],
  $slopCityStopRequestSession: function (error) {
    try { slopCityAbortOwnedWebRequests(); }
    catch (cleanupError) { console.warn('Session request cleanup failed.', cleanupError); }
    // A native callback trap is a game failure, never a recoverable Fetch
    // error. The launcher owns the visible terminal UI and full teardown.
    if (typeof Module.onAbort === 'function') { Module.onAbort(error); return; }
    if (typeof Module.pauseMainLoop === 'function') Module.pauseMainLoop();
    throw error; // A host without the required launcher must not hide the trap.
  },

  JS_WebRequest_Send__proxy: 'sync',
  JS_WebRequest_Send__sig: 'viiiiii',
  JS_WebRequest_Send__deps: ['$wr', '$slopCityWebRequestTokens', '$slopCityWebRequestsDisposed', '$slopCityRetireWebRequest', '$slopCityOwnsWebRequest', '$slopCityReleaseWebRequestBuffer', '$slopCityAllocateRequestBytes', '$slopCityStopRequestSession', '$getWasmTableEntry', '$lengthBytesUTF8', '$stringToUTF8', 'free', '$slopCityAbortOwnedWebRequests'],
  JS_WebRequest_Send: function (id, ptr, length, arg, response, progress) {
    var options = wr.requests[id];
    if (!options || slopCityWebRequestsDisposed) return;
    var previous = slopCityWebRequestTokens[id];
    var token = { id: id, options: options, controller: wr.abortControllers[id],
      retired: false, finished: false, progressDepth: 0, releaseBuffer: false };
    slopCityWebRequestTokens[id] = token;
    if (previous) slopCityRetireWebRequest(previous);
    // Aborting the previous transport may synchronously start another request.
    if (!slopCityOwnsWebRequest(token)) return;
    function clearTimer() {
      if (wr.timer[id]) { clearTimeout(wr.timer[id]); delete wr.timer[id]; }
    }
    function failed(error) {
      if (!slopCityOwnsWebRequest(token) || token.finished) return;
      token.finished = true; clearTimer(); slopCityReleaseWebRequestBuffer(token);
      if (!response) return;
      var message = String(error && error.message || error || 'Request failed.').slice(0, 4096);
      var code = options.isTimedOut ? 14 : token.controller.signal.aborted ? 17 : 2;
      var buffer = 0, fatal;
      try {
        try {
          var bytes = lengthBytesUTF8(message) + 1;
          buffer = slopCityAllocateRequestBytes(bytes);
          stringToUTF8(message, buffer, bytes);
        } catch (allocationError) { fatal = allocationError; }
        // Native error messages require a valid terminated string. If even
        // that tiny allocation fails, stop safely without calling into Wasm.
        if (!fatal) getWasmTableEntry(response)(arg, 500, 0, 0, buffer, code);
      } catch (callbackError) {
        fatal = callbackError;
      } finally { if (buffer) _free(buffer); }
      if (fatal) slopCityStopRequestSession(fatal);
    }
    options.init.onProgress = function (event) {
      if (!slopCityOwnsWebRequest(token) || token.finished || token.controller.signal.aborted || !progress) return;
      var pointer = 0, local = false, size = 0;
      try {
        // Pinned arrayBuffer fallback reports undefined total (buffer.length).
        // Its native integer callback treats that as zero.
        var total = event.total === undefined ? 0 : event.total;
        if (!Number.isSafeInteger(event.loaded) || event.loaded < 0 || event.loaded > 2147483647
          || !Number.isSafeInteger(total) || total < 0 || total > 2147483647)
          throw new Error('Invalid request progress lengths.');
        if (event.chunk) {
          if (!(event.chunk instanceof Uint8Array)) throw new Error('Invalid request progress bytes.');
          size = event.chunk.byteLength;
          if (size) {
            // Reentrant progress must not resize a pointer an outer callback
            // still borrows. That rare nested call gets its own temporary.
            local = token.progressDepth > 0;
            if (local) pointer = slopCityAllocateRequestBytes(size);
            else {
              if (!options.tempBuffer || options.tempBufferSize < size) {
                var capacity = Math.max(1024, size);
                var next = slopCityAllocateRequestBytes(capacity), previous = options.tempBuffer;
                options.tempBuffer = next; options.tempBufferSize = capacity;
                if (previous) _free(previous);
              }
              pointer = options.tempBuffer;
            }
            HEAPU8.set(event.chunk, pointer);
          }
        }
      } catch (error) {
        if (local && pointer) _free(pointer);
        failed(error); token.controller.abort(); return;
      }
      wr.responses[id] = event.response;
      token.progressDepth++;
      var callbackError;
      try { getWasmTableEntry(progress)(arg, event.response.status, event.loaded, total, pointer, size); }
      catch (error) { callbackError = error; }
      finally {
        token.progressDepth--;
        if (local && pointer) _free(pointer);
        if (token.releaseBuffer) slopCityReleaseWebRequestBuffer(token);
      }
      if (callbackError) {
        token.finished = true; clearTimer(); slopCityReleaseWebRequestBuffer(token);
        token.controller.abort(); slopCityStopRequestSession(callbackError);
      }
    };
    try {
      if (!Number.isSafeInteger(length) || length < 0 || !Number.isSafeInteger(ptr) || ptr < 0
        || (length && (ptr === 0 || ptr > HEAPU8.byteLength - length))) throw new Error('Invalid request body range.');
      if (length) options.init.body = new Blob([HEAPU8.subarray(ptr, ptr + length)]);
      if (options.timeout) wr.timer[id] = setTimeout(function () { options.isTimedOut = true; token.controller.abort(); }, options.timeout);
      var transport = Module.fetchWithProgress;
      if (Module.companyName && Module.productName && Module.cachedFetch) {
        transport = Module.cachedFetch;
        options.init.companyName = Module.companyName; options.init.productName = Module.productName;
        options.init.productVersion = Module.productVersion; options.init.control = Module.cacheControl(options.url);
      }
      Promise.resolve(transport(options.url, options.init)).then(function (result) {
        if (!slopCityOwnsWebRequest(token) || token.finished) return;
        if (token.controller.signal.aborted) { failed(new Error('Request aborted.')); return; }
        var body = result.parsedBody;
        if (!(body instanceof Uint8Array) || body.byteLength > HEAPU8.byteLength) throw new Error('Invalid or oversized request response bytes.');
        var buffer = 0, callbackError;
        try {
          if (response && !options.init.enableStreamingDownload && body.byteLength) {
            buffer = slopCityAllocateRequestBytes(body.byteLength);
            HEAPU8.set(body, buffer);
          }
          token.finished = true; clearTimer(); slopCityReleaseWebRequestBuffer(token);
          wr.responses[id] = result;
          if (response) {
            try { getWasmTableEntry(response)(arg, result.status, buffer, body.byteLength, 0, 0); }
            catch (error) { callbackError = error; }
          }
        } finally { if (buffer) _free(buffer); }
        if (callbackError) slopCityStopRequestSession(callbackError);
      }).catch(failed);
    } catch (error) { failed(error); }
  },

  $slopCityAbortOwnedWebRequests__deps: ['$wr', '$slopCityWebRequestTokens', '$slopCityWebRequestsDisposed', 'JS_WebRequest_Release'],
  $slopCityAbortOwnedWebRequests__postset: "Module['slopCityAbortOwnedWebRequests'] = slopCityAbortOwnedWebRequests;",
  $slopCityAbortOwnedWebRequests: function () {
    if (slopCityWebRequestsDisposed) return;
    slopCityWebRequestsDisposed = true;
    var firstError;
    Object.keys(wr.requests).forEach(function (id) {
      var controller = wr.abortControllers[id];
      try { _JS_WebRequest_Release(id); }
      catch (error) { if (!firstError) firstError = error; }
      try { if (controller && !controller.signal.aborted) controller.abort(); }
      catch (error) { if (!firstError) firstError = error; }
    });
    if (firstError) throw firstError;
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
