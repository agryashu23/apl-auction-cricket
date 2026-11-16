"""Contact sheets for the batting capture (-aplbatcap).

usage: python -I batcap_sheet.py <autoshots/batcap dir> [thumb width]

One sheet per ball: rows = moments (guard, release, pre, contact, after,
follow, finish), columns = cameras (main, off, leg, bowler, keeper, top),
with the ball's lines from batcap.txt printed across the top.
"""
import os
import re
import sys

from PIL import Image, ImageDraw

CAMS = ["main", "off", "leg", "bowler", "keeper", "close"]


def main():
    src = sys.argv[1]
    tw = int(sys.argv[2]) if len(sys.argv) > 2 else 320
    th = tw * 9 // 16
    out = os.path.join(src, "sheets")
    os.makedirs(out, exist_ok=True)
    notes = {}
    log = os.path.join(src, "batcap.txt")
    if os.path.exists(log):
        ball = None
        for line in open(log, encoding="utf-8"):
            m = re.match(r"--- ball (\d+)", line)
            if m:
                ball = int(m.group(1))
            if ball is not None:
                notes.setdefault(ball, []).append(line.strip())
    files = [f for f in os.listdir(src) if f.endswith(".png")]
    balls = sorted({int(f[1:3]) for f in files if re.match(r"b\d\d_", f)})
    for b in balls:
        moments = sorted({f.split("_")[1] for f in files if f.startswith(f"b{b:02d}_")})
        head = 18 * (len(notes.get(b, [])) + 1)
        sheet = Image.new("RGB", (tw * len(CAMS) + 70, th * len(moments) + head + 16), (20, 20, 24))
        d = ImageDraw.Draw(sheet)
        for i, line in enumerate(notes.get(b, [])):
            d.text((6, 4 + 18 * i), line[:300], fill=(240, 240, 200))
        for c, cam in enumerate(CAMS):
            d.text((70 + c * tw + 4, head), cam, fill=(200, 220, 255))
        for r, mo in enumerate(moments):
            y = head + 16 + r * th
            d.text((4, y + th // 2), mo, fill=(255, 255, 255))
            for c, cam in enumerate(CAMS):
                p = os.path.join(src, f"b{b:02d}_{mo}_{cam}.png")
                if not os.path.exists(p):
                    continue
                im = Image.open(p).convert("RGB")
                im.thumbnail((tw, th))
                sheet.paste(im, (70 + c * tw, y))
        sheet.save(os.path.join(out, f"ball{b:02d}.png"))
    print(f"{len(balls)} sheets -> {out}")


if __name__ == "__main__":
    main()
