"""Apply static-site security after all Unity/transport launcher mutations."""
import base64
import hashlib
from html import escape
from html.parser import HTMLParser
from pathlib import Path
import re
from urllib.parse import urlsplit, unquote, urlunsplit
from patch_unity_loader import harden_unity_loader


def integrity(data):
    return 'sha256-' + base64.b64encode(hashlib.sha256(data).digest()).decode('ascii')


def content_url(url, digest):
    """Give changed bytes a distinct browser cache key while retaining existing queries."""
    parsed = urlsplit(url)
    query = parsed.query + ('&' if parsed.query else '') + 'sha256=' + digest
    return urlunsplit(parsed._replace(query=query))


def check_input(relative):
    path = Path(relative)
    # Exports are public. Fail rather than silently ship credentials, source,
    # editor caches or hidden machine metadata copied into the output by mistake.
    if any(part.startswith('.') or part in ('__MACOSX', 'node_modules') for part in path.parts):
        raise ValueError('Private/hidden path in Web export: ' + str(relative))
    allowed = {'.html', '.js', '.json', '.css', '.unityweb', '.png', '.jpg', '.jpeg',
               '.svg', '.ico', '.mp4', '.webm', '.wav', '.ogg', '.mp3', '.woff', '.woff2', '.ttf'}
    if path.suffix.lower() not in allowed:
        raise ValueError('Unexpected public export file type: ' + str(relative))
    if path.name.lower() in ('credentials.json', 'secrets.json', 'service-account.json'):
        raise ValueError('Credential file in Web export: ' + str(relative))


def check_contents(path):
    if path.suffix.lower() not in ('.js', '.html', '.json', '.css'):
        return
    # Do not print matching values: the release must fail without leaking a key.
    data = path.read_bytes()
    if re.search(rb'-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----|\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|sk-proj-[A-Za-z0-9_-]{40,}|AKIA[A-Z0-9]{16})\b', data):
        raise ValueError('Possible credential in public export: ' + path.name)


class PageAudit(HTMLParser):
    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if any(key.startswith('on') for key in values):
            raise ValueError('Inline event handlers are not allowed in published HTML')
        if tag in ('iframe', 'object', 'embed', 'base', 'form', 'video', 'audio'):
            raise ValueError('Unexpected active content in published HTML: ' + tag)
        if tag == 'a' and values.get('target') == '_blank':
            if not {'noopener', 'noreferrer'} <= set(values.get('rel', '').split()):
                raise ValueError('External-window link must isolate its opener')
        for key in ('href', 'src'):
            if values.get(key, '').strip().lower().startswith('javascript:'):
                raise ValueError('Executable URL in published HTML')


class ScriptAttributes(HTMLParser):
    def handle_starttag(self, tag, attrs):
        self.values = dict(attrs)


def secure_page(path, root):
    text = path.read_text()
    if 'http-equiv="Content-Security-Policy"' in text:
        raise ValueError('Security must be applied once to a fresh export')
    PageAudit().feed(text)
    scripts = []
    external = []

    def script(match):
        attrs, body = match.group(1), match.group(2)
        parsed = ScriptAttributes()
        parsed.feed('<script' + attrs + '>')
        src = parsed.values.get('src')
        if src is None:
            scripts.append("'" + integrity(body.encode()) + "'")
            return match.group(0)
        url = urlsplit(src)
        if url.scheme or url.netloc or url.fragment:
            raise ValueError('Only packaged first-party scripts may execute: ' + src)
        target = (path.parent / unquote(url.path)).resolve()
        if not target.is_relative_to(root.resolve()) or not target.is_file():
            raise ValueError('Script escapes or is missing from package: ' + src)
        if 'integrity' in parsed.values:
            raise ValueError('Unexpected preexisting script integrity')
        data = target.read_bytes()
        versioned = content_url(src, hashlib.sha256(data).hexdigest())
        attrs = re.sub(r'\bsrc\s*=\s*([\x22\x27])[\s\S]*?\1',
                       'src="' + escape(versioned, quote=True) + '"', attrs, count=1, flags=re.I)
        external.append(versioned)
        return '<script' + attrs + ' integrity="' + integrity(data) + '" crossorigin="anonymous">' + body + '</script>'

    text = re.sub(r'<script\b([^>]*)>([\s\S]*?)</script>', script, text, flags=re.I)
    script_policy = "'self' blob: 'wasm-unsafe-eval' " + ' '.join(scripts) if scripts or external else "'none'"
    policy = '; '.join([
        "default-src 'none'", 'script-src ' + script_policy.strip(),
        "script-src-attr 'none'", "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data: blob:", "media-src 'self' blob:",
        "connect-src 'self'" + (" https://api.github.com/repos/hollingsworthd/slop-city/"
            if path.resolve() == (root / "index.html").resolve() else ""),
        "worker-src 'self' blob:", "child-src 'self' blob:",
        "frame-src 'none'", "object-src 'none'", "base-uri 'none'", "form-action 'none'"
    ])
    meta = '\n  <meta http-equiv="Content-Security-Policy" content="' + escape(policy, quote=True) + '">\n'
    if '<meta name="referrer"' not in text:
        meta += '  <meta name="referrer" content="no-referrer">\n'
    # Must precede every executable tag, including the canonical redirect.
    text = text.replace('<meta charset="utf-8">', '<meta charset="utf-8">' + meta, 1)
    if meta not in text:
        raise ValueError('Missing UTF-8 charset insertion point: ' + str(path))
    path.write_text(text)
    return policy


def harden(staging, template):
    portal = template / 'creator-feeds.html'
    (staging / 'StreamingAssets/Media/creator-feeds.html').write_bytes(portal.read_bytes())
    launcher = staging / 'index.html'
    text = launcher.read_text()
    loader = re.search(r'      script.src = "(Build/[^"]+\.js)";', text)
    if not loader:
        raise ValueError('Unity loader hook missing')
    harden_unity_loader(staging / loader.group(1))
    data = (staging / loader.group(1)).read_bytes()
    digest = integrity(data)
    loader_url = content_url(loader.group(1), hashlib.sha256(data).hexdigest())
    text = text.replace(loader.group(0), '      script.src = "' + loader_url + '";'
                        + '\n      script.integrity = "' + digest + '";\n      script.crossOrigin = "anonymous";', 1)
    launcher.write_text(text)
    policies = {p.relative_to(staging).as_posix(): secure_page(p, staging) for p in staging.rglob('*.html')}
    # Standard static-host format. Meta CSP above remains enforced on hosts
    # that do not support _headers. Verify real response headers after release.
    lines = ['/*', '  X-Content-Type-Options: nosniff', '  X-Frame-Options: DENY',
             '  Referrer-Policy: no-referrer', '  Strict-Transport-Security: max-age=31536000',
             '  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()']
    for name, policy in policies.items():
        paths = ['/', '/index.html'] if name == 'index.html' else ['/' + name]
        for route in paths:
            lines += [route, "  Content-Security-Policy: " + policy + "; frame-ancestors 'none'"]
    (staging / '_headers').write_text('\n'.join(lines) + '\n')
    return policies
