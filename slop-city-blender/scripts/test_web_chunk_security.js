/* Focused transport checks using Node's real Fetch integrity verifier on localhost. */
"use strict";
const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(path.join(__dirname, "web_chunk_fetch.js"), "utf8");
const hash = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const sri = bytes => "sha256-" + crypto.createHash("sha256").update(bytes).digest("base64");
const first = Buffer.from("small city ");
const second = Buffer.from("fixture");
const framework = Buffer.from("window.fixtureFramework = true;");
const code = Buffer.from("tiny wasm fixture");
const baseManifest = () => ({
  version: 1,
  files: {"Build/city.data.unityweb": {
    size: first.length + second.length, sha256: hash(Buffer.concat([first, second])),
    parts: [
      {path: "Build/city.part0000", size: first.length, sha256: hash(first)},
      {path: "Build/city.part0001", size: second.length, sha256: hash(second)}
    ]
  }},
  integrity: {"Build/city.framework.js.unityweb": hash(framework), "Build/city.wasm.unityweb": hash(code)}
});

let tamper = false;
let frameworkBody = framework;
let cacheFramework = false;
const frameworkCache = new Map();
const server = http.createServer((request, response) => {
  const content = {
    "/Build/city.part0000": tamper ? Buffer.from("wrong city ") : first,
    "/Build/city.part0001": second,
    "/Build/city.framework.js.unityweb": frameworkBody,
    "/Build/city.wasm.unityweb": code
  }[new URL(request.url, "http://localhost").pathname];
  let served = content;
  if (cacheFramework && new URL(request.url, "http://localhost").pathname.endsWith("city.framework.js.unityweb")) {
    if (!frameworkCache.has(request.url)) frameworkCache.set(request.url, content);
    served = frameworkCache.get(request.url);
  }
  if (!content) { response.writeHead(404); response.end(); return; }
  response.writeHead(200, {"Content-Type": "application/octet-stream", "Content-Length": served.length});
  response.end(served);
});

function load(origin, manifest = baseManifest(), base = origin + "/index.html") {
  const calls = [];
  const nativeFetch = function (resource, init) { calls.push({resource, init}); return fetch(resource, init); };
  const window = {fetch: nativeFetch};
  const context = {window, document: {baseURI: base}, location: {origin}, URL,
    Response, ReadableStream, AbortController, btoa};
  vm.runInNewContext(template.replace("/* SLOP_CITY_CHUNK_MANIFEST */", JSON.stringify(manifest)), context);
  return {window, calls, nativeFetch, api: window.slopCityChunks};
}

async function run(origin) {
  const normal = load(origin);
  const errors = [];
  normal.api.install(error => errors.push(error));
  const response = await normal.window.fetch(origin + "/Build/city.data.unityweb?sha256=" + hash(Buffer.concat([first, second])));
  assert.equal(response.headers.get("Content-Length"), String(first.length + second.length));
  assert.deepEqual(Buffer.from(await response.arrayBuffer()), Buffer.concat([first, second]));
  assert.equal(errors.length, 0);
  assert.equal(normal.calls.length, 2);
  [first, second].forEach((data, i) => {
    assert.equal(normal.calls[i].init.integrity, sri(data));
    assert.equal(normal.calls[i].init.redirect, "error");
    assert.equal(normal.calls[i].init.mode, "same-origin");
  });
  for (const [filename, data] of [["city.framework.js.unityweb", framework], ["city.wasm.unityweb", code]]) {
    const requested = origin + "/Build/" + filename + "?sha256=" + hash(data);
    const result = await normal.window.fetch(requested);
    assert.deepEqual(Buffer.from(await result.arrayBuffer()), data);
    assert.equal(normal.calls.at(-1).init.integrity, sri(data));
    assert.equal(normal.calls.at(-1).init.redirect, "error");
    assert.equal(normal.calls.at(-1).resource, requested, "Manifest normalization must retain the request query");
  }
  assert.equal(normal.api.cacheControl(origin + "/Build/city.data.unityweb", origin + "/Build/city.data.unityweb"), "no-store");
  normal.api.restore();
  assert.equal(normal.window.fetch, normal.nativeFetch);

  const corrupted = load(origin);
  const corruptedErrors = [];
  corrupted.api.install(error => corruptedErrors.push(error));
  tamper = true;
  try {
    const badResponse = await corrupted.window.fetch(origin + "/Build/city.data.unityweb");
    await assert.rejects(badResponse.arrayBuffer(), /fetch failed|integrity/i);
    assert.equal(corruptedErrors.length, 1);
    assert.equal(corrupted.calls.length, 1, "tampered first part must stop remaining requests");
  } finally { tamper = false; corrupted.api.restore(); }

  const aborted = load(origin);
  const abortErrors = [];
  aborted.api.install(error => abortErrors.push(error));
  const controller = new AbortController();
  controller.abort();
  const abortedResponse = await aborted.window.fetch(origin + "/Build/city.data.unityweb", {signal: controller.signal});
  await assert.rejects(abortedResponse.arrayBuffer(), /abort/i);
  assert.equal(abortErrors.length, 1);
  aborted.api.restore();

  const wrongFramework = baseManifest();
  wrongFramework.integrity["Build/city.framework.js.unityweb"] = hash(Buffer.from("different framework"));
  const invalid = load(origin, wrongFramework);
  invalid.api.install();
  await assert.rejects(invalid.window.fetch(origin + "/Build/city.framework.js.unityweb"), /fetch failed|integrity/i);
  invalid.api.restore();

  // Model a warmed browser cache by retaining the first body for each complete URL.
  cacheFramework = true;
  const oldURL = origin + "/Build/city.framework.js.unityweb?sha256=" + hash(framework);
  const before = load(origin); before.api.install();
  assert.deepEqual(Buffer.from(await (await before.window.fetch(oldURL)).arrayBuffer()), framework);
  before.api.restore();
  frameworkBody = Buffer.from("window.fixtureFramework = 'new release';");
  const nextManifest = baseManifest();
  nextManifest.integrity["Build/city.framework.js.unityweb"] = hash(frameworkBody);
  const after = load(origin, nextManifest); after.api.install();
  await assert.rejects(after.window.fetch(oldURL), /fetch failed|integrity/i, "Stale cache bytes must still fail integrity");
  const newURL = origin + "/Build/city.framework.js.unityweb?sha256=" + hash(frameworkBody);
  assert.deepEqual(Buffer.from(await (await after.window.fetch(newURL)).arrayBuffer()), frameworkBody);
  assert.notEqual(newURL, oldURL);
  assert.equal(after.calls.at(-1).resource, newURL);
  after.api.restore(); cacheFramework = false; frameworkBody = framework;

  for (const badPath of ["https://evil.invalid/a.js", "//evil.invalid/a.js", "Build/../outside.js", "Build/%2e%2e.js", "Build/a.js?x=1", "Build/a.js#fragment"]) {
    const manifest = baseManifest();
    manifest.files["Build/city.data.unityweb"].parts[0].path = badPath;
    assert.throws(() => load(origin, manifest), /Invalid city asset path/, badPath);
  }
  assert.throws(() => load(origin, baseManifest(), "https://evil.invalid/index.html"), /stay on this site/);
  console.log("Chunk security checks passed: real SRI mismatch rejection, framework/code and part integrity, bytes, abort, restore, and origin/path restrictions.");
}

server.listen(0, "127.0.0.1", async () => {
  try { await run("http://127.0.0.1:" + server.address().port); }
  catch (error) { console.error(error); process.exitCode = 1; }
  finally { server.close(); server.closeAllConnections(); }
});
