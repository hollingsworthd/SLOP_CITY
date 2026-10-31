mergeInto(LibraryManager.library, {
  SlopCityUsesTouch: function () {
    var ua = navigator.userAgent || '';
    var iosUa = /iPhone|iPad|iPod/i.test(ua);
    var ipadDesktop = navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1;
    var touchControls = window.slopCityTouchControls === true;
    var coarsePointer = navigator.maxTouchPoints > 0 && window.matchMedia('(pointer: coarse)').matches;
    return (touchControls || coarsePointer || iosUa || ipadDesktop) ? 1 : 0;
  }
});
