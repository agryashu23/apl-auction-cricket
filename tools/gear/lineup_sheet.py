"""Tiles build/gear/lineup/*.png (lineup.py) into lineup_sheet.png: per body, head front, head 3/4, full length, labelled."""
import json
from PIL import Image, ImageDraw
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
names = [e["model"] for e in json.load(open(ROOT + "client/Assets/Resources/Data/kit_cast.json"))["cast"]]
cols = 6
cw, ch = 300, 300 + 300 + 400 + 24
sheet = Image.new("RGB", (cols * cw, ((len(names) + cols - 1) // cols) * ch), (40, 40, 44))
d = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    x, y = (i % cols) * cw, (i // cols) * ch
    d.text((x + 6, y + 4), f"{i:02d} {n}", fill=(255, 255, 0))
    for k, tag in enumerate(("head", "head34")):
        sheet.paste(Image.open(f"{ROOT}build/gear/lineup/{n}_{tag}.png").convert("RGB"), (x, y + 24 + k * 300))
    sheet.paste(Image.open(f"{ROOT}build/gear/lineup/{n}_body.png").convert("RGB"), (x + 50, y + 624))
sheet.save(ROOT + "build/gear/lineup_sheet.png")
# Small version of just the heads for quick review.
heads = Image.new("RGB", (11 * 200, 2 * 220), (40, 40, 44))
dh = ImageDraw.Draw(heads)
for i, n in enumerate(names):
    im = Image.open(f"{ROOT}build/gear/lineup/{n}_head34.png").convert("RGB").resize((200, 200))
    heads.paste(im, ((i % 11) * 200, (i // 11) * 220 + 20))
    dh.text(((i % 11) * 200 + 4, (i // 11) * 220 + 4), f"{i:02d} {n}", fill=(255, 255, 0))
heads.save(ROOT + "build/gear/lineup_heads.png")
# The bodies the game still uses (CricketCast.Dropped left out), heads and full length.
DROPPED = {"Male_Adult_02", "Male_Adult_09", "Sports_Male_03", "Wood_Male_01", "Male_Adult_17", "Male_Adult_18"}
kept = [n for n in names if n not in DROPPED]
sheet2 = Image.new("RGB", (len(kept) * 200, 220 + 400), (40, 40, 44))
d2 = ImageDraw.Draw(sheet2)
for i, n in enumerate(kept):
    sheet2.paste(Image.open(f"{ROOT}build/gear/lineup/{n}_head34.png").convert("RGB").resize((200, 200)), (i * 200, 20))
    sheet2.paste(Image.open(f"{ROOT}build/gear/lineup/{n}_body.png").convert("RGB"), (i * 200, 220))
    d2.text((i * 200 + 4, 4), n, fill=(255, 255, 0))
sheet2.save(ROOT + "build/gear/lineup_kept.png")
