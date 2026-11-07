mergeInto(LibraryManager.library, {
  SlopCityProbeBrowserMediaCaps: function () {
    try {
      var transfer = false;
      try {
        var probe = document.createElement("canvas");
        transfer = typeof probe.transferControlToOffscreen === "function";
      } catch (e) {}
      var caps = {
        webcodecs: typeof VideoDecoder !== "undefined",
        offscreenCanvas: typeof OffscreenCanvas !== "undefined",
        videoFrame: typeof VideoFrame !== "undefined",
        offscreenTransfer: transfer
      };
      if (typeof Module !== "undefined") {
        Module.slopCityBrowserCaps = caps;
      }
      try {
        document.dispatchEvent(new CustomEvent("slop-city-browser-caps", { detail: caps }));
      } catch (e) {}
    } catch (e) {}
  },
  SlopCityBrowserMediaCapsMask: function () {
    var mask = 0;
    try {
      if (typeof VideoDecoder !== "undefined") mask |= 1;
      if (typeof OffscreenCanvas !== "undefined") mask |= 2;
      if (typeof VideoFrame !== "undefined") mask |= 4;
      try {
        var probe = document.createElement("canvas");
        if (typeof probe.transferControlToOffscreen === "function") mask |= 8;
      } catch (e) {}
    } catch (e) {}
    return mask;
  }
});
