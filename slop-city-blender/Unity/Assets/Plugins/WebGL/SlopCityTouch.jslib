mergeInto(LibraryManager.library, {
  SlopCityUsesTouch: function () {
    var capabilities = window.slopCityInputCapabilities;
    if (capabilities && typeof capabilities.touch === 'boolean') return capabilities.touch ? 1 : 0;
    // The template normally creates the decision before Unity starts. Keep a
    // guarded equivalent for exported/custom hosts that omit that template.
    var touchPoints = Number(navigator.maxTouchPoints) || 0;
    var coarse = false;
    try { coarse = typeof window.matchMedia === 'function' && window.matchMedia('(any-pointer: coarse)').matches; } catch (_) {}
    return (window.slopCityTouchControls === true || touchPoints > 0 || coarse) ? 1 : 0;
  },
  SlopCityFinePointerActive: function () {
    // A real mouse/pen event is authoritative even when matchMedia is absent
    // or inaccurate for hybrid hardware.
    return window.slopCityFinePointerActive === true ? 1 : 0;
  },
  SlopCityMediaHelpModalOpen: function () {
    return window.slopCityMediaHelpModalOpen === true ? 1 : 0;
  }
});
