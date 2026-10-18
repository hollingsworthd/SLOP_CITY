#!/usr/bin/env node
'use strict';
// QA-only: apply exact project jslib function bodies to an already-public build
// so physical A/B can proceed before Unity compilation. Never deploy this tree.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const baseline = path.resolve(process.argv[2] || '/private/tmp/slop-mobile-fix-20261014/baseline-web');
const parent = path.dirname(baseline);
const harness = path.resolve(process.argv[3] || path.join(parent, 'physical-qa/slop-city-touch-sweep.js'));
assert.ok(baseline.startsWith('/private/tmp/'), 'QA baseline must be isolated temporary public content');
assert.ok(fs.existsSync(harness), 'The shared physical QA harness must be ready');
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
const sri = data => 'sha256-' + crypto.createHash('sha256').update(data).digest('base64');
function tree(root) {
  const output = {};
  for (const name of fs.readdirSync(root)) {
    const file = path.join(root, name);
    assert.ok(!fs.lstatSync(file).isSymbolicLink(), 'No QA source symlinks');
    if (fs.statSync(file).isDirectory()) for (const [child, hash] of Object.entries(tree(file))) output[name + '/' + child] = hash;
    else output[name] = sha(fs.readFileSync(file));
  }
  return output;
}
function once(source, previous, replacement) {
  assert.equal(source.split(previous).length - 1, 1, 'One pinned source contract required: ' + previous.slice(0, 90));
  return source.replace(previous, replacement);
}
function declaration(source, name) {
  const marker = 'function ' + name + '(';
  assert.equal(source.split(marker).length - 1, 1, 'One compiled helper required: ' + name);
  const start = source.indexOf(marker), open = source.indexOf('{', start);
  let depth = 0, quote = '', comment = '';
  for (let i = open; i < source.length; i++) {
    const c = source[i], n = source[i + 1];
    if (comment === 'line') { if (c === '\n') comment = ''; continue; }
    if (comment === 'block') { if (c === '*' && n === '/') { comment = ''; i++; } continue; }
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = ''; continue; }
    if (c === '/' && n === '/') { comment = 'line'; i++; continue; }
    if (c === '/' && n === '*') { comment = 'block'; i++; continue; }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '{') depth++; else if (c === '}' && --depth === 0) return source.slice(start, i + 1);
  }
  throw Error('Unclosed compiled helper ' + name);
}
function gzipWithUnityHeader(original, source) {
  const flags = original[3]; let end = 10;
  if (flags & 4) end += 2 + original.readUInt16LE(end);
  for (const flag of [8, 16]) if (flags & flag) end = original.indexOf(0, end) + 1;
  if (flags & 2) end += 2;
  assert.ok(original.subarray(0, end).includes(Buffer.from('UnityWeb Compressed Content (gzip)\0')));
  const body = Buffer.from(source);
  let crc = -1;
  for (const byte of body) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1)); }
  const trailer = Buffer.alloc(8); trailer.writeUInt32LE((crc ^ -1) >>> 0); trailer.writeUInt32LE(body.length >>> 0, 4);
  const result = Buffer.concat([original.subarray(0, end), zlib.deflateRawSync(body, { level: 9 }), trailer]);
  assert.equal(zlib.gunzipSync(result).toString(), source);
  return result;
}
const fingerprints = tree(baseline);
assert.equal(fingerprints['index.html'], '592e3d802f2a56dcaef98fb9a27ce5eebe9716c422a106d4e7faf11bd50f33f8', 'Exact published v0.12.0 baseline required');
const frameworkName = 'Build/WebGL-v0.12.0.framework.js.unityweb';
const originalBytes = fs.readFileSync(path.join(baseline, frameworkName));
let framework = zlib.gunzipSync(originalBytes).toString();
const library = {};
for (const name of ['JS_Video_Destroy', 'JS_Video_UpdateToTexture', 'JS_Sound_Stop', 'JS_Sound_ReleaseInstance']) {
  library[name] = function () {}; library[name + '__deps'] = [];
}
const context = vm.createContext({ LibraryManager: { library }, mergeInto: (target, entries) => Object.assign(target, entries) });
const plugins = ['SlopCityVideoLifecycle', 'SlopCityVideoTexture', 'SlopCityAudioLifecycle'];
for (const name of plugins) vm.runInContext(fs.readFileSync(path.join(__dirname, '../Unity/Assets/Plugins/WebGL/' + name + '.jslib'), 'utf8'), context);
const aliases = [['_JS_Sound_Stop', 'slopCityNativeSoundStop'], ['_JS_Sound_ReleaseInstance', 'slopCityNativeSoundRelease']];
let additions = aliases.map(([native, alias]) => declaration(framework, native).replace('function ' + native + '(', 'function ' + alias + '('));
const untouchedAliases = new Set(['$slopCityNativeVideoDestroy', '$slopCityNativeVideoUpdateToTexture', '$slopCityNativeSoundStop', '$slopCityNativeSoundRelease']);
const replaced = [];
for (const [key, value] of Object.entries(library)) {
  if (typeof value !== 'function' || key.includes('__') || untouchedAliases.has(key)) continue;
  const name = key.startsWith('$') ? key.slice(1) : '_' + key;
  const body = value.toString().replace(/^function\s*\(/, 'function ' + name + '(');
  assert.ok(body.startsWith('function ' + name + '('), 'Named project helper emit');
  if (framework.includes('function ' + name + '(')) framework = once(framework, declaration(framework, name), body);
  else additions.push(body);
  replaced.push(name);
}
framework = once(framework, 'function _JS_Video_Create(', additions.join('\n') + '\nfunction _JS_Video_Create(');
framework = once(framework, 'function receiveInstance(instance,module){var exports=instance.exports;',
  'function receiveInstance(instance,module){var exports=instance.exports;wasmBinary=undefined;delete Module["wasmBinary"];');
// Verify the exact native render path was preserved byte-for-byte.
assert.equal(declaration(framework, 'slopCityNativeVideoUpdateToTexture'),
  declaration(zlib.gunzipSync(originalBytes).toString(), 'slopCityNativeVideoUpdateToTexture'));
const fixedBytes = gzipWithUnityHeader(originalBytes, framework);
new vm.Script(framework, { filename: 'browser-fixes.framework.js' });
function variant(name, fixed) {
  const output = path.join(parent, name);
  assert.ok(!fs.existsSync(output), 'Refusing to replace an existing QA variant: ' + output);
  fs.cpSync(baseline, output, { recursive: true, mode: fs.constants.COPYFILE_FICLONE });
  const changed = new Set(['index.html', '_headers', 'slop-city-package.json', 'slop-city-touch-sweep.js', 'qa-variant.json']);
  let html = fs.readFileSync(path.join(output, 'index.html'), 'utf8');
  if (fixed) {
    fs.writeFileSync(path.join(output, frameworkName), fixedBytes); changed.add(frameworkName);
    html = once(html, 'sha256=' + sha(originalBytes), 'sha256=' + sha(fixedBytes));
    html = html.replace('WebGL(?:-[0-9][A-Za-z0-9.-]*)?\\.framework', 'WebGL(?:-v?[0-9][A-Za-z0-9.-]*)?\\.framework');
    const manifestPath = path.join(output, 'chunk-manifest.json');
    const manifest = JSON.parse(fs.readFileSync(manifestPath));
    manifest.integrity[frameworkName] = sha(fixedBytes); manifest.sizes[frameworkName] = fixedBytes.length;
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n'); changed.add('chunk-manifest.json');
    const adapterPath = path.join(output, 'slop-city-chunks.js');
    const adapterOriginal = fs.readFileSync(adapterPath);
    const adapter = Buffer.from(once(once(adapterOriginal.toString(), sha(originalBytes), sha(fixedBytes)),
      '"' + frameworkName + '":' + originalBytes.length, '"' + frameworkName + '":' + fixedBytes.length));
    fs.writeFileSync(adapterPath, adapter); changed.add('slop-city-chunks.js');
    html = once(html, sha(adapterOriginal), sha(adapter)); html = once(html, sri(adapterOriginal), sri(adapter));
  }
  const harnessBytes = fs.readFileSync(harness); fs.writeFileSync(path.join(output, 'slop-city-touch-sweep.js'), harnessBytes);
  html = once(html, '</body>', '<script src="slop-city-touch-sweep.js?sha256=' + sha(harnessBytes)
    + '" integrity="' + sri(harnessBytes) + '" crossorigin="anonymous"></script>\n</body>');
  // Only the fixes change the existing launcher inline block. Refresh precisely
  // its CSP hash in both the actual meta policy and matching static-host header.
  let headers = fs.readFileSync(path.join(output, '_headers'), 'utf8');
  const inline = source => [...source.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)].filter(m => !/\bsrc=/.test(m[1])).map(m => m[2]);
  const oldInline = inline(fs.readFileSync(path.join(baseline, 'index.html'), 'utf8')), nextInline = inline(html);
  assert.equal(oldInline.length, nextInline.length);
  for (let i = 0; i < oldInline.length; i++) if (oldInline[i] !== nextInline[i]) {
    const oldHash = sri(Buffer.from(oldInline[i])), nextHash = sri(Buffer.from(nextInline[i]));
    assert.ok(html.includes(oldHash)); html = html.replaceAll(oldHash, nextHash); headers = headers.replaceAll(oldHash, nextHash);
  }
  fs.writeFileSync(path.join(output, 'index.html'), html); fs.writeFileSync(path.join(output, '_headers'), headers);
  const hashes = tree(output);
  for (const [file, hash] of Object.entries(fingerprints)) if (!changed.has(file)) assert.equal(hashes[file], hash, 'Unchanged public asset: ' + file);
  const packagePath = path.join(output, 'slop-city-package.json');
  const receipt = JSON.parse(fs.readFileSync(packagePath));
  for (const file of Object.keys(receipt.packagedFiles)) receipt.packagedFiles[file] = { size: fs.statSync(path.join(output, file)).size, sha256: hashes[file] };
  receipt.packagedFiles['slop-city-touch-sweep.js'] = { size: harnessBytes.length, sha256: sha(harnessBytes) };
  receipt.packagedBytes = Object.values(receipt.packagedFiles).reduce((sum, item) => sum + item.size, 0);
  receipt.qaOnly = true; receipt.productionBuildUnchanged = '0.12.0'; receipt.qaVariant = name;
  fs.writeFileSync(packagePath, JSON.stringify(receipt, null, 2) + '\n');
  const result = { qaOnly: true, createdAtUtc: new Date().toISOString(), variant: name, baselineLauncherSha256: fingerprints['index.html'],
    frameworkSha256: hashes[frameworkName], harnessSha256: sha(harnessBytes), visualSettingsUnchanged: true,
    changedFiles: [...changed], replacedCompiledFunctions: fixed ? replaced : [], noUnityBuild: true, noPublication: true };
  fs.writeFileSync(path.join(output, 'qa-variant.json'), JSON.stringify(result, null, 2) + '\n');
  return result;
}
const outputs = [variant('baseline-qa', false), variant('browser-fixes-web', true)];
assert.deepEqual(tree(baseline), fingerprints, 'Original published baseline remains byte-identical');
console.log(JSON.stringify({ passed: true, outputs }, null, 2));
