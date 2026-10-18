#!/usr/bin/env python3
"""Inspect packed Web textures without decoding pixels or changing the build.

Requires UnityPy in the invoking Python environment; no project dependency is
installed. Accepts Unity's .data/.data.unityweb or the unwrapped UnityFS bundle.
RGBA estimates are texture storage arithmetic, not measured process memory.
"""
import argparse
from collections import defaultdict
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import struct


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expect-no-dxt", action="store_true")
    parser.add_argument("--allow-dxt-name", action="append", default=[],
                        help="Allow this exact packed DXT texture name (repeatable).")
    args = parser.parse_args()
    try:
        import UnityPy
        from UnityPy.enums import TextureFormat
    except ImportError:
        parser.error("UnityPy must be available in this Python environment.")

    raw = args.input.read_bytes()
    data = gzip.decompress(raw) if raw[:2] == b"\x1f\x8b" else raw
    if data.startswith(b"UnityWebData1.0\0"):
        position = 20
        header_end = struct.unpack_from("<I", data, 16)[0]
        while position < header_end:
            offset, size, length = struct.unpack_from("<III", data, position)
            position += 12
            name = data[position:position + length].decode("utf-8")
            position += length
            if name == "data.unity3d":
                if offset + size > len(data):
                    raise ValueError("Unity bundle extends beyond the data package")
                data = data[offset:offset + size]
                break
        else:
            raise ValueError("Unity data package contains no data.unity3d")

    environment = UnityPy.load(data)
    textures = []
    by_format = defaultdict(lambda: {"count": 0, "storedBytes": 0, "rgba32MipBytes": 0})
    for obj in environment.objects:
        if obj.type.name not in ("Texture2D", "Cubemap"):
            continue
        texture = obj.read()
        width, height = texture.m_Width, texture.m_Height
        rgba_bytes = 0
        for _ in range(texture.m_MipCount):
            rgba_bytes += width * height * 4
            width, height = max(1, width // 2), max(1, height // 2)
        rgba_bytes *= texture.m_ImageCount
        # CompleteImageSize is per face on Cubemap; actual inline/streamed
        # byte lengths account for every face and avoid undercounting it.
        stored_bytes = len(texture.image_data) + texture.m_StreamData.size
        row = {"name": texture.m_Name, "kind": obj.type.name,
               "width": texture.m_Width, "height": texture.m_Height,
               "format": TextureFormat(texture.m_TextureFormat).name,
               "mips": texture.m_MipCount, "images": texture.m_ImageCount,
               "readable": texture.m_IsReadable, "storedBytes": stored_bytes,
               "rgba32MipBytes": rgba_bytes}
        textures.append(row)
        totals = by_format[row["format"]]
        totals["count"] += 1
        totals["storedBytes"] += stored_bytes
        totals["rgba32MipBytes"] += rgba_bytes

    dxt = [row for row in textures if row["format"] in ("DXT1", "DXT5")]
    allowed_names = set(args.allow_dxt_name)
    unexpected = [row for row in dxt if row["name"] not in allowed_names]
    receipt = {"checkedAtUtc": datetime.now(timezone.utc).isoformat(),
               "inputName": args.input.name, "inputSha256": hashlib.sha256(raw).hexdigest(),
               "scope": "Packed scene bundle Texture2D/Cubemap metadata; excludes default resources and runtime textures.",
               "interpretation": "RGBA32 mip bytes estimate one uncompressed representation if a GPU format is unsupported. They do not measure Wasm/GPU residency or establish crash causation.",
               "textureCount": len(textures), "formats": dict(by_format),
               "dxtCount": len(dxt), "dxtStoredBytes": sum(row["storedBytes"] for row in dxt),
               "dxtRgba32MipBytes": sum(row["rgba32MipBytes"] for row in dxt),
               "allowedDxtNames": sorted(allowed_names),
               "presentAllowedDxtNames": sorted({row["name"] for row in dxt if row["name"] in allowed_names}),
               "unexpectedDxtNames": sorted({row["name"] for row in unexpected}),
               "dxtValidationRequested": args.expect_no_dxt,
               "dxtValidationPassed": not unexpected if args.expect_no_dxt else None,
               "textures": sorted(textures, key=lambda row: row["rgba32MipBytes"], reverse=True)}
    args.output.write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({key: value for key, value in receipt.items() if key != "textures"}, indent=2))
    if args.expect_no_dxt and unexpected:
        raise SystemExit("Unexpected packed DXT textures remain")


if __name__ == "__main__":
    main()
