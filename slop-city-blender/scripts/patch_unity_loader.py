"""Narrow failure-path repairs for the pinned Unity 6000.3 loader, applied to staging only.

Every anchor is required exactly once. A changed Unity loader must be reviewed
instead of silently shipping an incomplete patch. Package SRI is computed later.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError("Pinned Unity loader contract changed: " + old[:80])
    return source.replace(old, new, 1)


HELPERS = r'''
var slopStartupCleanup = new Set(), slopStartupStopped = false;
window.slopCityStopStartup = function(reason) {
  if (slopStartupStopped) return;
  slopStartupStopped = true;
  var error = reason instanceof Error ? reason : new Error(String(reason || "City startup cancelled."));
  Array.from(slopStartupCleanup).forEach(function(cleanup) {
    try { cleanup(error); } catch (failure) { console.warn("City startup cleanup failed.", failure); }
  });
  slopStartupCleanup.clear();
};
function slopDecompress(bytes, sourceUrl) {
  return new Promise(function(resolve, reject) {
    try {
      if (slopStartupStopped) throw new Error("City startup cancelled.");
      var codec = null;
      for (var name in P) if (P[name].hasUnityMarker(bytes)) { codec = P[name]; break; }
      if (!codec) { resolve(bytes); return; }
      var state = codec.slopWorker;
      if (!state) {
        state = {pending:new Map(), next:0, closed:false, worker:null, url:null};
        codec.slopWorker = state;
        state.close = function(error) {
          if (state.closed) return;
          state.closed = true;
          if (codec.slopWorker === state) codec.slopWorker = null;
          slopStartupCleanup.delete(state.close);
          state.pending.forEach(function(item) { clearTimeout(item.timer); item.reject(error || new Error("City decompression stopped.")); });
          state.pending.clear();
          // Settle every consumer even when browser resource cleanup fails.
          try { if (state.worker) { state.worker.onmessage = state.worker.onerror = state.worker.onmessageerror = null; state.worker.terminate(); } }
          catch (failure) { console.warn("City worker cleanup failed.", failure); }
          try { if (state.url) URL.revokeObjectURL(state.url); }
          catch (failure) { console.warn("City worker URL cleanup failed.", failure); }
          state.worker = state.url = null;
        };
        slopStartupCleanup.add(state.close);
        try {
          state.url = URL.createObjectURL(new Blob([
            "this.require=", codec.require.toString(), ";this.decompress=", codec.decompress.toString(), ";this.onmessage=",
            function(event) {
              try {
                var compressed = event.data.compressed, result;
                if (event.data.gzip) {
                  // Pinned Gzip.js exports pako's Inflate constructor. Its
                  // default onData retains all chunks, then onEnd flattens
                  // them into another whole output. Write directly instead.
                  if (!(compressed instanceof Uint8Array) || compressed.length < 18)
                    throw new Error("Invalid compressed city data.");
                  var end = compressed.length;
                  var size = compressed[end-4] + compressed[end-3] * 256
                    + compressed[end-2] * 65536 + compressed[end-1] * 16777216;
                  // Match the existing packaged gzip output ceiling. ISIZE is
                  // untrusted until the inflater validates the full gzip CRC
                  // and length trailer; never grow this allocation from input.
                  if (!(size > 0 && size <= 1024 * 1024 * 1024))
                    throw new Error("Invalid city gzip output size.");
                  if (!this.exports) this.exports = this.require("inflate.js");
                  if (typeof this.exports.Inflate !== "function")
                    throw new Error("Pinned city gzip inflater is unavailable.");
                  var inflater = new this.exports.Inflate(), offset = 0;
                  result = new Uint8Array(size);
                  inflater.onData = function(chunk) {
                    if (!(chunk instanceof Uint8Array) || chunk.length > size - offset)
                      throw new Error("Unexpected city gzip output size.");
                    result.set(chunk, offset);
                    offset += chunk.length;
                  };
                  // Do not force Z_FINISH: the pinned API can close an
                  // incomplete input on that flag. Its natural stream end
                  // requires the checksum/trailer to have actually arrived.
                  if (!inflater.push(compressed, false) || !inflater.ended
                    || inflater.err !== 0 || inflater.strm.avail_in !== 0 || offset !== size)
                    throw new Error("Incomplete or invalid city gzip data.");
                } else result = this.decompress(compressed);
                if (!result) throw new Error("Invalid compressed city data.");
                postMessage({id:event.data.id, bytes:result}, [result.buffer]);
              } catch (error) { postMessage({id:event.data.id, error:String(error)}); }
            }.toString(), ";"
          ], {type:"application/javascript"}));
          state.worker = new Worker(state.url);
          state.worker.onerror = function(event) { state.close(new Error("City decompression worker failed: " + (event.message || sourceUrl))); };
          state.worker.onmessageerror = function() { state.close(new Error("City decompression returned an unreadable message.")); };
          state.worker.onmessage = function(event) {
            var data = event.data, item = data && state.pending.get(data.id);
            if (!item) { state.close(new Error("City decompression returned an unknown response.")); return; }
            state.pending.delete(data.id); clearTimeout(item.timer);
            if (data.error || !(data.bytes instanceof Uint8Array)) item.reject(new Error(data.error || "Invalid decompressed city bytes."));
            else item.resolve(data.bytes);
            if (state.pending.size === 0) state.close();
          };
        } catch (error) { state.close(error); throw error; }
      }
      var id = state.next++;
      var timer = setTimeout(function() { state.close(new Error("City decompression exceeded its deadline.")); }, 120000);
      state.pending.set(id, {resolve:resolve, reject:reject, timer:timer});
      try { state.worker.postMessage({id:id, compressed:bytes, gzip:codec === P.gzip}, [bytes.buffer]); }
      catch (error) { state.close(error); }
    } catch (error) { reject(error); }
  });
}
'''


FRAMEWORK_PROMISE = r'''return new Promise(function(resolve,reject){
  var script=document.createElement("script"), settled=false;
  var timer=setTimeout(function(){finish(new Error("City framework exceeded its startup deadline."));},120000);
  function finish(error){
    if(settled)return;settled=true;clearTimeout(timer);
    script.onload=script.onerror=null;slopStartupCleanup.delete(finish);
    try{URL.revokeObjectURL(s);}catch(failure){console.warn("City framework URL cleanup failed.",failure);}
    if(error){try{script.remove();}finally{reject(error);}return;}
    var framework=unityFramework;unityFramework=null;resolve(framework);
  }
  slopStartupCleanup.add(finish);
  b.slopCityFrameworkScriptUrl=s;
  script.src=s;
  script.onload=function(){
    if(typeof unityFramework!=="function"){finish(new Error("City framework is unavailable or could not be parsed."));return;}
    finish();
  };
  script.onerror=function(){finish(new Error("City framework could not be loaded: "+b.frameworkUrl));};
  b.deinitializers.push(function(){try{script.remove();}finally{if(!settled)finish(new Error("City startup cancelled."));}});
  try{if(slopStartupStopped)throw new Error("City startup cancelled.");document.body.appendChild(script);}
  catch(error){finish(error);}
})'''


def harden_unity_loader(path):
    source = path.read_text()
    source = replace_once(source, 'function createUnityInstance(t,n,l){',
                          'function createUnityInstance(t,n,l){' + HELPERS)
    # onRuntimeInitialized is too late to own listeners/intervals if framework
    # initialization fails or navigation cancels the still-starting player.
    source = replace_once(source, 'b.QuitCleanup=function(){',
                          'typeof b.slopCityPublishModule==="function"&&b.slopCityPublishModule(b),b.QuitCleanup=function(){')
    start = 'new Promise(function(e,t){try{for(var n in P){'
    end = ';var i,s}).catch(function(e){var t="Failed to download file "'
    if source.count(start) != 1 or source.count(end) != 1:
        raise ValueError("Pinned Unity decompression contract changed")
    first, last = source.index(start), source.index(end)
    if first >= last:
        raise ValueError("Pinned Unity decompression order changed")
    source = source[:first] + 'slopDecompress(i,s)' + source[last:]
    source = replace_once(source, ':console.error(t)})}function T(){',
                          ':console.error(t);throw e})}function T(){')
    start = 'return new Promise(function(a,e){var i=document.createElement("script");'
    end = 'b.deinitializers.push(function(){document.body.removeChild(i)})})'
    if source.count(start) != 1 or source.count(end) != 1:
        raise ValueError("Pinned Unity framework contract changed")
    first, last = source.index(start), source.index(end) + len(end)
    if first >= last:
        raise ValueError("Pinned Unity framework order changed")
    source = source[:first] + FRAMEWORK_PROMISE + source[last:]
    source = replace_once(source, 'e=A("dataUrl");b.preRun.push(',
                          'e=A("dataUrl");e.catch(function(error){x(String(error),b.dataUrl,0)});b.preRun.push(')
    path.write_text(source)
