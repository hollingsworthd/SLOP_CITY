#!/usr/bin/env python3
"""Verify the real compiled framework contract and gzip-preserving staging patch."""
import argparse
import contextlib
import gzip
import hashlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import package_web
from release_wasm_binary import RELEASE, SUCCESS_HOOK, release_framework_binary
from test_web_security import LAUNCHER, UNITY_LOADER, Page, sri

ROOT = Path(__file__).resolve().parents[1]
FRAMEWORK = ROOT / 'Unity/Builds/WebGL-v0.12.0/Build/WebGL-v0.12.0.framework.js.unityweb'


class WasmBinaryRelease(unittest.TestCase):
    def fixture(self, source, framework):
        (source / 'Build').mkdir(parents=True)
        (source / 'StreamingAssets/Media').mkdir(parents=True)
        (source / 'StreamingAssets/Media/creator-feeds.html').write_text('<html><head><meta charset="utf-8"></head><body></body></html>')
        (source / 'index.html').write_text(LAUNCHER)
        (source / 'guard.js').write_text('window.guard = true;')
        for name, data in {
            'WebGL.loader.js': UNITY_LOADER,
            'WebGL.framework.js.unityweb': framework,
            'WebGL.wasm.unityweb': b'wasm',
            'WebGL.data.unityweb': b'city',
        }.items():
            (source / 'Build' / name).write_bytes(data)

    def test_pinned_framework_preserves_gzip_and_changes_one_success_boundary(self):
        original = FRAMEWORK.read_bytes()
        patched = release_framework_binary(original)
        before = gzip.decompress(original).decode()
        after = gzip.decompress(patched).decode()
        self.assertEqual(after.replace(SUCCESS_HOOK + RELEASE, SUCCESS_HOOK), before)
        self.assertEqual(patched[:64], original[:64])
        self.assertEqual(release_framework_binary(original), patched)
        self.assertIn(b'UnityWeb Compressed Content (gzip)\0', patched[:128])
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'framework.unityweb'
            output.write_bytes(patched)
            subprocess.run(['node', str(ROOT / 'scripts/test_wasm_binary_release.js'),
                            '--framework', str(output)], check=True)

    def test_contract_changes_and_double_patches_fail_closed(self):
        for data in (b'framework', b'var wasmBinary;',
                     b'var wasmBinary;' + SUCCESS_HOOK.encode() * 2 + b'}',
                     b'var wasmBinary;' + (SUCCESS_HOOK + RELEASE + '}').encode()):
            with self.assertRaises(ValueError):
                release_framework_binary(data)

    def test_package_hashes_chunks_and_integrity_follow_staged_framework(self):
        original = FRAMEWORK.read_bytes()
        expected = release_framework_binary(original)
        relative = 'Build/WebGL.framework.js.unityweb'
        digest = hashlib.sha256(expected).hexdigest()
        for limit in (package_web.MAX_FILE, 30_000):
            with self.subTest(limit=limit), tempfile.TemporaryDirectory() as folder:
                source, output = Path(folder) / 'source', Path(folder) / 'out'
                self.fixture(source, original)
                before = {p.relative_to(source).as_posix(): p.read_bytes() for p in source.rglob('*') if p.is_file()}
                with patch.object(package_web, 'MAX_FILE', limit), patch.object(package_web, 'PART_SIZE', 10_000), contextlib.redirect_stdout(io.StringIO()):
                    package_web.package(source, output)
                manifest = json.loads((output / 'chunk-manifest.json').read_text())
                receipt = json.loads((output / package_web.MARKER).read_text())
                self.assertEqual(manifest['integrity'][relative], digest)
                self.assertEqual(manifest['sizes'][relative], len(expected))
                self.assertEqual(receipt['sourceFiles'][relative], {'size': len(original), 'sha256': hashlib.sha256(original).hexdigest()})
                if limit < len(expected):
                    entry = manifest['files'][relative]
                    self.assertFalse((output / relative).exists())
                    self.assertEqual(entry['sha256'], digest)
                    self.assertEqual(entry['size'], len(expected))
                    delivered = b''.join((output / part['path']).read_bytes() for part in entry['parts'])
                    for part in entry['parts']:
                        self.assertIn(digest[:16], part['path'])
                        self.assertEqual(part['sha256'], hashlib.sha256((output / part['path']).read_bytes()).hexdigest())
                else:
                    delivered = (output / relative).read_bytes()
                    self.assertEqual(receipt['packagedFiles'][relative], {'size': len(expected), 'sha256': digest})
                self.assertEqual(delivered, expected)
                html = (output / 'index.html').read_text()
                self.assertIn('frameworkUrl: "' + relative + '?sha256=' + digest + '"', html)
                adapter = (output / package_web.ADAPTER).read_bytes()
                self.assertIn(digest.encode(), adapter)
                adapter_tag = next(attrs for tag, attrs in Page(html).tags if tag == 'script' and attrs.get('src', '').startswith(package_web.ADAPTER + '?'))
                self.assertEqual(adapter_tag['integrity'], sri(adapter))
                after = {p.relative_to(source).as_posix(): p.read_bytes() for p in source.rglob('*') if p.is_file()}
                self.assertEqual(after, before)

    def test_packaging_contract_failure_preserves_previous_destination(self):
        with tempfile.TemporaryDirectory() as folder:
            source, output = Path(folder) / 'source', Path(folder) / 'out'
            self.fixture(source, FRAMEWORK.read_bytes())
            with contextlib.redirect_stdout(io.StringIO()):
                package_web.package(source, output)
            before = {p.relative_to(output).as_posix(): p.read_bytes() for p in output.rglob('*') if p.is_file()}
            framework = source / 'Build/WebGL.framework.js.unityweb'
            framework.write_bytes(b'changed compiler contract')
            with self.assertRaisesRegex(ValueError, 'instantiation contract changed'):
                package_web.package(source, output)
            self.assertEqual(framework.read_bytes(), b'changed compiler contract')
            after = {p.relative_to(output).as_posix(): p.read_bytes() for p in output.rglob('*') if p.is_file()}
            self.assertEqual(after, before)
            self.assertFalse(list(output.parent.glob('.out-packaging-*')))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--framework', type=Path, help='Actual unpatched Unity framework fixture')
    args, remaining = parser.parse_known_args()
    if args.framework:
        FRAMEWORK = args.framework
    unittest.main(argv=[__file__, *remaining], verbosity=2)
