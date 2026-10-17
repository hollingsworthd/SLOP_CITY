#!/usr/bin/env python3
"""Focused static export security regressions; fixtures use temporary directories."""
import base64
import contextlib
import hashlib
import io
import json
from html.parser import HTMLParser
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

import harden_web
import package_web
from patch_unity_loader import harden_unity_loader
from release_wasm_binary import BINARY_COPY_HOOK, SUCCESS_HOOK, release_framework_binary


SCRIPTS = Path(__file__).resolve().parent
TEMPLATE = SCRIPTS.parent / 'Unity/Assets/WebGLTemplates/SlopCity'


def sri(data):
    return 'sha256-' + base64.b64encode(hashlib.sha256(data).digest()).decode()


class Page(HTMLParser):
    def __init__(self, text):
        super().__init__()
        self.tags = []
        self.feed(text)

    def handle_starttag(self, tag, attrs):
        self.tags.append((tag, dict(attrs)))


LAUNCHER = '''<!doctype html>
<html><head><meta charset="utf-8">
<script id="redirect">window.example = "SLOP CITY";</script>
<script src="guard.js?v=test"></script>
</head><body>
  <script>
    const config = {
      dataUrl: "Build/WebGL.data.unityweb",
      frameworkUrl: "Build/WebGL.framework.js.unityweb",
      codeUrl: "Build/WebGL.wasm.unityweb"
    };
    function fail(message, detail) {
      console.log(message, detail);
    }
    function launch() {
      const script = document.createElement("script");
      script.src = "Build/WebGL.loader.js";
      script.onload = function () {
        packageHook();
      };
    }
    function packageHook() {
        createUnityInstance(canvas, config, function (progress) {
          console.log(progress);
        }).then(function (instance) {
          window.instance = instance;
        });
    }
  </script>
</body></html>'''

# Reduced, syntactically complete Unity 6000.3 loader. These exact minified
# startup/decompression/framework boundaries match the preserved v0.10.1 export;
# unrelated rendering/cache code is omitted so only data exceeds the chunk limit.
# The real production patcher must consume every boundary, without being mocked.
UNITY_LOADER = '''function createUnityInstance(t,n,l){var b=n,P={};b.deinitializers=[];b.preRun=[];function c(e,t){console.error(e)}function A(t){var r=b[t];return Promise.resolve(new Uint8Array()).then(function(e){return i=e,s=r,new Promise(function(e,t){try{for(var n in P){if(P[n].hasUnityMarker(i))return e(P[n].decompress(i))}e(i)}catch(e){t(e)}});var i,s}).catch(function(e){var t="Failed to download file "+r;"file:"==location.protocol?c(t,"error"):console.error(t)})}function T(){return Promise.all([A("frameworkUrl").then(function(e){var s=URL.createObjectURL(new Blob([e],{type:"application/javascript"}));return new Promise(function(a,e){var i=document.createElement("script");i.src=s,i.onload=function(){a(unityFramework)},i.onerror=e,document.body.appendChild(i),b.deinitializers.push(function(){document.body.removeChild(i)})})})])}var e;e=A("dataUrl");b.preRun.push(function(){return e});return T()}'''.encode()
UNITY_FRAMEWORK = ('var wasmBinary;function getBinarySync(file){' + BINARY_COPY_HOOK + '}'
                   + SUCCESS_HOOK + 'return exports;}').encode()


