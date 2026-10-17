mergeInto(LibraryManager.library, {
  SlopCity_IsMemoryDiagnosticsEnabled: function () {
    return new URLSearchParams(window.location.search).get("qa") === "1" ? 1 : 0;
  },
  SlopCity_IsDeferredRenderingDiagnosticsEnabled: function () {
    var query = new URLSearchParams(window.location.search);
    return query.get("qa") === "1" && query.get("rendering") === "deferred" ? 1 : 0;
  },
  SlopCity_ReportMemory__deps: ['$GL'],
  SlopCity_ReportMemory: function (allocatedBytes, reservedBytes, managedUsedBytes, x, y, z, yaw, pitch, actualRenderingPath) {
    if (new URLSearchParams(window.location.search).get("qa") !== "1") return;
    var parent = document.body || document.documentElement;
    if (!parent) return;
    var output = document.getElementById("slop-city-memory-diagnostics");
    if (!output) {
      output = document.createElement("pre");
      output.id = "slop-city-memory-diagnostics";
      output.hidden = true;
      output.setAttribute("aria-hidden", "true");
      output.style.setProperty("display", "none", "important");
      parent.appendChild(output);
    }
    var heap = Module["HEAPU8"];
    var textureSupport = Module["slopCityMemoryTextureSupport"];
    if (!textureSupport && typeof GLctx !== "undefined" && GLctx) {
      // Inspect the game's existing context once. ETC2 may be core WebGL 2
      // support, so include format enums as well as optional extensions.
      textureSupport = {
        extensions: (GLctx.getSupportedExtensions() || []).filter(function (name) {
          return /compressed.*texture|texture_compression/i.test(name);
        }),
        formats: Array.from(GLctx.getParameter(GLctx.COMPRESSED_TEXTURE_FORMATS) || [])
      };
      Module["slopCityMemoryTextureSupport"] = textureSupport;
    }
    var json = JSON.stringify({ version: 1, checkedAtUtc: new Date().toISOString(),
      wasmHeapBytes: heap && heap.buffer ? heap.buffer.byteLength : null,
      unityAllocatedBytes: allocatedBytes, unityReservedBytes: reservedBytes,
      managedUsedBytes: managedUsedBytes,
      cameraPosition: [x, y, z], cameraYaw: yaw, cameraPitch: pitch,
      actualRenderingPath: actualRenderingPath,
      textureCompressionExtensions: textureSupport ? textureSupport.extensions : null,
      compressedTextureFormatEnums: textureSupport ? textureSupport.formats : null });
    output.textContent = json;
    output.setAttribute("data-snapshot", json);
  },
  SlopCity_ReportFrameRate: function (fps, frameMs, p95Ms, width, height) {
    if (typeof window.slopCityReportPerformance === "function") {
      window.slopCityReportPerformance(fps, frameMs, p95Ms, width, height);
    }
  }
});
