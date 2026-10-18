"""Deterministic Japanese storefront atlas, baked from bundled SIL OFL Noto CJK.

Run with Python and Pillow. Font provenance/license live in ../Fonts.
Only text and fixture graphics are emitted; no runtime system-font requirement.
"""
from pathlib import Path
import hashlib
import json
import random
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
FONT = HERE.parent / "Fonts/NotoSansCJKjp-Bold.otf"
LABELS = [
    ("夜市", "NIGHT MARKET"), ("深夜食堂", "LATE NIGHT DINER"),
    ("電脳商店", "CYBER STORE"), ("機械出版", "MACHINE PUBLISHERS"),
    ("未来薬局", "FUTURE PHARMACY"), ("修理工房", "REPAIRS"),
    ("中央線", "CENTRAL LINE"), ("地下鉄", "SUBWAY"),
    ("都市案内", "CITY INFORMATION"), ("喫茶", "COFFEE"),
    ("ラーメン", "RAMEN"), ("カラオケ", "KARAOKE"),
    ("夢境ホテル", "DREAM HOTEL"), ("古書", "BOOKS"),
    ("中古家電", "USED ELECTRONICS"), ("東京通信", "TOKYO SIGNAL"),
    ("夜間診療", "NIGHT CLINIC"), ("無人酒店", "AUTOMATED LIQUOR"),
    ("両替", "EXCHANGE"), ("ゲーム", "CITY ARCADE"),
    ("携帯修理", "PHONE REPAIR"), ("監視区域", "SURVEILLANCE AREA"),
    ("燃料", "FUEL"), ("居住区", "RESIDENTIAL"),
    ("屋上庭園", "ROOF GARDEN"), ("電器店", "ELECTRONICS"),
    ("写真館", "PHOTO STUDIO"), ("映画館", "CINEMA"),
    ("駅前市場", "STATION MARKET"), ("新宿放送", "SHINJUKU BROADCAST"),
    ("防災センター", "CIVIL DEFENCE"), ("営業中", "OPEN"),
]


def font(size):
    return ImageFont.truetype(str(FONT), size)


def centered_text(draw, center, text, face, fill):
    bounds = draw.textbbox((0, 0), text, font=face)
    draw.text((center[0] - (bounds[2] - bounds[0]) / 2 - bounds[0],
               center[1] - (bounds[3] - bounds[1]) / 2 - bounds[1]),
              text, font=face, fill=fill)


