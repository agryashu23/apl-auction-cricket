"""
Packs the crowd frames (crowd_sprites.py) into the game's sprite sheet:

    client-assets/gear/crowd_atlas.png  RGBA 1024x1024: 16 x 8 cells of 64x128,
                                        alpha = coverage
    client-assets/gear/crowd_mask.png   L, same layout: 255 where a team fan's
                                        top / a placard's border and letters /
                                        a flag's cloth take the team colour

Cells: body b's frames at b*8 + (sit, stand, cheer_a, cheer_b, clap_a, clap_b,
wave_a, wave_b); placards (2 cells wide) at 112 "4", 114 "FOUR", 116 "6",
118 "SIX!", 120 "OUT!", 122 "WICKET"; the flag at 124. Must match
StadiumBuilder.Crowd.cs.   python tools/gear/crowd_atlas.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
SRC = ROOT + "build/gear/crowd/"
BODIES = [
    "Male_Adult_01", "Male_Adult_03", "Male_Adult_05", "Male_Adult_08", "Male_Adult_10", "Male_Adult_12", "Male_Adult_15",
    "Male_Adult_17", "Male_Adult_18", "Sports_Male_03", "Female_Adult_01", "Female_Adult_02", "Business_Female_02", "Business_Female_04",
]
FRAMES = ["sit", "stand", "cheer_a", "cheer_b", "clap_a", "clap_b", "wave_a", "wave_b"]
COLS, ROWS, CW, CH = 16, 8, 64, 128
PLACARDS = ["4", "FOUR", "6", "SIX!", "OUT!", "WICKET"]

atlas = Image.new("RGBA", (COLS * CW, ROWS * CH), (20, 20, 24, 0))
mask = Image.new("L", atlas.size, 0)


def at(cell):
    return (cell % COLS) * CW, (cell // COLS) * CH


for b, body in enumerate(BODIES):
    for f, frame in enumerate(FRAMES):
        x, y = at(b * 8 + f)
        col = Image.open(f"{SRC}{body}_{frame}.png").convert("RGBA").resize((CW, CH), Image.LANCZOS)
        m = Image.open(f"{SRC}{body}_{frame}_mask.png").convert("L").resize((CW, CH), Image.LANCZOS)
        # Dark under the transparent pixels so mips and filtering don't halo.
        under = Image.new("RGBA", col.size, (20, 20, 24, 0))
        under.alpha_composite(col)
        atlas.paste(under, (x, y))
        a = col.split()[3]
        mask.paste(Image.composite(m, Image.new("L", m.size, 0), a), (x, y))

font_path = "C:/Windows/Fonts/arialbd.ttf"
for i, text in enumerate(PLACARDS):
    x, y = at(112 + 2 * i)
    card = (x + 6, y + 30, x + 122, y + 96)
    d, dm = ImageDraw.Draw(atlas), ImageDraw.Draw(mask)
    d.rectangle(card, fill=(250, 250, 246, 255))
    dm.rectangle(card, outline=255, width=5)
    size = 52 if len(text) <= 2 else 30 if len(text) <= 4 else 22
    font = ImageFont.truetype(font_path, size) if os.path.exists(font_path) else ImageFont.load_default()
    box = d.textbbox((0, 0), text, font=font)
    tx = x + 64 - (box[2] - box[0]) // 2 - box[0]
    ty = y + 63 - (box[3] - box[1]) // 2 - box[1]
    d.text((tx, ty), text, fill=(235, 235, 235, 255), font=font)
    dm.text((tx, ty), text, fill=255, font=font)
    # Hands holding the card's bottom corners.
    for hx in (x + 22, x + 106):
        d.ellipse((hx - 6, y + 92, hx + 6, y + 104), fill=(196, 150, 118, 255))

# The flag: pole on the left, the cloth in the team colour with a white band.
x, y = at(124)
d, dm = ImageDraw.Draw(atlas), ImageDraw.Draw(mask)
d.rectangle((x, y, x + 3, y + 127), fill=(150, 150, 156, 255))
d.rectangle((x + 4, y + 8, x + 63, y + 56), fill=(236, 236, 236, 255))
dm.rectangle((x + 4, y + 8, x + 63, y + 56), fill=255)
dm.rectangle((x + 4, y + 26, x + 63, y + 38), fill=0)

os.makedirs(ROOT + "client-assets/gear", exist_ok=True)
atlas.save(ROOT + "client-assets/gear/crowd_atlas.png")
mask.save(ROOT + "client-assets/gear/crowd_mask.png")
preview = Image.new("RGBA", atlas.size, (60, 90, 60, 255))
preview.alpha_composite(atlas)
preview.save(ROOT + "build/gear/crowd_atlas_preview.png")
print("ATLAS", atlas.size)
