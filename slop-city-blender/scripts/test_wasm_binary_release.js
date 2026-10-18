#!/usr/bin/env node
'use strict';
// Execute the actual patched receiveInstance, using a real tiny Wasm module.
// Prove failed compilation retains retry bytes and success frees both owners.
// Dynamic imports also work when this public repository is nested under an
// ES-module application, without changing the standalone checkout's settings.
(async () => {
const assert = (await import('node:assert/strict')).default;
const fs = (await import('node:fs')).default;
const path = (await import('node:path')).default;
const vm = (await import('node:vm')).default;
const zlib = (await import('node:zlib')).default;
const scriptDirectory = path.dirname(path.resolve(process.argv[1]));
assert.equal(process.argv[2], '--framework');
const file = fs.readFileSync(process.argv[3]);
const framework = (file[0] === 0x1f ? zlib.gunzipSync(file) : file).toString();
const start = framework.indexOf('function receiveInstance(instance,module){');
assert.ok(start >= 0);
const end = framework.indexOf('}addRunDependency("wasm-instantiate")', start);
assert.ok(end > start);
const receiver = framework.slice(start, end + 1);
const binaryStart = framework.indexOf('function getBinary(file){');
const binaryEnd = framework.indexOf('function getBinaryPromise(', binaryStart);
assert.ok(binaryStart >= 0 && binaryEnd > binaryStart, 'Actual binary reader contract must be present');
const binaryReader = framework.slice(binaryStart, binaryEnd);
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
function environment() {
  const binary = Uint8Array.from(Buffer.from('0061736d010000000503010001070a01066d656d6f72790200', 'hex'));
  const Module = { wasmBinary: binary };
  const context = { Module, updateMemoryViews() { assert.equal(Module.asm.memory.buffer.byteLength, 65536); },
    addOnInit() {}, removeRunDependency(dependency) { assert.equal(dependency, 'wasm-instantiate'); } };
  vm.createContext(context);
  vm.runInContext('var wasmBinary=Module["wasmBinary"],wasmMemory,wasmTable;'
    + receiver + ';getRetained=function(){return wasmBinary;};', context);
  return { context, Module, binary };
}
async function run() {
  // Execute the compiled reader, including its fallback, rather than merely
  // matching the patch text. A loader view may cover only part of a buffer.
  const storage = Uint8Array.from({ length: 24 }, (_, index) => index);
  const view = storage.subarray(7, 19);
  let fallbackFile;
  const readerContext = { Uint8Array, wasmBinary: view, wasmBinaryFile: 'build.wasm',
    readBinary(file) { fallbackFile = file; return Uint8Array.of(99); },
    abort(error) { throw error; } };
  vm.createContext(readerContext);
  vm.runInContext(binaryReader, readerContext);
  const borrowed = readerContext.getBinary('build.wasm');
  check(borrowed === view, 'Existing loader Uint8Array is borrowed without a Wasm-sized copy');
  check(borrowed.byteOffset === 7 && borrowed.byteLength === 12 && borrowed[0] === 7,
    'Borrowed subarray preserves its exact offset and length');
  readerContext.wasmBinary = storage.buffer;
  const normalized = readerContext.getBinary('build.wasm');
  check(normalized instanceof Uint8Array && normalized.buffer === storage.buffer
    && normalized.byteLength === storage.byteLength, 'ArrayBuffer input is normalized without copying its storage');
  check(readerContext.getBinary('another.wasm')[0] === 99 && fallbackFile === 'another.wasm',
    'Unrelated file reads retain the compiled fallback');
  readerContext.wasmBinary = undefined;
  check(readerContext.getBinary('build.wasm')[0] === 99 && fallbackFile === 'build.wasm',
    'Missing resident binary retains the compiled fallback');
  const successful = environment();
  const result = await WebAssembly.instantiate(successful.binary);
  successful.context.receiveInstance(result.instance, result.module);
  check(successful.context.getRetained() === undefined && !('wasmBinary' in successful.Module), 'Successful instantiation releases closure and loader Module bytes');
  check(successful.Module.asm.memory.buffer.byteLength === 65536, 'Instantiated memory/exports remain functional after raw bytes are released');
  const retry = environment();
  await assert.rejects(WebAssembly.instantiate(new Uint8Array([0])), WebAssembly.CompileError); checks++;
  check(retry.context.getRetained() === retry.binary && retry.Module.wasmBinary === retry.binary, 'Compilation failure retains raw bytes for fallback/retry');
  const recovered = await WebAssembly.instantiate(retry.context.getRetained());
  retry.context.receiveInstance(recovered.instance, recovered.module);
  check(!retry.context.getRetained() && !retry.Module.wasmBinary && retry.Module.asm.memory, 'Retained retry bytes instantiate successfully and release only after recovery');
  const launchFlag = process.argv.indexOf('--launcher');
  const templatePath = launchFlag >= 0 ? process.argv[launchFlag + 1]
    : path.join(scriptDirectory, '../Unity/Assets/WebGLTemplates/SlopCity/index.html');
  assert.ok(templatePath, '--launcher requires an actual launcher path');
  const template = fs.readFileSync(templatePath, 'utf8');
  const marker = template.indexOf('errorHandler: function');
  const finish = template.indexOf('\n      },', marker);
  let failures = 0;
  const handler = vm.runInNewContext('(' + template.slice(marker + 'errorHandler: '.length, finish + '\n      }'.length) + ')',
    { fail() { failures++; } });
  for (const filename of ['Build/WebGL-v0.12.0.framework.js.unityweb', 'Build/WebGL-v0.12.1.framework.js.unityweb',
                          'Build/WebGL-0.11.8.framework.js.unityweb', 'Build/WebGL.framework.js.unityweb',
                          'Build/WebGL-reference-recovery.framework.js.unityweb']) {
    check(handler('TypeError: fixture engine failure', filename), 'Unity framework filename is classified: ' + filename);
  }
  check(handler('RuntimeError: unreachable at wasm-function[1]', ''), 'Wasm stack error is classified');
  const prior = failures;
  for (const filename of ['analytics.js', 'Build/WebGL-reference-recovery.helper.js', 'Build/Other.framework.js.unityweb']) {
    check(handler('TypeError: unrelated page failure', filename) === false && failures === prior,
      'Unrelated page errors are left to normal reporting: ' + filename);
  }
  console.log(JSON.stringify({ passed: true, checks, scope: 'actual compiled zero-copy reader, success callback with real Wasm instantiate, failure/retry and launcher error classification' }));
}
await run();
})().catch(error => { console.error(error); process.exitCode = 1; });
