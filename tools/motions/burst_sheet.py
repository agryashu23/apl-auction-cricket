"""
Tile the burst (unity_bootstrap.ps1 -PlayTest -Burst) into one sheet per ball:
every frame of the delivery in order (0.03 s of game time apart), cropped to
the batter, numbered — how the stance, the shape-up and the shot join up.

    python tools/motions/burst_sheet.py build/unity/burst
"""
import collections
import os
import re
import sys

from PIL import Image, ImageDraw

src = sys.argv[1]
out = os.path.join(src, "sheets")
os.makedirs(out, exist_ok=True)
balls = collections.defaultdict(list)
for f in os.listdir(src):
    m = re.match(r"^ball(\d+)_f(\d+)\.png$", f)
    if m:
        balls[int(m.group(1))].append((int(m.group(2)), os.path.join(src, f)))

W, H, COLS = 300, 300, 8
for ball, frames in sorted(balls.items()):
    frames.sort()
    rows = (len(frames) + COLS - 1) // COLS
    sheet = Image.new("RGB", (W * COLS, H * rows + 22), "white")
    d = ImageDraw.Draw(sheet)
    d.text((6, 4), f"ball {ball}: {len(frames)} frames, 0.03 s of game time apart (1/5 speed, a frame every 0.15 s)", fill="black")
    for i, (n, path) in enumerate(frames):
        im = Image.open(path).convert("RGB")
        w, h = im.size
        # The batter's close-up: he stands right of centre; keep a square around him.
        box = (int(w * 0.38), int(h * 0.12), int(w * 0.38) + int(h * 0.8), int(h * 0.92))
        im = im.crop(box).resize((W, H))
        sheet.paste(im, ((i % COLS) * W, 22 + (i // COLS) * H))
        d.text(((i % COLS) * W + 4, 24 + (i // COLS) * H), f"{n}", fill="white")
    sheet.save(os.path.join(out, f"ball{ball:02d}.png"))
print(f"{len(balls)} ball sheets in {out}")
