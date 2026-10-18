#!/usr/bin/env node
'use strict';
// Clone a completed public package and add only the gated camera QA harness.
// Production bytes remain untouched; the separate QA receipt records overrides.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
assert.equal(process.argv.length, 5, 'Usage: install_physical_qa.js candidate-web candidate-qa sweep.js');
const [source, target, harness] = process.argv.slice(2).map(value => path.resolve(value));
assert.ok(source.startsWith('/private/tmp/') && target.startsWith('/private/tmp/'), 'QA must remain in isolated temporary directories');
assert.ok(source !== target && !target.startsWith(source + '/'), 'Separate QA output required');
assert.ok(!fs.existsSync(target), 'Refusing to overwrite an existing QA output');
assert.ok(fs.existsSync(path.join(source, 'slop-city-package.json')), 'A completed production package marker is required');
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
const sri = data => 'sha256-' + crypto.createHash('sha256').update(data).digest('base64');
function tree(root) {
  const result = {};
  for (const name of fs.readdirSync(root)) {
    const file = path.join(root, name);
    assert.ok(!fs.lstatSync(file).isSymbolicLink(), 'No source symlinks');
    if (fs.statSync(file).isDirectory()) for (const [child, digest] of Object.entries(tree(file))) result[name + '/' + child] = digest;
    else result[name] = sha(fs.readFileSync(file));
  }
  return result;
}
const before = tree(source), harnessBytes = fs.readFileSync(harness);
const productionReceipt = JSON.parse(fs.readFileSync(path.join(source, 'slop-city-package.json')));
for (const [file, value] of Object.entries(productionReceipt.packagedFiles)) {
  assert.equal(before[file], value.sha256, 'Completed production package checksum: ' + file);
  assert.equal(fs.statSync(path.join(source, file)).size, value.size);
}
let html = fs.readFileSync(path.join(source, 'index.html'), 'utf8');
assert.ok(html.includes('http-equiv="Content-Security-Policy"') && /script-src[^;]*&#x27;self&#x27;/.test(html), 'Existing enforced self-script CSP required');
assert.equal(html.split('</body>').length, 2);
assert.ok(!html.includes('slop-city-touch-sweep.js'), 'Harness must be installed once');
fs.cpSync(source, target, { recursive: true, mode: fs.constants.COPYFILE_FICLONE });
fs.writeFileSync(path.join(target, 'slop-city-touch-sweep.js'), harnessBytes);
html = html.replace('</body>', '<script src="slop-city-touch-sweep.js?sha256=' + sha(harnessBytes)
  + '" integrity="' + sri(harnessBytes) + '" crossorigin="anonymous"></script>\n</body>');
fs.writeFileSync(path.join(target, 'index.html'), html);
const after = tree(target);
let preserved = 0;
for (const [file, digest] of Object.entries(before)) if (file !== 'index.html') { assert.equal(after[file], digest, 'Non-QA bytes unchanged: ' + file); preserved++; }
assert.deepEqual(tree(source), before, 'Production source remains byte-identical');
const receipt = { qaOnly: true, createdAtUtc: new Date().toISOString(), source, target,
  productionPackageBytesUnchanged: true, nonQaFilesPreserved: preserved,
  harnessSha256: sha(harnessBytes), sourceLauncherSha256: before['index.html'], qaLauncherSha256: after['index.html'],
  changedFiles: ['index.html', 'slop-city-touch-sweep.js'],
  copiedPackageReceiptDescribesProductionSource: true,
  qaRequires: 'qa=1&autosweep=1; manually Start only after real PLAY',
  visualSettingsUnchanged: true, noUnityBuild: true, noPublication: true };
fs.writeFileSync(path.join(target, 'qa-variant.json'), JSON.stringify(receipt, null, 2) + '\n');
console.log(JSON.stringify({ passed: true, ...receipt }, null, 2));
