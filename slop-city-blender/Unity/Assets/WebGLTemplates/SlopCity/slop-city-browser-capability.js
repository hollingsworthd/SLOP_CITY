// The WebGL city is intentionally desktop-only. This check is capability based
// rather than viewport-width based, so a narrow desktop window remains valid
// while phones and touch-only tablets never allocate the game's Wasm/WebGL heap.
(function () {
  "use strict";
  const browser = window.navigator || {};
  const userAgent = String(browser.userAgent || "");
  const knownMobile = (browser.userAgentData && browser.userAgentData.mobile === true)
    || /(?:Android|webOS|iPhone|iPad|iPod|IEMobile|Opera Mini|Mobile)/i.test(userAgent);
  const desktopIpad = browser.platform === "MacIntel" && Number(browser.maxTouchPoints) > 1;
  const matches = function (query) {
    try { return typeof window.matchMedia === "function" && window.matchMedia(query).matches; }
    catch (_) { return false; }
  };
  const capabilityKnown = typeof window.matchMedia === "function";
  const finePointer = matches("(any-pointer: fine)") || matches("(pointer: fine)");
  const hover = matches("(any-hover: hover)") || matches("(hover: hover)");
  window.slopCityDesktopGameBrowser = !(knownMobile || desktopIpad)
    && (!capabilityKnown || (finePointer && hover));
})();
