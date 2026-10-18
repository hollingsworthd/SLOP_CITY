#!/usr/bin/env node
// Verify QA opt-in and bounded snapshots. --framework tests rebuilt helpers.
// This does not measure physical browser/GPU memory or compile C#.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const zlib = require('node:zlib');
const project = path.resolve(__dirname, '../Unity');
const bridge = fs.readFileSync(path.join(project, 'Assets/Plugins/WebGL/SlopCityPerformance.jslib'), 'utf8');
const monitor = fs.readFileSync(path.join(project, 'Assets/Scripts/SlopCityWebPerformance.cs'), 'utf8');
const finish = fs.readFileSync(path.join(project, 'Assets/Scripts/SlopCityCameraFinish.cs'), 'utf8');
let framework;
if (process.argv.length > 2) {
  assert.equal(process.argv[2], '--framework');
  assert.ok(process.argv[3], '--framework needs a path');
  const bytes = fs.readFileSync(process.argv[3]);
  framework = (bytes[0] === 0x1f && bytes[1] === 0x8b ? zlib.gunzipSync(bytes) : bytes).toString('utf8');
}
function helper(source, name) {
  const start = source.indexOf('function ' + name + '(');
  assert.notEqual(start, -1, 'Missing rebuilt helper ' + name);
  const open = source.indexOf('{', start);
  let depth = 0, quote = '', comment = '';
  for (let i = open; i < source.length; i++) {
    const c = source[i], next = source[i + 1];
    if (comment === 'line') { if (c === '\n') comment = ''; continue; }
    if (comment === 'block') { if (c === '*' && next === '/') { comment = ''; i++; } continue; }
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = ''; continue; }
    if (c === '/' && next === '/') { comment = 'line'; i++; continue; }
    if (c === '/' && next === '*') { comment = 'block'; i++; continue; }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '{') depth++;
    else if (c === '}' && --depth === 0) return source.slice(start, i + 1);
  }
  throw new Error('Unclosed rebuilt helper ' + name);
}
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
const nodes = new Map(), fpsCalls = [];
let created = 0, supportQueries = 0;
const parent = { appendChild(node) { nodes.set(node.id, node); } };
const window = { location: { search: '' }, slopCityReportPerformance(...args) { fpsCalls.push(args); } };
const context = vm.createContext({ window, URLSearchParams, Date, JSON,
  Module: { HEAPU8: { buffer: { byteLength: 536870912 } } },
  GLctx: {
    COMPRESSED_TEXTURE_FORMATS: 34467,
    getSupportedExtensions() {
      supportQueries++;
      return ['WEBGL_compressed_texture_etc', 'EXT_texture_compression_bptc',
        'OES_compressed_ETC1_RGB8_texture', 'EXT_color_buffer_float'];
    },
    getParameter(parameter) {
      assert.equal(parameter, this.COMPRESSED_TEXTURE_FORMATS);
      supportQueries++;
      return new Uint32Array([0x9274, 0x9278]);
    }
  },
  document: {
    body: parent,
    getElementById(id) { return nodes.get(id); },
    createElement(tag) {
      assert.equal(tag, 'pre'); created++;
      return { style: { setProperty() {} }, setAttribute(key, value) { this[key] = value; } };
    }
  },
  LibraryManager: { library: {} }, mergeInto(target, fields) { Object.assign(target, fields); }
});
vm.runInContext(bridge, context);
const library = context.LibraryManager.library;
if (framework) {
  for (const name of ['SlopCity_IsMemoryDiagnosticsEnabled', 'SlopCity_IsDeferredRenderingDiagnosticsEnabled', 'SlopCity_ReportMemory', 'SlopCity_ReportFrameRate'])
    vm.runInContext('LibraryManager.library.' + name + ' = ' + helper(framework, '_' + name) + ';', context);
}

// Check the C# opt-in boundary alongside the executable JS boundary.
const guardedProfiler = monitor.match(/if\s*\(memoryDiagnostics\)\s*\{([\s\S]*?)\}/);
check(guardedProfiler && (guardedProfiler[0].match(/Profiler\./g) || []).length === 3
  && (monitor.match(/Profiler\./g) || []).length === 3, 'All memory-profiler calls sit inside the opt-in branch');
check(/memoryDiagnostics\s*=\s*SlopCity_IsMemoryDiagnosticsEnabled\(\)\s*!=\s*0/.test(monitor),
  'C# derives opt-in from the JS query');
check(guardedProfiler && guardedProfiler[1].includes('Camera.main') && (monitor.match(/Camera\.main/g) || []).length === 1,
  'Camera sampling stays in the same QA branch as memory profiling');
check(library.SlopCity_ReportMemory.length === 9,
  'Memory and camera bridge has nine scalar arguments including actual rendering path');
check(/float x, float y, float z, float yaw, float pitch, int actualRenderingPath/.test(monitor)
  && /camera \? \(int\)camera\.actualRenderingPath : -1/.test(guardedProfiler[1]),
  'C# passes actual hardware rendering path as the ninth argument inside QA sampling');
for (const query of ['', '?rendering=deferred', '?qa=0&rendering=deferred', '?qa=01&rendering=deferred',
  '?qa=true&rendering=deferred', '?qa=1', '?qa=1&rendering=forward', '?qa=1&rendering=Deferred', '?qa=1&rendering=other']) {
  window.location.search = query;
  check(library.SlopCity_IsDeferredRenderingDiagnosticsEnabled() === 0,
    query + ' retains ordinary Forward rendering');
}
window.location.search = '?qa=1&rendering=deferred';
check(library.SlopCity_IsDeferredRenderingDiagnosticsEnabled() === 1,
  'Only exact QA and deferred opt-in selects the comparison');
