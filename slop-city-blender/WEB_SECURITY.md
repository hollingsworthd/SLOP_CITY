# SLOP CITY™ web security

Initial audit: October 12, 2026, Site version 13 / game v0.9.7, security revision `2026-10-12-1`. The v0.9.11 license/source-reader update retains these protections, with the single repository-scoped API connection described below. It is published as Site version 17 at [slopcity.org](https://slopcity.org/?v=0.9.11); exact IDs are in `Unity/Builds/Reports/web-v0.9.11-publication.json`. These browser protections do not modify desktop live sync.

The public Unity game is a static download with local saves. This audit found no exposed application backend, login, database or privileged server action in this deployment. The older `slop-city/` prototype is a separate application and was not changed.

## Enforced protections

- A Content Security Policy in the HTML permits only the packaged scripts and exact inline script hashes, same-origin network/media and required Unity blob resources. JavaScript eval, inline event handlers, third-party frames, plugins and form submissions are blocked. WebAssembly compilation remains permitted for Unity; inline styles remain permitted for the launcher and engine.
- SHA-256 Subresource Integrity checks cover the launcher scripts and Unity loader. Fetch integrity checks cover the framework, Wasm and every data chunk before bytes reach Unity. Failed downloads stop with a recoverable reload message. Media bytes are retained and audited at packaging; runtime media playback is not independently SRI-checked.
- Changed script, loader, framework, Wasm and data URLs include their SHA-256 content version. This prevents a returning browser from reusing old cached bytes under new integrity metadata. Manifest lookup ignores the query only for matching; the request keeps its version and integrity checks. Missing helper/download failures offer reload guidance.
- The browser creator page links to the two creators without running TikTok's embed script in the game origin. New windows use `noopener,noreferrer`; the browser bridge rejects executable and unsafe URL schemes.
- Packaging rejects hidden files, unsupported private/source formats, symlinks, credential filenames and recognizable secret patterns. Failures identify the file without printing the secret. Pattern matching is a release guard, not proof that arbitrary credentials cannot exist.

## Hosting and trust limits

The deployed host ignores the portable `_headers` file. Successful application responses were checked directly: HSTS, nosniff, X-Frame-Options, Permissions-Policy and HTTP CSP/frame-ancestors from that file are **not active**. Meta CSP and meta no-referrer are active; a meta policy cannot provide frame-ancestors protection. HTTP redirects to HTTPS, but this is not equivalent to HSTS. Rejected requests can contain different edge headers; do not count them as application protections.

The current Sites tools do not expose WAF/rate-limit controls or a supported static custom-header setting. No account MFA, registrar lock or provider DDoS configuration was changed or verified. Adding those controls requires supported hosting/account configuration; do not assume moving this 205 MB game into a generic Worker preserves hosting and downloads.

Public browser code/assets can be copied and modified locally. Local saves are not an authoritative economy. CSP/SRI reduce specific injection and asset-tampering risks; they do not protect a release if an attacker controls its HTML, hashes or publishing account. Keep publishing and registrar credentials out of client files. No DRM, server-authoritative anti-cheat or guarantee against all attacks is claimed.

## Maintenance and checks

Run from `slop-city-blender/`:

```sh
python3 scripts/test_web_security.py
node scripts/test_web_chunk_security.js
node scripts/test_browser_links.js
python3 scripts/package_web.py Unity/Builds/WebGL ../slop-city-web-v08-upload/dist
```

Package a fresh, unhardened Unity export. The packager applies integrity hashes and CSP **after** launcher mutations. Do not edit the resulting `dist/` scripts/HTML independently of their hashes. Publish through the Sites skill from the canonical checkout, preserving project identity and public audience.

Local browser probes demonstrated blocked injected scripts, JavaScript eval, off-origin connections and an altered SRI script. Real Fetch fixtures rejected altered data chunks and framework bytes. The actual public game loaded and played video on its world TV with no errors; the existing missing Content-Length cache warning and embedded pointer-lock limitation remain. No physical phone testing or destructive public attack/load test was performed.

Evidence is in `Unity/Builds/Reports/web-security-2026-10-12-*.json`, including the publication receipt, actual successful response headers and browser checks. For that initial security-only release, all 208 compared game/media files matched its previous release and retained the dated 91-post / 191-asset snapshot. The later v0.9.9 refresh contains 100 posts / 209 creator assets. Its warmed-cache browser retest loaded and played after a real cached-helper failure was corrected; six Python security checks and real-Fetch cache/integrity regressions passed.

The v0.9.11 in-game public source reader may connect only to `https://api.github.com/repos/hollingsworthd/slop-city/`. It sends no authentication, disables redirects, validates repository-relative paths, caps responses at 2 MiB and text files at 256 KiB, displays plain paginated text, and cancels stale requests. The game page CSP includes this exact API path; other pages retain self-only connections. No GitHub frame or third-party executable script is allowed. The repository remains private until its owner changes it.
