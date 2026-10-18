// Unity's generated OpenURL bridge opens a new tab without opener isolation.
// Keep explicit profile-link actions, but never give that tab control of this one.
(function () {
  "use strict";
  const nativeOpen = window.open.bind(window);
  window.open = function (url, target, features) {
    let destination;
    try { destination = new URL(String(url), location.href); } catch (_) { return null; }
    if (destination.protocol !== "https:" &&
        !(destination.protocol === "http:" && destination.origin === location.origin)) return null;
    const options = String(features || "").split(",").filter(function (feature) {
      return feature.trim() && !/^\s*(noopener|noreferrer)\b/i.test(feature);
    });
    options.push("noopener", "noreferrer");
    return nativeOpen(destination.href, target || "_blank", options.join(","));
  };
})();