const webInitialization = finish.match(/if \(!browserProfileInitialized\)[\s\S]*?QualitySettings\.pixelLightCount/);
check(webInitialization && webInitialization[0].includes('SlopCity_IsDeferredRenderingDiagnosticsEnabled() != 0')
  && /diagnosticDeferred && !lightweightFinish\s*\? RenderingPath\.DeferredShading : RenderingPath\.Forward/.test(webInitialization[0]),
  'Camera initialization selects deferred once before its first frame, without overriding a memory fallback');
const memoryFallback = finish.match(/private void ReduceMemory\(\)[\s\S]*?private void Initialize/);
check(memoryFallback && /lightweightFinish = true/.test(memoryFallback[0])
  && /view\.renderingPath = RenderingPath\.Forward/.test(memoryFallback[0]) && !/diagnosticDeferred/.test(memoryFallback[0]),
  'Low-memory recovery always returns to Forward regardless of the QA opt-in');
check(finish.includes('view.renderingPath = lightweightFinish ? RenderingPath.Forward : RenderingPath.DeferredShading;'),
  'Native rendering path policy remains unchanged');
for (const query of ['', '?qa=0', '?qa=01', '?qa=true']) {
  window.location.search = query;
  check(library.SlopCity_IsMemoryDiagnosticsEnabled() === 0, query + ' does not enable profiling');
  library.SlopCity_ReportMemory(1, 2, 3, 1.25, 2.5, -3.75, 90, 333, 1);
}
check(created === 0 && supportQueries === 0, 'Ordinary play creates no diagnostics DOM or GL queries');
window.location.search = '?qa=1';
check(library.SlopCity_IsMemoryDiagnosticsEnabled() === 1, 'Explicit QA enables memory sampling');
for (let i = 0; i < 1000; i++) library.SlopCity_ReportMemory(400000000 + i, 500000000, 1000000, i / 4, 2.5, -3.75, 90, 333, 3);
check(created === 1 && nodes.size === 1 && supportQueries === 2,
  'Repeated reports retain one node and inspect compression support once');
const output = nodes.get('slop-city-memory-diagnostics');
let snapshot = JSON.parse(output.textContent);
check(output.hidden && output['aria-hidden'] === 'true' && output['data-snapshot'] === output.textContent,
  'Snapshot remains hidden and readable through its data attribute');
check(snapshot.wasmHeapBytes === 536870912 && snapshot.unityAllocatedBytes === 400000999
  && snapshot.unityReservedBytes === 500000000 && snapshot.managedUsedBytes === 1000000,
  'Snapshot reports byte counts and latest sample without accumulating history');
check(snapshot.textureCompressionExtensions.length === 3
  && snapshot.textureCompressionExtensions.includes('WEBGL_compressed_texture_etc')
  && !snapshot.textureCompressionExtensions.includes('EXT_color_buffer_float')
  && snapshot.compressedTextureFormatEnums.includes(0x9274) && snapshot.compressedTextureFormatEnums.includes(0x9278),
  'Compression extensions exclude unrelated support and include core ETC2 format enums');
check(JSON.stringify(snapshot.cameraPosition) === '[249.75,2.5,-3.75]' && snapshot.cameraYaw === 90 && snapshot.cameraPitch === 333,
  'Latest camera position and Euler angles retain their argument order without history');
check(snapshot.actualRenderingPath === 3, 'Snapshot reports actual DeferredShading rather than the requested query');
context.Module.HEAPU8 = { buffer: { byteLength: 805306368 } };
library.SlopCity_ReportMemory(4, 5, 6, -1.25, 0, 8, 180, 27, 1);
snapshot = JSON.parse(output.textContent);
check(snapshot.wasmHeapBytes === 805306368 && snapshot.unityAllocatedBytes === 4,
  'New samples observe the current Wasm buffer after growth');
check(JSON.stringify(snapshot.cameraPosition) === '[-1.25,0,8]' && snapshot.cameraYaw === 180 && snapshot.cameraPitch === 27,
  'Subsequent reports replace all camera scalars alongside heap growth');
check(snapshot.actualRenderingPath === 1, 'Hardware or memory fallback reports actual Forward');
library.SlopCity_ReportMemory(4, 5, 6, 0, 0, 0, 0, 0, -1);
check(JSON.parse(output.textContent).actualRenderingPath === -1, 'Missing camera remains distinct from Forward');
library.SlopCity_ReportFrameRate(30, 33, 36, 400, 800);
check(JSON.stringify(fpsCalls) === '[[30,33,36,400,800]]', 'Existing FPS reporting remains unchanged');
window.location.search = '';
const previous = output.textContent;
library.SlopCity_ReportMemory(7, 8, 9, 9, 8, 7, 270, 0, 3);
check(output.textContent === previous, 'Reporter itself stops updating when QA is disabled');
console.log(JSON.stringify({ passed: true, checks, framework: !!framework,
  scope: 'QA gate source contract and executable JS, deferred path opt-in/fallback, bounded snapshots, nine-argument camera ABI/QA sampling, current Wasm heap and existing-context texture support; no C# compilation or physical memory measurement' }));
