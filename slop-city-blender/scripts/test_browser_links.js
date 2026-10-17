/* Verify the real browser OpenURL wrapper with an isolated native-open stub. */
"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname,
  "../Unity/Assets/WebGLTemplates/SlopCity/slop-city-browser-links.js"), "utf8");

function fixture(href = "https://slopcity.org/?v=test") {
  const calls = [];
  const origin = new URL(href).origin;
  const location = {href, origin};
  const opened = {testOnlyNativeReturn: true};
  const window = {open: function () { calls.push({thisValue: this, args: [...arguments]}); return opened; }};
  vm.runInNewContext(source, {window, location, URL});
  return {window, calls, location, href, opened};
}

const game = fixture();
assert.equal(game.window.open("https://www.tiktok.com/@1984drh", "_blank", ""), game.opened);
assert.deepEqual(game.calls[0].args, ["https://www.tiktok.com/@1984drh", "_blank", "noopener,noreferrer"]);
assert.equal(game.calls[0].thisValue, game.window);
assert.equal(game.location.href, game.href);

game.window.open("https://www.tiktok.com/@machine_publishers", undefined,
  "width=480, NoOpEnEr = 0,noreferrer=false,noopener=no, Noreferrer=0,height=640");
assert.deepEqual(game.calls[1].args, ["https://www.tiktok.com/@machine_publishers", "_blank",
  "width=480,height=640,noopener,noreferrer"]);
assert.equal(game.location.href, game.href);

for (const bad of ["javascript:alert(1)", " JaVaScRiPt:alert(1)", "data:text/html,evil",
                   "file:///tmp/evil.html", "http://evil.invalid/", "https://[", "about:blank"]) {
  const count = game.calls.length;
  assert.equal(game.window.open(bad, "_blank", "noopener=0"), null, bad);
  assert.equal(game.calls.length, count, "invalid navigation reached native open: " + bad);
  assert.equal(game.location.href, game.href);
}

game.window.open("StreamingAssets/Media/creator-feeds.html");
assert.deepEqual(game.calls.at(-1).args, ["https://slopcity.org/StreamingAssets/Media/creator-feeds.html",
  "_blank", "noopener,noreferrer"]);

const preview = fixture("http://127.0.0.1:19000/index.html");
preview.window.open("StreamingAssets/Media/creator-feeds.html");
assert.deepEqual(preview.calls[0].args, ["http://127.0.0.1:19000/StreamingAssets/Media/creator-feeds.html",
  "_blank", "noopener,noreferrer"]);
assert.equal(preview.window.open("http://localhost:19000/elsewhere"), null);
assert.equal(preview.location.href, preview.href);
console.log("Browser link checks passed: supported creator/portal links isolate opener, options cannot disable isolation, unsafe destinations rejected, and game location preserved.");
