"""
Draw a contact sheet of a clip from dump_pose.py's JSON: stick figures at
evenly spaced frames (plus any frames asked for), three rows — side view
(batter's off side to the right; x across, z up), front view (y across,
z up) and top view (x across, y up) — left limbs red, right limbs blue,
a green ground line. Same idea as the packs' preview PNGs, so a reviewer
can judge a pose the way the pack author did.

    python tools/motions/sheet.py <pose.json> <out.png> [--frames 1,31,60] [--count 10] [--follow]

--follow keeps the figure centred on its hips (for clips that travel).
"""
import argparse
import json
import math

from PIL import Image, ImageDraw

ap = argparse.ArgumentParser()
ap.add_argument("pose")
ap.add_argument("out")
ap.add_argument("--frames", default="", help="1-based frame numbers to include (as the packs count them)")
ap.add_argument("--count", type=int, default=10)
ap.add_argument("--follow", action="store_true")
ap.add_argument("--scale", type=float, default=70.0, help="pixels per metre")
a = ap.parse_args()

d = json.load(open(a.pose))
frames = d["frames"]
if not frames:
    raise SystemExit("no frames")
by_index = {fr["frame"]: fr for fr in frames}
first, last = d["first"], d["last"]
# The packs count frames from 1 at the clip's first exported frame.
want = {first + (int(x) - 1) for x in a.frames.split(",") if x.strip()}
n = max(2, a.count)
for i in range(n):
    want.add(first + round(i * (last - first) / (n - 1)))
chosen = [by_index[f] for f in sorted(want) if f in by_index]

CELL_W, CELL_H = 200, 240
S = a.scale
views = [("side", lambda p: (p[0], p[2])), ("front", lambda p: (p[1], p[2])), ("top", lambda p: (p[0], p[1]))]
img = Image.new("RGB", (CELL_W * len(chosen), CELL_H * len(views) + 24), "white")
dr = ImageDraw.Draw(img)
dr.text((6, 4), f"{d['file']}  frames {first}..{last} ({len(frames)} keyed), {d['fps']} fps", fill="black")


def colour(name):
    n = name.lower()
    if "left" in n:
        return (220, 40, 40)
    if "right" in n:
        return (40, 60, 220)
    return (20, 20, 20)


for ci, fr in enumerate(chosen):
    bones = fr["bones"]
    hips = next((v[0] for k, v in bones.items() if k.endswith("Hips")), [0, 0, 0])
    for vi, (vname, proj) in enumerate(views):
        ox, oy = ci * CELL_W, 24 + vi * CELL_H
        dr.rectangle([ox, oy, ox + CELL_W - 1, oy + CELL_H - 1], outline=(200, 200, 200))
        # centre: the hips (x/y) if following, else the origin; ground at the bottom for side/front
        cx = hips[0] if (a.follow or vname == "front") else 0.0
        cy = hips[1] if (a.follow or vname == "side") else 0.0
        if vname == "top":
            def to_px(p, cx=cx, cy=cy, ox=ox, oy=oy):
                return (ox + CELL_W / 2 + (p[0] - cx) * S, oy + CELL_H / 2 - (p[1] - cy) * S)
        else:
            def to_px(p, cx=cx, cy=cy, ox=ox, oy=oy, proj=proj):
                u, v = proj(p)
                c = cx if vname == "side" else cy
                return (ox + CELL_W / 2 + (u - c) * S, oy + CELL_H - 30 - v * S)
            dr.line([(ox + 4, oy + CELL_H - 30), (ox + CELL_W - 4, oy + CELL_H - 30)], fill=(60, 170, 60), width=1)
        for name, (h, t) in bones.items():
            if name.endswith("_end") or name.endswith("_End"):
                continue
            dr.line([to_px(h), to_px(t)], fill=colour(name), width=2)
        for prop in d.get("props", []):
            pts = [to_px(c) for c in prop["corners"]]
            xs, ys = [p[0] for p in pts], [p[1] for p in pts]
            if ci == 0:
                dr.rectangle([min(xs), min(ys), max(xs), max(ys)], outline=(160, 110, 40))
        dr.text((ox + 6, oy + 4), f"{vname} f{fr['frame'] - first + 1}", fill="black")
img.save(a.out)
print(f"sheet {a.out}: {len(chosen)} frames x {len(views)} views")
