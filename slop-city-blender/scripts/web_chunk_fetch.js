/* Bounded, integrity-checked streaming transport for compiled city assets. */
(function () {
  "use strict";
  const manifest = /* SLOP_CITY_CHUNK_MANIFEST */;
  const deadlineMs = 120000;
  const active = new Set();
  let originalFetch, wrapper;
  let mobileDownloadTail = Promise.resolve();

  function localPath(path) {
    if (typeof path !== "string" || !/^Build\/[A-Za-z0-9_.-]+$/.test(path)) throw new Error("Invalid city asset path");
    const url = new URL(path, document.baseURI);
    if (url.origin !== location.origin) throw new Error("City assets must stay on this site");
    return url.href;
  }
  function integrity(hash) {
    if (typeof hash !== "string" || !/^[a-f0-9]{64}$/.test(hash)) throw new Error("Invalid city asset checksum");
    return "sha256-" + btoa(hash.match(/../g).map(byte => String.fromCharCode(parseInt(byte, 16))).join(""));
  }
  function lookup(resource) {
    const url = new URL(typeof resource === "string" || resource instanceof URL ? resource : resource.url, document.baseURI);
    url.search = ""; url.hash = "";
    return url.href;
  }
  const files = new Map(Object.entries(manifest.integrity).map(function ([path, hash]) {
    const entry = manifest.files[path] || {size: manifest.sizes[path], parts: [{path, size: manifest.sizes[path], sha256: hash}]};
    entry.gzipSize = (manifest.gzipSizes || {})[path];
    if (entry.gzipSize !== undefined && (!Number.isSafeInteger(entry.gzipSize) || entry.gzipSize <= 0 || entry.gzipSize > 1024 * 1024 * 1024))
      throw new Error("Invalid city gzip size: " + path);
    integrity(hash);
    if (!Number.isSafeInteger(entry.size) || entry.size <= 0 || !entry.parts.length
        || entry.parts.some(part => !Number.isSafeInteger(part.size) || part.size <= 0 || part.size > 24 * 1024 * 1024)
        || entry.parts.reduce((sum, part) => sum + part.size, 0) !== entry.size) throw new Error("Invalid city asset manifest: " + path);
    entry.parts.forEach(part => { localPath(part.path); integrity(part.sha256); });
    return [localPath(path), entry];
  }));

  function discardResponse(response) {
    if (!response || !response.body) return;
    try { Promise.resolve(response.body.cancel()).catch(() => {}); } catch (_) { /* Already locked or closed. */ }
  }
  function deadline(promise, controller, discardLateValue) {
    return new Promise(function (resolve, reject) {
      let settled = false, timer;
      function finish(error, value) {
        if (settled) { if (!error && discardLateValue) discardLateValue(value); return; }
        settled = true;
        clearTimeout(timer);
        controller.signal.removeEventListener("abort", aborted);
        if (error) reject(error); else resolve(value);
      }
      const aborted = () => finish(controller.signal.reason || new Error("City download cancelled."));
      controller.signal.addEventListener("abort", aborted, {once: true});
      timer = setTimeout(function () {
        const error = new Error("The city download stopped responding.");
        finish(error); controller.abort(error);
      }, deadlineMs);
      // Observe the original promise even after cancellation so its eventual
      // rejection or response body cannot escape the cancelled request.
      Promise.resolve(promise).then(value => finish(null, value), error => finish(error));
      if (controller.signal.aborted) aborted();
    });
  }

  function install(onError) {
    if (wrapper) return;
    originalFetch = window.fetch;
    const fetchPart = originalFetch.bind(window);
    wrapper = function (resource, init) {
      let entry;
      try { entry = files.get(lookup(resource)); } catch (_) { /* Leave unrelated requests alone. */ }
      const method = (init && init.method) || (resource && resource.method) || "GET";
      if (!entry || method.toUpperCase() !== "GET") return fetchPart(resource, init);
      const owner = new AbortController();
      // Unity starts its framework, code and data requests together. On touch
      // devices, finish one decoded response before admitting the next large
      // download so native inflation buffers cannot overlap each other.
      let releaseSlot = function () {};
      const precedingDownload = window.slopCityTouchControls ? mobileDownloadTail : Promise.resolve();
      if (window.slopCityTouchControls) {
        const completedDownload = new Promise(resolve => { releaseSlot = resolve; });
        // Cancelling a queued request must not admit its successor while the
        // preceding response is still being consumed.
        mobileDownloadTail = Promise.all([precedingDownload, completedDownload]).then(() => {});
      }
      let decoder = null;
      if (entry.gzipSize && typeof DecompressionStream === "function") {
        try { decoder = new DecompressionStream("gzip"); }
        catch (_) { /* Keep Unity's existing inflater on unsupported browsers. */ }
      }
      const signal = (init && init.signal) || (resource && resource.signal);
      const abort = () => owner.abort(signal && signal.reason);
      active.add(owner);
      if (signal) {
        if (signal.aborted) abort(); else signal.addEventListener("abort", abort, {once: true});
      }
      let index = 0, bytesInPart = 0, reader = null, transfer = null, releaseTransfer = null;
      let ended = false, streamController;
      const ownerAborted = () => finish(owner.signal.reason || new Error("City download cancelled."), false);
      function releaseReader(old) {
        try { Promise.resolve(old.cancel()).catch(() => {}).then(() => { try { old.releaseLock(); } catch (_) {} }); }
        catch (_) { /* A closed reader still needs its lock released. */ }
        try { old.releaseLock(); } catch (_) { /* Retry when cancellation settles. */ }
      }
      function cleanup() {
        active.delete(owner);
        owner.signal.removeEventListener("abort", ownerAborted);
        if (signal) signal.removeEventListener("abort", abort);
        if (releaseTransfer) { releaseTransfer(); releaseTransfer = null; }
        if (reader) { const old = reader; reader = null; releaseReader(old); }
      }
      function finish(error, report) {
        if (ended) return;
        ended = true;
        owner.abort(error);
        cleanup();
        if (streamController) streamController.error(error);
        if (report && onError) onError(error);
      }
      async function openPart(part) {
        await new Promise(function (resolve, reject) {
          const cancelled = () => { owner.signal.removeEventListener("abort", cancelled); reject(owner.signal.reason || new Error("City download cancelled.")); };
          owner.signal.addEventListener("abort", cancelled, {once: true});
          precedingDownload.then(function () { owner.signal.removeEventListener("abort", cancelled); resolve(); });
          if (owner.signal.aborted) cancelled();
        });
        let lastError;
        // Retry only before exposing bytes; retrying a partially emitted stream
        // would duplicate content and corrupt the compiled package.
        for (let attempt = 0; attempt < 2; attempt++) {
          if (ended || owner.signal.aborted) throw new Error("City download cancelled.");
          transfer = new AbortController();
          const current = transfer;
          const cancel = () => current.abort(owner.signal.reason);
          const detach = () => owner.signal.removeEventListener("abort", cancel);
          owner.signal.addEventListener("abort", cancel, {once: true});
          releaseTransfer = detach;
          try {
            const response = await deadline(fetchPart(localPath(part.path), {
              method: "GET", signal: current.signal, integrity: integrity(part.sha256),
              mode: "same-origin", redirect: "error", cache: attempt ? "reload" : "default",
              credentials: (init && init.credentials) || (resource && resource.credentials) || "same-origin"
            }), current, discardResponse);
            if (ended || owner.signal.aborted) { discardResponse(response); throw new Error("City download cancelled."); }
            if (!response.ok) { discardResponse(response); throw new Error("City download failed (HTTP " + response.status + "): " + part.path); }
            return response;
          } catch (error) {
            lastError = error; current.abort(); detach();
            if (releaseTransfer === detach) releaseTransfer = null;
            if (ended || owner.signal.aborted) throw error;
          }
        }
        throw lastError;
      }
      const body = new ReadableStream({
        start(stream) {
          streamController = stream;
          owner.signal.addEventListener("abort", ownerAborted, {once: true});
          if (owner.signal.aborted) ownerAborted();
        },
        async pull(stream) {
          if (ended) return;
          try {
            while (index < entry.parts.length) {
              const part = entry.parts[index];
              if (!reader) {
                const response = await openPart(part);
                if (ended) { discardResponse(response); return; }
                bytesInPart = 0;
                if (!response.body) {
                  const bytes = new Uint8Array(await deadline(response.arrayBuffer(), transfer));
                  if (ended) return;
                  if (bytes.byteLength !== part.size) throw new Error("Incomplete city download: " + part.path);
                  releaseTransfer(); releaseTransfer = null;
                  index++; stream.enqueue(bytes); return;
                }
                reader = response.body.getReader();
              }
              const currentReader = reader;
              const result = await deadline(currentReader.read(), transfer);
              if (ended) return;
              if (result.done) {
                currentReader.releaseLock(); reader = null;
                releaseTransfer(); releaseTransfer = null;
                if (bytesInPart !== part.size) throw new Error("Incomplete city download: " + part.path);
                index++; continue;
              }
              bytesInPart += result.value.byteLength;
              if (bytesInPart > part.size) throw new Error("Unexpected city download size: " + part.path);
              stream.enqueue(result.value); return;
            }
            ended = true; stream.close();
          } catch (error) { finish(error, true); }
        },
        cancel(reason) { ended = true; owner.abort(reason); cleanup(); }
      });
      // Supply the exact decoded length to Unity's single download buffer. Its
      // gzip-marker check now sees the decoded bytes, so it does not allocate a
      // compressed whole-file buffer or use the chunk-and-flatten pako worker.
      const outputSize = decoder ? entry.gzipSize : entry.size;
      const outputReader = (decoder ? body.pipeThrough(decoder) : body).getReader();
      let outputEnded = false, outputBytes = 0, outputController;
      function finishOutput(error, cancellation) {
        if (outputEnded) return;
        outputEnded = true; ended = true;
        owner.signal.removeEventListener("abort", abortOutput);
        // Abort the current Fetch before cleanup detaches its owner listener.
        if (error || cancellation) owner.abort(error || cancellation);
        cleanup(); releaseSlot();
        if (error) {
          releaseReader(outputReader);
          if (outputController) outputController.error(error);
        }
      }
      const abortOutput = () => finishOutput(owner.signal.reason || new Error("City download cancelled."));
      const output = new ReadableStream({
        start(stream) {
          outputController = stream;
          owner.signal.addEventListener("abort", abortOutput, {once: true});
          if (owner.signal.aborted) abortOutput();
        },
        async pull(stream) {
          if (outputEnded) return;
          try {
            // Queued responses may wait behind a slow, healthy download. The
            // underlying fetch/read operations already enforce their deadlines;
            // do not count time waiting for the preceding download as a stall.
            const result = await outputReader.read();
            if (outputEnded) return;
            if (result.done) {
              if (outputBytes !== outputSize) throw new Error("Incomplete decoded city download.");
              outputReader.releaseLock(); finishOutput(); stream.close(); return;
            }
            outputBytes += result.value.byteLength;
            if (outputBytes > outputSize) throw new Error("Unexpected decoded city download size.");
            stream.enqueue(result.value);
          } catch (error) {
            if (outputEnded) return;
            finishOutput(error); owner.abort(error);
            if (onError) onError(error);
          }
        },
        cancel(reason) {
          finishOutput(null, reason || new Error("City download cancelled."));
          releaseReader(outputReader);
        }
      });
      // Unity allocates Content-Length bytes as soon as Fetch resolves. Holding
      // only the transfer is insufficient: queued downloads would still reserve
      // every decoded whole-file buffer together before receiving their bytes.
      // Admit the Response with its transfer slot, without timing healthy waits.
      return new Promise(function (resolve, reject) {
        const cancelled = () => {
          owner.signal.removeEventListener("abort", cancelled);
          reject(owner.signal.reason || new Error("City download cancelled."));
        };
        owner.signal.addEventListener("abort", cancelled, {once: true});
        precedingDownload.then(function () {
          owner.signal.removeEventListener("abort", cancelled);
          if (owner.signal.aborted) { cancelled(); return; }
          resolve(new Response(output, {
            status: 200, headers: {"Content-Type": "application/octet-stream", "Content-Length": String(outputSize)}
          }));
        });
        if (owner.signal.aborted) cancelled();
      });
    };
    window.fetch = wrapper;
  }
  function restore() {
    if (wrapper && window.fetch === wrapper) window.fetch = originalFetch;
    wrapper = null;
  }
  window.slopCityChunks = {
    install, restore,
    abortAll: function (reason) { for (const request of active) request.abort(reason); restore(); },
    cacheControl: function (url) {
      // HTTP caching/SRI still apply to parts; avoid Unity's whole-response tee.
      return files.has(lookup(url)) ? "no-store" : /\.bundle/.test(url) ? "must-revalidate" : "no-store";
    }
  };
})();
