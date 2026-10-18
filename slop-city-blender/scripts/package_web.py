#!/usr/bin/env python3
"""Package a completed Slop City Unity export for hosts with a 25 MiB file limit.

Usage: python3 scripts/package_web.py Unity/Builds/WebGL ../slop-city-web-v07/dist
The Unity export is untouched. Only an empty or previously managed destination
can be replaced; adjacent Site metadata such as .openai and .git is untouched.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile
from harden_web import check_contents, check_input, content_url, harden
from release_wasm_binary import release_framework_binary


MIB = 1024 * 1024
MAX_FILE = 24 * MIB
MAX_PACKAGE = 248 * MIB
PART_SIZE = 16 * MIB
MARKER = "slop-city-package.json"
ADAPTER = "slop-city-chunks.js"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(MIB), b""):
            digest.update(block)
    return digest.hexdigest()


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError(f"Expected exactly one launcher hook: {old!r}")
    return text.replace(old, new, 1)


def patch_launcher(html):
    html = replace_once(html, "  <script>\n", f'  <script src="{ADAPTER}"></script>\n  <script>\n')
    html = replace_once(html, "    const config = {\n", "    const config = {\n      cacheControl: function (url) { return window.slopCityChunks.cacheControl(url, config.dataUrl); },\n")
    html = replace_once(html, "        createUnityInstance(canvas, config, function (progress) {", "        if (!window.slopCityChunks) {\n          fail(\"The city files could not be loaded. Reload to try again.\", \"Download helper missing.\");\n          return;\n        }\n        window.slopCityChunks.install(function (error) {\n          fail(\"The city download was interrupted. Reload to reconnect.\", error);\n        });\n        createUnityInstance(canvas, config, function (progress) {")
    return replace_once(html, "        }).then(function (instance) {\n", "        }).then(function (instance) {\n          try { window.slopCityChunks.restore(); } catch (error) { console.warn(\"City download cleanup failed.\", error); }\n")


def package(source, destination):
    if destination.is_symlink():
        raise ValueError("The destination must not be a symlink.")
    source, destination = source.resolve(), destination.resolve()
    if not source.is_dir() or not (source / "index.html").is_file():
        raise ValueError("The source must be a completed Unity export containing index.html.")
    if source == destination or source in destination.parents or destination in source.parents:
        raise ValueError("Source and destination must be separate, non-nested directories.")
    if destination.exists():
        if not destination.is_dir():
            raise ValueError("The destination exists and is not a directory.")
        if any((destination / name).exists() for name in (".git", ".openai")):
            raise ValueError("Point at the Site's dist directory, not the Site project root.")
        if any(destination.iterdir()):
            try:
                previous = json.loads((destination / MARKER).read_text())
            except (OSError, ValueError) as error:
                raise ValueError("Refusing to replace a nonempty destination not managed by this packager.") from error
            if previous.get("generator") != "slop-city-web-packager-v1":
                raise ValueError("The destination package marker is not recognized.")

    html = (source / "index.html").read_text()
    if "SLOP CITY" not in html or "createUnityInstance(canvas, config," not in html:
        raise ValueError("Expected the Slop City WebGL launcher.")
    fetch_files = set(re.findall(r'(?:dataUrl|frameworkUrl|codeUrl|symbolsUrl|workerUrl|memoryUrl):\s*"([^"]+)"', html))
    framework_files = set(re.findall(r'frameworkUrl:\s*"([^"]+)"', html))
    inputs = sorted(path for path in source.rglob("*") if path.is_file())
    if any(path.is_symlink() for path in source.rglob("*")):
        raise ValueError("Symlinks are not supported in the exported webpage.")
    reserved = {MARKER, ADAPTER, "chunk-manifest.json"}
    if any(path.relative_to(source).as_posix() in reserved for path in inputs):
        raise ValueError("The source already contains packager-generated files.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{destination.name}-packaging-", dir=destination.parent))
    manifest = {"version": 2, "files": {}, "integrity": {}, "sizes": {}, "gzipSizes": {}}
    source_hashes = {}
    backup = None
    try:
        for path in inputs:
            relative = path.relative_to(source).as_posix()
            check_input(relative)
            check_contents(path)
            size = path.stat().st_size
            digest = sha256(path)
            source_hashes[relative] = {"size": size, "sha256": digest}
            target = staging / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            packaged_input = path
            if relative in framework_files:
                # Preserve the raw export and its receipt. Transform only staging,
                # before delivered content hashes, chunk names and script integrity.
                original = path.read_bytes()
                if len(original) != size or hashlib.sha256(original).hexdigest() != digest:
                    raise ValueError(f"Source changed during packaging: {relative}")
                target.write_bytes(release_framework_binary(original))
                packaged_input = target
                size, digest = target.stat().st_size, sha256(target)
            if relative in fetch_files:
                manifest["integrity"][relative] = digest
                manifest["sizes"][relative] = size
                # Unity's fallback inflater first holds every inflated chunk and
                # then copies them into a second whole-file buffer. Let browsers
                # with a native streaming inflater bypass that startup peak.
                with packaged_input.open("rb") as stream:
                    header = stream.read(256)
                    if header.startswith(b'\x1f\x8b') and b'UnityWeb Compressed Content (gzip)\0' in header:
                        stream.seek(-4, os.SEEK_END)
                        unpacked_size = struct.unpack('<I', stream.read(4))[0]
                        if not 0 < unpacked_size <= 1024 * MIB:
                            raise ValueError(f"Unsupported gzip output size: {relative}")
                        manifest["gzipSizes"][relative] = unpacked_size
            if size <= MAX_FILE:
                if packaged_input != target:
                    shutil.copy2(packaged_input, target)
                if target.stat().st_size != size or sha256(target) != digest:
                    raise ValueError(f"Copy verification failed: {relative}")
                continue
            if relative not in fetch_files or not relative.endswith(".unityweb"):
                raise ValueError(f"Oversized file is not a supported Unity fetch resource: {relative}")
            parts = []
            reconstructed = hashlib.sha256()
            reconstructed_size = 0
            with packaged_input.open("rb") as stream:
                for index, block in enumerate(iter(lambda: stream.read(PART_SIZE), b"")):
                    part_relative = f"{relative}.{digest[:16]}.part{index:04d}"
                    part_path = staging / part_relative
                    part_path.write_bytes(block)
                    part_digest = hashlib.sha256(block).hexdigest()
                    if sha256(part_path) != part_digest or part_path.stat().st_size != len(block):
                        raise ValueError(f"Chunk verification failed: {part_relative}")
                    reconstructed.update(block)
                    reconstructed_size += len(block)
                    parts.append({"path": part_relative, "size": len(block), "sha256": part_digest})
            if reconstructed_size != size or reconstructed.hexdigest() != digest:
                raise ValueError(f"Source changed during packaging: {relative}")
            manifest["files"][relative] = {"size": size, "sha256": digest, "parts": parts}
            if packaged_input == target:
                target.unlink()

        if fetch_files:
            html = patch_launcher(html)
            adapter = Path(__file__).with_name("web_chunk_fetch.js").read_text()
            adapter = replace_once(adapter, "/* SLOP_CITY_CHUNK_MANIFEST */", json.dumps(manifest, separators=(",", ":")))
            (staging / ADAPTER).write_text(adapter)
        # Unity's cache key must change with the compiled content, just like SRI.
        # Chunk manifest lookup uses the path; its Fetch requests retain this query.
        def version_asset(match):
            relative = match.group(2)
            return match.group(1) + '"' + content_url(relative, manifest['integrity'][relative]) + '"'
        html = re.sub(r'((?:dataUrl|frameworkUrl|codeUrl|symbolsUrl|workerUrl|memoryUrl):\s*)"([^"]+)"', version_asset, html)
        (staging / "index.html").write_text(html)
        (staging / "chunk-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        harden(staging, Path(__file__).resolve().parents[1] / "Unity/Assets/WebGLTemplates/SlopCity")
        outputs = {}
        for path in sorted(staging.rglob("*")):
            if path.is_file():
                size = path.stat().st_size
                if size > MAX_FILE:
                    raise ValueError(f"Packaged asset exceeds {MAX_FILE} bytes: {path.name}")
                outputs[path.relative_to(staging).as_posix()] = {"size": size, "sha256": sha256(path)}
        project_root = Path(__file__).resolve().parents[1]
        source_label = source.relative_to(project_root).as_posix() if source.is_relative_to(project_root) else source.name
        receipt = {
            "generator": "slop-city-web-packager-v1", "source": source_label,
            "maximumFileBytes": MAX_FILE, "chunkBytes": PART_SIZE,
            "maximumPackageBytes": MAX_PACKAGE,
            "sourceFiles": source_hashes, "packagedFiles": outputs,
            "sourceBytes": sum(value["size"] for value in source_hashes.values()),
            "packagedBytes": sum(value["size"] for value in outputs.values()),
            "chunkedFiles": len(manifest["files"]),
        }
        (staging / MARKER).write_text(json.dumps(receipt, indent=2) + "\n")
        if (staging / MARKER).stat().st_size > MAX_FILE:
            raise ValueError("Package receipt exceeds the host file limit.")
        # Include every final asset and generated metadata, including this receipt,
        # before moving aside a previously usable destination.
        package_bytes = sum(path.stat().st_size for path in staging.rglob("*") if path.is_file())
        if package_bytes > MAX_PACKAGE:
            raise ValueError(f"Package exceeds the {MAX_PACKAGE}-byte upload budget: {package_bytes} bytes.")
        if destination.exists():
            backup = Path(tempfile.mkdtemp(prefix=f".{destination.name}-previous-", dir=destination.parent))
            backup.rmdir()
            os.replace(destination, backup)
        try:
            os.replace(staging, destination)
        except BaseException:
            if backup:
                os.replace(backup, destination)
                backup = None
            raise
        if backup:
            shutil.rmtree(backup)
            backup = None
        print(json.dumps({"destination": str(destination), "files": len(outputs) + 1, "chunkedFiles": len(manifest["files"]), "bytes": receipt["packagedBytes"], "largestAssetBytes": max(value["size"] for value in outputs.values())}, indent=2))
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Completed Unity WebGL export directory")
    parser.add_argument("destination", type=Path, help="Empty or previously packaged Site dist directory")
    args = parser.parse_args()
    try:
        package(args.source, args.destination)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Packaging failed: {error}\n")


if __name__ == "__main__":
    main()