class WebSecurity(unittest.TestCase):
    def fixture(self, root):
        (root / 'Build').mkdir()
        (root / 'StreamingAssets/Media').mkdir(parents=True)
        (root / 'index.html').write_text(LAUNCHER)
        (root / 'guard.js').write_text('window.guard = true;')
        for name, data in {
            'WebGL.loader.js': UNITY_LOADER,
            'WebGL.framework.js.unityweb': UNITY_FRAMEWORK,
            'WebGL.wasm.unityweb': b'wasm',
            'WebGL.data.unityweb': b'abcdefghijklmnopqrstuvwxyz0123456789',
        }.items():
            (root / 'Build' / name).write_bytes(data)
        (root / 'StreamingAssets/Media/creator-feeds.html').write_text(
            '<html><head><meta charset="utf-8"></head><body>'
            '<script src="https://www.tiktok.com/embed.js"></script></body></html>')

    def test_private_export_files_are_rejected(self):
        for path in ['.env', '.git/config.json', 'Build/.secret.js', '__MACOSX/a.js',
                     'node_modules/a.js', '../outside.js', 'Assets/source.cs',
                     'key.pem', 'identity.p12', 'database.sqlite', 'game.pdb',
                     'password.txt', 'Unity.dll', 'credentials.json', 'secrets.json',
                     'service-account.json']:
            with self.subTest(path=path), self.assertRaises(ValueError):
                harden_web.check_input(path)

    def test_credential_detection_redacts_values(self):
        fixtures = ['-----BEGIN PRIVATE KEY-----', '-----BEGIN RSA PRIVATE KEY-----',
                    'ghp_' + 'x' * 36, 'github_pat_' + 'x' * 50,
                    'sk-proj-' + 'x' * 50, 'AKIA' + 'X' * 16]
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'public.json'
            for value in fixtures:
                with self.subTest(kind=value.split('-', 1)[0][:8]):
                    path.write_text('{"testFixture": "' + value + '"}')
                    with self.assertRaises(ValueError) as caught:
                        harden_web.check_contents(path)
                    self.assertNotIn(value, str(caught.exception))
                    self.assertIn('public.json', str(caught.exception))
            path.write_text('{"publicCreator": "machine_publishers"}')
            harden_web.check_contents(path)

    def test_portal_has_no_executable_embeds(self):
        text = (TEMPLATE / 'creator-feeds.html').read_text()
        tags = Page(text).tags
        self.assertFalse(set(t for t, _ in tags) & {'script', 'iframe', 'video', 'audio', 'object', 'embed'})
        links = [a for t, a in tags if t == 'a' and a.get('target') == '_blank']
        self.assertEqual({a['href'] for a in links}, {
            'https://www.tiktok.com/@1984drh', 'https://www.tiktok.com/@machine_publishers'})
        self.assertTrue(all({'noopener', 'noreferrer'} <= set(a['rel'].split()) for a in links))

    def test_unsafe_html_and_external_scripts_fail_closed(self):
        bad = [
            '<script src="https://evil.invalid/a.js"></script>',
            "<script src='https://evil.invalid/a.js'></script>",
            '<script src="../outside.js"></script>',
            '<script src="%2e%2e/outside.js"></script>',
            '<button onclick="evil()">Go</button>',
            '<a href="javascript:evil()">Go</a>',
            '<iframe src="https://evil.invalid"></iframe>',
            '<base href="https://evil.invalid/">',
            '<a target="_blank" href="https://evil.invalid">Go</a>',
        ]
        for markup in bad:
            with self.subTest(markup=markup), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                (root / 'index.html').write_text('<head><meta charset="utf-8"></head>' + markup)
                with self.assertRaises(ValueError):
                    harden_web.secure_page(root / 'index.html', root)
        # A different/malformed Unity loader must fail before replacing its file.
        anchors = [
            b'function createUnityInstance(t,n,l){',
            b'new Promise(function(e,t){try{for(var n in P){',
            b';var i,s}).catch(function(e){var t="Failed to download file "',
            b':console.error(t)})}function T(){',
            b'return new Promise(function(a,e){var i=document.createElement("script");',
            b'b.deinitializers.push(function(){document.body.removeChild(i)})})',
            b'e=A("dataUrl");b.preRun.push(',
        ]
        for anchor in anchors:
            for malformed in (UNITY_LOADER.replace(anchor, b'', 1), UNITY_LOADER + anchor):
                with self.subTest(anchor=anchor, duplicate=malformed.endswith(anchor)), tempfile.TemporaryDirectory() as folder:
                    path = Path(folder) / 'WebGL.loader.js'
                    path.write_bytes(malformed)
                    with self.assertRaises(ValueError):
                        harden_unity_loader(path)
                    self.assertEqual(path.read_bytes(), malformed)

    def test_packaged_hashes_match_final_mutated_html(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, out = root / 'source', root / 'out'
            source.mkdir()
            self.fixture(source)
            with patch.object(package_web, 'MAX_FILE', 30_000), patch.object(package_web, 'PART_SIZE', 10_000):
                # Force only the synthetic data file through the real chunk path.
                (source / 'Build/WebGL.data.unityweb').write_bytes(b'city' * 7501)
                package_web.package(source, out)
            text = (out / 'index.html').read_text()
            tags = Page(text).tags
            policy = next(a['content'] for t, a in tags if t == 'meta' and a.get('http-equiv') == 'Content-Security-Policy')
            directives = dict(v.strip().split(' ', 1) for v in policy.split(';'))
            self.assertNotIn("'unsafe-eval'", directives['script-src'])
            self.assertNotIn("'unsafe-inline'", directives['script-src'])
            self.assertIn("'wasm-unsafe-eval'", directives['script-src'])
            self.assertEqual(directives['script-src-attr'], "'none'")
            self.assertEqual(directives['connect-src'], "'self' https://api.github.com/repos/hollingsworthd/slop-city/")
            self.assertEqual(directives['frame-src'], "'none'")
            inline = re.findall(r'<script\b([^>]*)>([\s\S]*?)</script>', text)
            for attrs, body in inline:
                if 'src=' not in attrs:
                    self.assertIn("'" + sri(body.encode()) + "'", directives['script-src'])
            self.assertIn('window.slopCityChunks.install', text)
            for tag, attrs in tags:
                if tag == 'script' and 'src' in attrs:
                    target = out / attrs['src'].split('?', 1)[0]
                    self.assertEqual(attrs['integrity'], sri(target.read_bytes()))
                    self.assertEqual(attrs['crossorigin'], 'anonymous')
                    self.assertEqual(parse_qs(urlsplit(attrs['src']).query)['sha256'], [hashlib.sha256(target.read_bytes()).hexdigest()])
            dynamic_hash = re.search(r'script.integrity = "([^"]+)"', text).group(1)
            final_loader = (out / 'Build/WebGL.loader.js').read_bytes()
            self.assertNotEqual(final_loader, UNITY_LOADER)
            self.assertEqual((source / 'Build/WebGL.loader.js').read_bytes(), UNITY_LOADER)
            self.assertIn(b'slopDecompress(i,s)', final_loader)
            self.assertIn(b'window.slopCityStopStartup', final_loader)
            self.assertEqual(dynamic_hash, sri(final_loader))
            self.assertLess(text.index('http-equiv="Content-Security-Policy"'), text.index('<script'))
            portal = (out / 'StreamingAssets/Media/creator-feeds.html').read_text()
            self.assertNotIn('embed.js', portal)
            self.assertIn("script-src &#x27;none&#x27;", portal)
            self.assertIn("frame-ancestors 'none'", (out / '_headers').read_text())

    def test_release_transition_changes_every_changed_asset_cache_key(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / 'source'; source.mkdir(); self.fixture(source)
            pages = []
            for revision in (1, 2):
                for name in ('guard.js', 'Build/WebGL.loader.js', 'Build/WebGL.framework.js.unityweb',
                             'Build/WebGL.wasm.unityweb', 'Build/WebGL.data.unityweb'):
                    data = (name + str(revision)).encode() * (2000 if '.data.' in name else 1)
                    if name.endswith('.loader.js'):
                        data = UNITY_LOADER + b'\n// release fixture ' + str(revision).encode()
                    if name.endswith('.framework.js.unityweb'):
                        data = UNITY_FRAMEWORK + b'\n// release fixture ' + str(revision).encode()
                    (source / name).write_bytes(data)
                out = root / str(revision)
                with patch.object(package_web, 'MAX_FILE', 30_000), patch.object(package_web, 'PART_SIZE', 10_000):
                    package_web.package(source, out)
                text = (out / 'index.html').read_text()
                urls = {urlsplit(a['src']).path: a['src'] for t, a in Page(text).tags if t == 'script' and 'src' in a}
                loader_url = re.search(r'script.src = "([^"]+)"', text).group(1)
                urls[urlsplit(loader_url).path] = loader_url
                final_loader = (out / urlsplit(loader_url).path).read_bytes()
                self.assertNotEqual(final_loader, (source / urlsplit(loader_url).path).read_bytes())
                self.assertEqual(parse_qs(urlsplit(loader_url).query)['sha256'], [hashlib.sha256(final_loader).hexdigest()])
                self.assertEqual(re.search(r'script.integrity = "([^"]+)"', text).group(1), sri(final_loader))
                for _, url in re.findall(r'(dataUrl|frameworkUrl|codeUrl):\s*"([^"]+)"', text):
                    urls[urlsplit(url).path] = url
                    data = (source / urlsplit(url).path).read_bytes()
                    if urlsplit(url).path.endswith('.framework.js.unityweb'):
                        data = release_framework_binary(data)
                    expected = hashlib.sha256(data).hexdigest()
                    self.assertEqual(parse_qs(urlsplit(url).query)['sha256'], [expected])
                self.assertIn('if (!window.slopCityChunks)', text)
                self.assertIn('try { window.slopCityChunks.restore(); } catch (error) { console.warn("City download cleanup failed.", error); }', text)
                pages.append(urls)
            self.assertEqual(set(pages[0]), set(pages[1]))
            self.assertEqual(len(pages[0]), 6)
            # A browser cache warmed with the old URLs cannot answer new requests.
            self.assertTrue(set(pages[0].values()).isdisjoint(pages[1].values()))

    def test_total_upload_budget_includes_receipt_and_preserves_previous_package(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, out = root / 'source', root / 'out'
            source.mkdir()
            self.fixture(source)
            def snapshot(path):
                return {p.relative_to(path).as_posix(): p.read_bytes() for p in path.rglob('*') if p.is_file()}
            source_before = snapshot(source)
            with contextlib.redirect_stdout(io.StringIO()):
                package_web.package(source, out)
            receipt = json.loads((out / package_web.MARKER).read_text())
            self.assertEqual(receipt['maximumPackageBytes'], 248 * package_web.MIB)
            # Solve for an exact inclusive limit while accounting for its own
            # decimal representation in the generated receipt. No large fixture.
            assets_bytes = sum(p.stat().st_size for p in out.rglob('*') if p.is_file() and p.name != package_web.MARKER)
            limit = assets_bytes + (out / package_web.MARKER).stat().st_size
            for _ in range(5):
                receipt['maximumPackageBytes'] = limit
                measured = assets_bytes + len((json.dumps(receipt, indent=2) + '\n').encode())
                if measured == limit:
                    break
                limit = measured
            else:
                self.fail('Receipt size did not converge to an exact upload limit')
            self.assertEqual(measured, limit)
            with patch.object(package_web, 'MAX_PACKAGE', limit), contextlib.redirect_stdout(io.StringIO()):
                package_web.package(source, out)
            self.assertEqual(sum(p.stat().st_size for p in out.rglob('*') if p.is_file()), limit)
            self.assertEqual(json.loads((out / package_web.MARKER).read_text())['maximumPackageBytes'], limit)
            self.assertEqual(snapshot(source), source_before)
            prior = snapshot(out)
            # The one-byte reduction remains above all bytes excluding the
            # receipt, so this specifically rejects omission of the receipt.
            self.assertGreater(limit - 1, assets_bytes)
            with patch.object(package_web, 'MAX_PACKAGE', limit - 1), patch.object(package_web.os, 'replace') as replace:
                with self.assertRaisesRegex(ValueError, 'upload budget'):
                    package_web.package(source, out)
                replace.assert_not_called()
            self.assertEqual(snapshot(out), prior)
            self.assertEqual(snapshot(source), source_before)
            self.assertFalse(list(root.glob('.out-packaging-*')))
            self.assertFalse(list(root.glob('.out-previous-*')))


if __name__ == '__main__':
    unittest.main(verbosity=2)
