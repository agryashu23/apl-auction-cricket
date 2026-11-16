"""Tiles build/gear/lineup/<name>_{head34,body}.png for the names given into build/gear/<out>.png (11 per row).

    python tools/gear/candidates_sheet.py lineup_candidates Male_Adult_15 Male_Adult_19 ...
"""
import sys

from PIL import Image, ImageDraw

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
R = _APL_REPO + "/build/gear/"
out, names = sys.argv[1], sys.argv[2:]
per = 11
rows = (len(names) + per - 1) // per
sheet = Image.new("RGB", (min(per, len(names)) * 200, rows * 620), (40, 40, 44))
d = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    x, y = (i % per) * 200, (i // per) * 620
    sheet.paste(Image.open(f"{R}lineup/{n}_head34.png").convert("RGB").resize((200, 200)), (x, y + 20))
    sheet.paste(Image.open(f"{R}lineup/{n}_body.png").convert("RGB"), (x, y + 220))
    d.text((x + 4, y + 4), n, fill=(255, 255, 0))
sheet.save(f"{R}{out}.png")
print("SHEET", f"{R}{out}.png")
