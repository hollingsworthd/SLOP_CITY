mergeInto(LibraryManager.library, {
  SlopCity_GetSceneRenderHeight: function () {
    var setting = window.slopCitySceneRenderHeight;
    var value = Number(typeof setting === "function" ? setting() : setting);
    return value === 0 || value === 540 || value === 720 || value === 1080 ? value : 720;
  },
  SlopCity_GetDisplayPixelRatio: function () {
    var setting = window.slopCityDisplayPixelRatio;
    var value = Number(typeof setting === "function" ? setting() : setting);
    if (!Number.isFinite(value) || value <= 0) value = Number(window.devicePixelRatio) || 1;
    return Math.max(1, value);
  },
  SlopCity_ReportPresentation: function (displayWidth, displayHeight, worldWidth, worldHeight, pixelRatio) {
    var presentation = window.slopCityPresentation;
    if (!presentation) {
      presentation = window.slopCityPresentation = {};
    }
    presentation.displayWidth = displayWidth;
    presentation.displayHeight = displayHeight;
    presentation.worldWidth = worldWidth;
    presentation.worldHeight = worldHeight;
    presentation.pixelRatio = pixelRatio;
    presentation.worldScale = worldHeight / Math.max(1, displayHeight);
  }
});
