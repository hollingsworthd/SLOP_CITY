#!/usr/bin/env python3
"""Reproduce/check the small OFL branding font. Requires FontTools 4.60.1."""
import argparse
import hashlib
import io
from pathlib import Path

from fontTools import subset, version
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Unity/Assets/Art/Fonts/NotoSansCJKjp-Bold.otf"
DESTINATION = ROOT / "Unity/Assets/Resources/Fonts/SlopCityBrandSans-Bold.otf"
SOURCE_SHA256 = "e53dcb0dcb2922e45d01aae1ebd2f382bb81d4229b18b6b883bd170678af1f76"


def generate():
    if version != "4.60.1":
        raise SystemExit("Use FontTools 4.60.1 for reproducible output.")
    if hashlib.sha256(SOURCE.read_bytes()).hexdigest() != SOURCE_SHA256:
        raise SystemExit("Authored font source differs from its recorded provenance.")
    font = TTFont(SOURCE, recalcTimestamp=False)
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.name_languages = ["*"]
    subsetter = subset.Subsetter(options=options)
    # Basic Latin, Latin-1, general punctuation, and the actual trademark glyph.
    subsetter.populate(unicodes=list(range(0x20, 0x100)) + list(range(0x2000, 0x2070)) + [0x2122])
    subsetter.subset(font)
    names = {1: "Slop City Brand Sans", 3: "Slop City Brand Sans Bold 1.0",
             4: "Slop City Brand Sans Bold", 6: "SlopCityBrandSans-Bold",
             16: "Slop City Brand Sans", 17: "Bold"}
    for entry in font["name"].names:
        if entry.nameID in names:
            entry.string = names[entry.nameID].encode(entry.getEncoding())
    font["CFF "].cff.fontNames = ["SlopCityBrandSans-Bold"]
    top = font["CFF "].cff.topDictIndex[0]
    top.FamilyName = "Slop City Brand Sans"
    top.FullName = "Slop City Brand Sans Bold"
    output = io.BytesIO()
    font.save(output)
    data = output.getvalue()
    generated = TTFont(io.BytesIO(data))
    cmap = generated.getBestCmap()
    for char in "SLOP CITY™SLOPCITY.org© 2026 Machine Publishers, LLC":
        if ord(char) not in cmap:
            raise SystemExit(f"Missing branding glyph U+{ord(char):04X}.")
    # Reader pages carrying the trademark use this font too; retain all glyphs
    # needed by the bundled notices rather than hiding missing characters.
    for notice in (DESTINATION.parent.parent / "Licensing").glob("*.txt"):
        for char in notice.read_text():
            if not char.isspace() and ord(char) not in cmap:
                raise SystemExit(f"Unsupported notice character U+{ord(char):04X}: {notice.name}.")
    trademark = generated["CFF "].cff.topDictIndex[0].CharStrings[cmap[0x2122]]
    trademark.decompile()
    if not trademark.program or len(data) > 32_000:
        raise SystemExit("Brand font glyph data or size budget failed.")
    return data


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Validate reproducibility without writes.")
    args = parser.parse_args()
    data = generate()
    if args.check:
        if DESTINATION.read_bytes() != data:
            raise SystemExit("Bundled font differs from reproducible source subset.")
    else:
        DESTINATION.parent.mkdir(parents=True, exist_ok=True)
        DESTINATION.write_bytes(data)
    print(f"Brand font {'PASS' if args.check else 'written'}: {len(data)} bytes; SHA-256 {hashlib.sha256(data).hexdigest()}")