def create():
    rng = random.Random(1984)
    albedo = Image.new("RGB", (4096, 4096), (12, 12, 12))
    emission = Image.new("RGB", albedo.size, (0, 0, 0))
    entries = []
    missing = []
    face = font(96)
    tofu = bytes(face.getmask(chr(0x10FFFF)))
    for char in sorted(set("".join(label for label, _ in LABELS))):
        if bytes(face.getmask(char)) == tofu:
            missing.append(char)
    if missing:
        raise RuntimeError("Font is missing label glyphs: " + "".join(missing))
    for orientation in ("horizontal", "vertical"):
        for index, (jp, english) in enumerate(LABELS):
            vertical = orientation == "vertical"
            width, height = (256, 1024) if vertical else (1024, 256)
            x = (index % 16) * width if vertical else (index % 4) * width
            y = 2048 + (index // 16) * height if vertical else (index // 4) * height
            panel = Image.new("RGB", (width, height), (18, 18, 18))
            mask = Image.new("RGB", panel.size, (0, 0, 0))
            paint, light = ImageDraw.Draw(panel), ImageDraw.Draw(mask)
            # A powder-coated face has small discontinuities instead of pristine solid blocks.
            for scratch in range(180):
                sx, sy = rng.randrange(width), rng.randrange(height)
                gray = rng.randint(20, 34)
                paint.line((sx, sy, min(width - 1, sx + rng.randint(1, 8)), sy), fill=(gray,) * 3)
            paint.rounded_rectangle((10, 10, width - 11, height - 11), radius=10, outline=(65, 65, 65), width=3)
            light.line((19, 19, width - 20, 19), fill=(88, 88, 88), width=3)
            light.line((19, height - 20, width - 20, height - 20), fill=(65, 65, 65), width=2)
            if vertical:
                char_size = min(180, 790 // len(jp))
                step = min(192, 800 / len(jp))
                start = height * .49 - (len(jp) - 1) * step / 2
                for ci, char in enumerate(jp):
                    if char == "ー":
                        # Tate-gaki requires a vertical prolonged sound mark.
                        glyph = Image.new("L", (220, 220), 0)
                        centered_text(ImageDraw.Draw(glyph), (110, 110), char, font(char_size), 255)
                        glyph = glyph.rotate(90, resample=Image.Resampling.BICUBIC)
                        gx, gy = int(width / 2 - 110), int(start + ci * step - 110)
                        panel.paste((234, 234, 234), (gx, gy), glyph)
                        mask.paste((220, 220, 220), (gx, gy), glyph)
                    else:
                        centered_text(paint, (width / 2, start + ci * step), char, font(char_size), (234,) * 3)
                        centered_text(light, (width / 2, start + ci * step), char, font(char_size), (220,) * 3)
                centered_text(paint, (width / 2, 952), f"24H  /  {index + 1:02d}", font(23), (122,) * 3)
                centered_text(light, (width / 2, 952), f"24H  /  {index + 1:02d}", font(23), (58,) * 3)
            else:
                size = min(157, 880 // len(jp))
                centered_text(paint, (512, 104), jp, font(size), (234,) * 3)
                centered_text(light, (512, 104), jp, font(size), (220,) * 3)
                centered_text(paint, (512, 212), english + "  /  24H", font(30), (148,) * 3)
                centered_text(light, (512, 212), english + "  /  24H", font(30), (81,) * 3)
                for bolt_x in (24, width - 25):
                    for bolt_y in (28, height - 29):
                        paint.ellipse((bolt_x - 3, bolt_y - 3, bolt_x + 3, bolt_y + 3), fill=(108,) * 3)
            albedo.paste(panel, (x, y))
            emission.paste(mask, (x, y))
            # Unity UVs originate at the bottom; half-pixel insets prevent atlas bleed.
            entries.append({"id": index + (32 if vertical else 0), "label": jp,
                            "english": english, "orientation": orientation,
                            "rectPixelsTopLeft": [x, y, width, height],
                            "uv": [(x + 2) / 4096, (4096 - y - height + 2) / 4096,
                                   (width - 4) / 4096, (height - 4) / 4096]})
    albedo.save(HERE / "JapanKanban-Albedo.png", optimize=True)
    emission.save(HERE / "JapanKanban-Emission.png", optimize=True)
    manifest = {"schema": 1, "font": "../Fonts/NotoSansCJKjp-Bold.otf", "license": "SIL OFL 1.1",
                "fontSha256": hashlib.sha256(FONT.read_bytes()).hexdigest(),
                "atlasSize": [4096, 4096], "albedo": "JapanKanban-Albedo.png",
                "emission": "JapanKanban-Emission.png", "missingGlyphs": missing,
                "labels": [jp for jp, _ in LABELS], "entries": entries,
                "note": "Baked Japanese glyphs; vertical prolonged sound marks rotated for tategaki."}
    (HERE / "sign-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    # Contact sheet is a review aid; import disabled by its location outside Unity Assets.
    preview = Image.new("RGB", (1536, 1152), (18, 21, 26))
    for i in range(12):
        crop = albedo.crop((i % 4 * 1024, i // 4 * 256, i % 4 * 1024 + 1024, i // 4 * 256 + 256))
        preview.paste(crop.resize((512, 128)), ((i % 3) * 512, (i // 3) * 128))
    for i in range(8):
        crop = albedo.crop((i * 256, 2048, i * 256 + 256, 3072))
        preview.paste(crop.resize((144, 576)), (40 + i * 182, 552))
    review_path = HERE.parents[3] / "renders/Japan-sign-atlas-review.png"
    review_path.parent.mkdir(parents=True, exist_ok=True)
    preview.save(review_path)
    print(f"Created {len(entries)} label tiles; missing glyphs={missing}; preview={review_path}")


if __name__ == "__main__":
    create()
