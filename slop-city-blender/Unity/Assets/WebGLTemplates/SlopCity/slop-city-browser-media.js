// Help with host restrictions without ever opening a separate video player.
(function () {
  "use strict";
  const panel = document.getElementById("media-support");
  const title = document.getElementById("media-support-title");
  const message = document.getElementById("media-support-message");
  const toggle = document.getElementById("media-help");
  const copyStatus = document.getElementById("media-copy-status");
  let blocked = typeof window.slopCityCanPlayInlineVideo === "function" && !window.slopCityCanPlayInlineVideo();
  let pending = false;
  let inGame = false;

  function show(visible) {
    panel.hidden = !visible;
    toggle.setAttribute("aria-expanded", String(visible));
  }
  function update() {
    if (blocked) {
      title.textContent = "Open in Safari or Chrome for broadcasts";
      message.textContent = "This browser cannot keep videos on the city’s screens. You can still explore here, or open the game in Safari or Chrome to watch and listen.";
      toggle.textContent = "Video blocked · Help";
    } else if (pending) {
      title.textContent = "Tap to enable video and sound";
      message.textContent = "This browser is waiting for permission. Tap the city to enable video and sound. If it stays silent, open the game in Safari or Chrome.";
      toggle.textContent = "Enable media";
    } else {
      title.textContent = "No video or sound?";
      message.textContent = "Tap the city once to allow video and sound. Check the volume and playback controls in MEDIA.";
      toggle.textContent = "Media help";
    }
  }
  update();
  if (blocked) show(true);

  // This button's real input events also reach the playback bridge's capture
  // listeners. Do not defer play through a promise or manufacture a gesture.
  toggle.addEventListener("click", function () { show(panel.hidden); });
  document.getElementById("media-dismiss").addEventListener("click", function () {
    show(false);
    if (inGame) document.getElementById("unity-canvas").focus();
    else toggle.focus();
  });
  document.addEventListener("slopcityready", function () {
    inGame = true;
    document.getElementById("stage").appendChild(panel);
    panel.classList.add("floating");
    show(false);
  });
  document.addEventListener("slopcityvideoblocked", function () {
    const first = !blocked;
    blocked = true;
    update();
    // Explain an unexpected presentation restriction once, not every clip.
    if (first) show(true);
  });
  document.addEventListener("slopcityvideoplayback", function (event) {
    const wasPending = pending;
    pending = event.detail.pendingCount > 0;
    update();
    if (wasPending && !pending && !blocked) show(false);
  });
  document.getElementById("media-copy").addEventListener("click", async function () {
    try {
      if (!navigator.clipboard || !navigator.clipboard.writeText) throw new Error("Clipboard unavailable");
      await navigator.clipboard.writeText("https://slopcity.org/");
      copyStatus.textContent = "Copied. Paste into Safari or Chrome.";
    } catch (_) {
      copyStatus.textContent = "Type SLOPCITY.org into Safari or Chrome.";
    }
  });
})();
