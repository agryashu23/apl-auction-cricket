"""
Contact sheet with the BAT drawn in (brown, handle darker), from dump_pose.py's JSON plus a bat track
(ceremony_common's <clip>.battrack.json, or a clip of bat_tracks.json): side / front / top like sheet.py,
left limbs red, right blue, ground green. Use it to check the bat sits in the hands and clear of the body.

    python tools/motions/bat_sheet.py <pose.json> <battrack.json | bat_tracks.json#clip> <out.png> --frames 1,30,60 [--follow]
"""
import argparse
import json

from PIL import Image, ImageDraw

GRIP_R, BAT_LEN, HANDLE = 0.17, 0.86, 0.30
ap = argparse.ArgumentParser()
ap.add_argument("pose"); ap.add_argument("track"); ap.add_argument("out")
ap.add_argument("--frames", default="1"); ap.add_argument("--follow", action="store_true"); ap.add_argument("--scale", type=float, default=100.0)
a = ap.parse_args()
d = json.load(open(a.pose))
path, _, clip = a.track.partition("#")
t = json.load(open(path))
rows = t["clips"][clip] if clip else t["rows"]
first = d["first"]; by = {fr["frame"]: fr for fr in d["frames"]}
want = [int(x) for x in a.frames.split(",") if x.strip()]
W, Hh, S = 200, 240, a.scale
views = [("side", lambda p: (p[0], p[2])), ("front", lambda p: (p[1], p[2])), ("top", lambda p: (p[0], p[1]))]
img = Image.new("RGB", (W * len(want), Hh * 3 + 24), "white"); dr = ImageDraw.Draw(img)
dr.text((6, 4), f"{d['file']} + bat track ({len(rows)} rows)", fill="black")


def col(n):
    n = n.lower()
    return (220, 40, 40) if "left" in n else (40, 60, 220) if "right" in n else (20, 20, 20)


for ci, f in enumerate(want):
    fr = by.get(first + f - 1)
    if fr is None or f - 1 >= len(rows):
        continue
    r = rows[f - 1]
    g, ax = r[0:3], r[3:6]
    top = [g[i] - ax[i] * GRIP_R for i in range(3)]
    hand_end = [top[i] + ax[i] * HANDLE for i in range(3)]
    toe = [top[i] + ax[i] * BAT_LEN for i in range(3)]
    hips = next(v[0] for k, v in fr["bones"].items() if k.endswith("Hips"))
    for vi, (vn, proj) in enumerate(views):
        ox, oy = ci * W, 24 + vi * Hh
        dr.rectangle([ox, oy, ox + W - 1, oy + Hh - 1], outline=(200, 200, 200))
        cx = hips[0] if (a.follow or vn == "front") else 0.0
        cy = hips[1] if (a.follow or vn == "side") else 0.0
        if vn == "top":
            def px(p, ox=ox, oy=oy, cx=cx, cy=cy):
                return (ox + W / 2 + (p[0] - cx) * S, oy + Hh / 2 - (p[1] - cy) * S)
        else:
            def px(p, ox=ox, oy=oy, cx=cx, cy=cy, proj=proj, vn=vn):
                u, v = proj(p); c = cx if vn == "side" else cy
                return (ox + W / 2 + (u - c) * S, oy + Hh - 30 - v * S)
            dr.line([(ox + 4, oy + Hh - 30), (ox + W - 4, oy + Hh - 30)], fill=(60, 170, 60))
        for n, (h, tl) in fr["bones"].items():
            if not (n.endswith("_end") or n.endswith("_End")):
                dr.line([px(h), px(tl)], fill=col(n), width=2)
        dr.line([px(hand_end), px(toe)], fill=(160, 100, 30), width=6)
        dr.line([px(top), px(hand_end)], fill=(90, 50, 10), width=3)
        dr.text((ox + 6, oy + 4), f"{vn} f{f} tg{r[9]:.0f}", fill="black")
img.save(a.out)
print("bat sheet", a.out)
