#!/usr/bin/env python3
"""Resize only exported browser media; authored/other-platform clips are untouched."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys


def main():
    output = Path(sys.argv[1]).resolve()
    source = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else Path(__file__).resolve().parents[1] / "Unity" / "Assets" / "StreamingAssets" / "Media"
    if not (output / "index.html").is_file():
        raise SystemExit("Expected a completed browser export.")
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise SystemExit("Browser media optimization needs ffmpeg on PATH.")
    cache = output.parent / "WebMediaCache"
    cache.mkdir(exist_ok=True)
    receipt = []
    for name in ("broadcast.mp4", "in-q-tel.mp4", "duet.mp4"):
        target = output / "StreamingAssets" / "Media" / name
        original = source / name
        digest = hashlib.sha256(original.read_bytes()).hexdigest()
        optimized = cache / (digest + "-640-24-v1.mp4")
        if not optimized.is_file():
            temporary = optimized.with_suffix(".tmp.mp4")
            subprocess.run([
                ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", str(original),
                "-map", "0:v:0", "-map", "0:a:0?", "-vf",
                "scale=640:640:force_original_aspect_ratio=decrease:force_divisible_by=2,fps=24",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "26", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k", "-ac", "2", "-movflags", "+faststart", str(temporary)
            ], check=True)
            temporary.replace(optimized)
        shutil.copyfile(optimized, target)
        receipt.append({"file": name, "sourceSha256": digest,
                        "webSha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                        "webBytes": target.stat().st_size, "maximumDimension": 640, "fps": 24})
    (output / "StreamingAssets" / "Media" / "web-derivatives.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({"browserMedia": receipt}, indent=2))


if __name__ == "__main__":
    main()
