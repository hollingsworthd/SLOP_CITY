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
    ffprobe = shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        raise SystemExit("Browser media optimization needs ffmpeg and ffprobe on PATH.")
    cache = output.parent / "WebMediaCache"
    cache.mkdir(exist_ok=True)
    receipt = []
    for name in ("broadcast.mp4", "in-q-tel.mp4", "duet.mp4"):
        target = output / "StreamingAssets" / "Media" / name
        original = source / name
        digest = hashlib.sha256(original.read_bytes()).hexdigest()
        optimized = cache / (digest + "-480-24-square-v2.mp4")
        if not optimized.is_file():
            temporary = optimized.with_suffix(".tmp.mp4")
            subprocess.run([
                ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", str(original),
                "-map", "0:v:0", "-map", "0:a:0?", "-vf",
                "scale=w='max(2,trunc(min(iw*sar,min(480,480*dar))/2)*2)':h='max(2,trunc(min(ih,min(480,480/dar))/2)*2)',setsar=1,fps=24",
                "-c:v", "libx264", "-refs", "1", "-bf", "0", "-preset", "veryfast", "-crf", "26", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k", "-ac", "2", "-movflags", "+faststart", str(temporary)
            ], check=True)
            temporary.replace(optimized)
        def probe(path):
            return json.loads(subprocess.run([ffprobe, "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)], check=True, capture_output=True, text=True).stdout)
        before, after = probe(original), probe(optimized)
        video = [item for item in after['streams'] if item['codec_type'] == 'video']
        audio_before = [item for item in before['streams'] if item['codec_type'] == 'audio']
        audio_after = [item for item in after['streams'] if item['codec_type'] == 'audio']
        if len(video) != 1 or max(video[0]['width'], video[0]['height']) > 480 or video[0]['sample_aspect_ratio'] != '1:1' or len(audio_before) != len(audio_after) or abs(float(before['format']['duration']) - float(after['format']['duration'])) > .15:
            raise RuntimeError('Browser media profile/duration changed: ' + name)
        subprocess.run([ffmpeg, "-v", "error", "-xerror", "-nostdin", "-threads", "2", "-i", str(optimized), "-f", "null", "-"], check=True)
        if hashlib.sha256(original.read_bytes()).hexdigest() != digest:
            raise RuntimeError('Authored media changed during preparation: ' + name)
        shutil.copyfile(optimized, target)
        receipt.append({"file": name, "sourceSha256": digest,
                        "webSha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                        "webBytes": target.stat().st_size, "maximumDimension": 480, "fps": 24})
    (output / "StreamingAssets" / "Media" / "web-derivatives.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({"browserMedia": receipt}, indent=2))


if __name__ == "__main__":
    main()
