"""
Tile the shot gallery (unity_bootstrap.ps1 -PlayTest -ShotGallery) into one
sheet per clip: the three moments (before contact, contact, after) across,
the two cameras (off side, bowler's end) down, each cropped to the batter.

    python tools/motions/gallery_sheet.py build/unity/gallery
"""
import collections
import os
import re
import sys

from PIL import Image, ImageDraw

src = sys.argv[1]
out = os.path.join(src, "sheets")
os.makedirs(out, exist_ok=True)
groups = collections.defaultdict(dict)
for f in os.listdir(src):
    m = re.match(r"^\d+_(.+)_t(\d)_(side|front)\.png$", f)
    if m:
        groups[m.group(1)][(m.group(3), int(m.group(2)))] = os.path.join(src, f)

CAMS = ["side", "front"]
W, H = 440, 360
for clip, shots in sorted(groups.items()):
    times = sorted({k[1] for k in shots})
    sheet = Image.new("RGB", (W * len(times), H * len(CAMS) + 24), "white")
    draw = ImageDraw.Draw(sheet)
    draw.text((6, 5), f"{clip}  (t0 = 0.3 s before contact, t1 = contact, t2 = 0.3 s after; top: off side, bottom: bowler's end)", fill="black")
    for ci, cam in enumerate(CAMS):
        for ti, t in enumerate(times):
            path = shots.get((cam, t))
            if not path:
                continue
            im = Image.open(path).convert("RGB")
            w, h = im.size
            # The batter fills the middle of the frame: keep the central 55% x 80%.
            box = (int(w * 0.225), int(h * 0.1), int(w * 0.775), int(h * 0.9))
            im = im.crop(box).resize((W, H))
            sheet.paste(im, (ti * W, 24 + ci * H))
            draw.text((ti * W + 6, 28 + ci * H), f"{cam} t{t}", fill="white")
    sheet.save(os.path.join(out, clip + ".png"))
print(f"{len(groups)} clip sheets in {out}")
