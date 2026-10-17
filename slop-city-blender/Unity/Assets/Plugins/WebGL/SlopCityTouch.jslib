mergeInto(LibraryManager.library, {
  SlopCityUsesTouch: function () {
    return (window.slopCityTouchControls === true || (navigator.maxTouchPoints > 0 && window.matchMedia('(pointer: coarse)').matches)) ? 1 : 0;
  }
});
