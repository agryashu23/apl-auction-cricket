"""
Per-venue art for the gameplay frame (round 10: "all 13 venues look identical from the
gameplay camera"):

    client-src/Assets/Resources/Ui/Venues/<id>_board.png    640 x 248  LED board: the venue's
        colours, its motif (waves, fort walls, peaks ...) and its name — one in every four
        rope boards (StadiumBuilder.RopeAndBoards)
    client-src/Assets/Resources/Ui/Venues/<id>_screen.png  1024 x 396  sight screen: dark (the
        batter picks the ball up against it) but not a void — a navy-to-venue gradient, LED
        panel seams, a faint motif low down, a frame in the venue's accent, the name small on top

Font: Oswald (SIL OFL, client-assets/fonts/Oswald-VF.ttf). Sheet: build/review/venue_art.png.

    python tools/venue_art.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client-src", "Assets", "Resources", "Ui", "Venues")
FONT = os.path.join(ROOT, "client-assets", "fonts", "Oswald-VF.ttf")

# id: (name, city, dark, accent, motif) — accents match StadiumLook.Accent in Stadiums.cs.
VENUES = {
    "seaface": ("SEAFACE STADIUM", "MUMBAI", (10, 32, 70), (0, 170, 220), "waves"),
    "marina": ("MARINA OVAL", "CHENNAI", (40, 30, 8), (250, 196, 30), "sails"),
    "gardencity": ("GARDEN CITY BOWL", "BENGALURU", (14, 44, 22), (120, 200, 70), "trees"),
    "lakeside": ("LAKESIDE ARENA", "NAVA RAIPUR", (10, 40, 52), (80, 210, 200), "ripples"),
    "hooghly": ("HOOGHLY OVAL", "KOLKATA", (36, 14, 50), (190, 120, 240), "bridge"),
    "oldfort": ("OLD FORT GROUND", "DELHI", (50, 18, 10), (230, 110, 60), "fort"),
    "deccan": ("DECCAN ARENA", "HYDERABAD", (46, 22, 6), (250, 140, 30), "minarets"),
    "sabarmati": ("SABARMATI MEGA BOWL", "AHMEDABAD", (8, 26, 56), (60, 140, 255), "river"),
    "awadh": ("AWADH ARENA", "LUCKNOW", (8, 40, 44), (40, 200, 190), "gateway"),
    "shivalik": ("SHIVALIK PARK", "NEW CHANDIGARH", (40, 10, 16), (230, 60, 80), "hills"),
    "dhauladhar": ("DHAULADHAR HEIGHTS", "DHARAMSHALA", (14, 26, 48), (200, 225, 255), "peaks"),
    "pinkcity": ("PINK CITY OVAL", "JAIPUR", (60, 16, 34), (250, 120, 160), "arches"),
    "brahmaputra": ("BRAHMAPUTRA PARK", "GUWAHATI", (8, 38, 30), (90, 220, 140), "hills_river"),
}


def font(size, weight="Bold"):
    f = ImageFont.truetype(FONT, size)
    f.set_variation_by_name(weight)
    return f


def gradient(w, h, top, bottom):
    im = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(im)
    for y in range(h):
        k = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * k) for i in range(3)))
    return im


def motif(kind, w, h):
    """The venue's silhouette as an L mask (255 = motif), filling the bottom of a w x h area."""
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    if kind == "waves":
        for row in range(3):
            y0 = h * (0.55 + 0.15 * row)
            pts = [(x, y0 + math.sin(x / w * math.pi * 8 + row) * h * 0.06) for x in range(0, w + 8, 8)]
            d.line(pts, fill=255, width=max(3, h // 30))
    elif kind == "sails":
        for i in range(6):
            x = w * (0.08 + i * 0.17)
            d.polygon([(x, h * 0.95), (x + w * 0.06, h * 0.35), (x + w * 0.1, h * 0.95)], fill=255)
    elif kind == "trees":
        for i in range(9):
            x = w * (0.04 + i * 0.115)
            r = h * (0.16 + 0.05 * (i % 3))
            d.ellipse([x - r, h * 0.62 - r, x + r, h * 0.62 + r], fill=255)
            d.rectangle([x - r * 0.12, h * 0.62, x + r * 0.12, h], fill=255)
    elif kind == "ripples":
        for i in range(5):
            r = w * (0.08 + i * 0.09)
            d.arc([w * 0.5 - r, h * 0.85 - r * 0.35, w * 0.5 + r, h * 0.85 + r * 0.35], 180, 360, fill=255, width=max(3, h // 40))
    elif kind == "bridge":
        d.rectangle([0, h * 0.78, w, h * 0.84], fill=255)
        for x0 in (w * 0.3, w * 0.7):
            d.rectangle([x0 - w * 0.015, h * 0.2, x0 + w * 0.015, h], fill=255)
            for k in range(1, 7):
                d.line([(x0, h * 0.22), (x0 + k * w * 0.03, h * 0.8)], fill=255, width=2)
                d.line([(x0, h * 0.22), (x0 - k * w * 0.03, h * 0.8)], fill=255, width=2)
    elif kind == "fort":
        d.rectangle([0, h * 0.55, w, h], fill=255)
        step = w / 24
        for i in range(24):
            if i % 2 == 0:
                d.rectangle([i * step, h * 0.45, (i + 1) * step, h * 0.55], fill=255)
        for x in (w * 0.15, w * 0.5, w * 0.85):
            d.rectangle([x - w * 0.04, h * 0.3, x + w * 0.04, h], fill=255)
            d.rectangle([x - w * 0.05, h * 0.24, x + w * 0.05, h * 0.3], fill=255)
    elif kind == "minarets":
        d.rectangle([w * 0.3, h * 0.5, w * 0.7, h], fill=255)
        d.ellipse([w * 0.43, h * 0.62, w * 0.57, h * 0.9], fill=0)
        d.rectangle([w * 0.43, h * 0.76, w * 0.57, h], fill=0)
        for x in (w * 0.3, w * 0.7):
            d.rectangle([x - w * 0.025, h * 0.12, x + w * 0.025, h], fill=255)
            d.ellipse([x - w * 0.035, h * 0.04, x + w * 0.035, h * 0.18], fill=255)
    elif kind == "river":
        for i in range(4):
            y0 = h * (0.6 + 0.1 * i)
            pts = [(x, y0 + math.sin(x / w * math.pi * 3 + i * 0.7) * h * 0.05) for x in range(0, w + 8, 8)]
            d.line(pts, fill=255, width=max(3, h // 35))
    elif kind == "gateway":
        d.rectangle([w * 0.25, h * 0.25, w * 0.75, h], fill=255)
        d.ellipse([w * 0.38, h * 0.38, w * 0.62, h * 0.8], fill=0)
        d.rectangle([w * 0.38, h * 0.6, w * 0.62, h], fill=0)
        for i in range(7):
            x = w * (0.27 + i * 0.077)
            d.ellipse([x - w * 0.015, h * 0.18, x + w * 0.015, h * 0.27], fill=255)
    elif kind in ("hills", "hills_river", "peaks"):
        pts = [(0, h)]
        for x in range(0, w + 10, 10):
            t = x / w
            if kind == "peaks":
                y = h * (0.35 + 0.3 * abs(math.sin(t * math.pi * 3.2)) + 0.08 * math.sin(t * 37))
            else:
                y = h * (0.5 + 0.18 * math.sin(t * math.pi * 2.2 + 0.6) + 0.06 * math.sin(t * 19))
            pts.append((x, y))
        pts.append((w, h))
        d.polygon(pts, fill=255)
        if kind == "hills_river":
            d.rectangle([0, h * 0.86, w, h * 0.92], fill=0)
    elif kind == "arches":
        n = 7
        for i in range(n):
            x = w * (i + 0.5) / n
            d.rectangle([x - w * 0.05, h * 0.3, x + w * 0.05, h], fill=255)
        for i in range(n):
            x = w * (i + 0.5) / n
            d.ellipse([x - w * 0.03, h * 0.45, x + w * 0.03, h * 0.65], fill=0)
            d.rectangle([x - w * 0.03, h * 0.55, x + w * 0.03, h * 0.9], fill=0)
    return m


def tint(c, k):
    return tuple(min(255, int(v * k)) for v in c)


def board(vid, name, city, dark, accent, kind):
    w, h = 640, 248
    im = gradient(w, h, tint(dark, 1.6), dark)
    sil = motif(kind, w, h)
    im.paste(Image.new("RGB", (w, h), tint(accent, 0.55)), (0, 0), sil.point(lambda v: v * 0.55))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w - 1, 9], fill=accent)
    d.rectangle([0, h - 10, w - 1, h - 1], fill=accent)
    f = font(78)
    tw = d.textlength(name, font=f)
    if tw > w - 40:
        f = font(int(78 * (w - 40) / tw))
        tw = d.textlength(name, font=f)
    d.text(((w - tw) / 2, 52), name, font=f, fill=(255, 255, 255))
    fc = font(34, "Medium")
    cw = d.textlength(city, font=fc)
    d.text(((w - cw) / 2, 160), city, font=fc, fill=accent)
    return im


def screen(vid, name, city, dark, accent, kind):
    """Round 12: the plain dark screen read as a blank big screen (r11 `103`): now it carries the
    venue's motif at a readable strength and its name large across the middle — still dark overall
    (a ball must show against it), like a real LED sight screen showing the ground's branding."""
    w, h = 1024, 396
    deep = (6, 9, 22)
    im = gradient(w, h, tint(dark, 0.7), deep)
    sil = motif(kind, w, int(h * 0.55)).filter(ImageFilter.GaussianBlur(1))
    layer = Image.new("RGB", (w, int(h * 0.55)), tint(accent, 0.32))
    im.paste(layer, (0, h - int(h * 0.55)), sil.point(lambda v: v * 0.8))
    d = ImageDraw.Draw(im)
    for x in range(0, w, 64):  # LED cabinets
        d.line([(x, 0), (x, h)], fill=(0, 0, 0), width=2)
    d.line([(0, h // 2), (w, h // 2)], fill=(0, 0, 0), width=2)
    f = font(96)
    tw = d.textlength(name, font=f)
    if tw > w - 80:
        f = font(int(96 * (w - 80) / tw))
        tw = d.textlength(name, font=f)
    d.text(((w - tw) / 2, h * 0.22), name, font=f, fill=tint(accent, 0.55))
    fc = font(34, "Medium")
    cw = d.textlength(city, font=fc)
    d.text(((w - cw) / 2, h * 0.22 + 112), city, font=fc, fill=tint(accent, 0.4))
    b = 8
    d.rectangle([0, 0, w - 1, h - 1], outline=tint(accent, 0.85), width=b)
    return im


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    tiles = []
    for vid, v in VENUES.items():
        bo = board(vid, *v)
        sc = screen(vid, *v)
        bo.save(os.path.join(OUT, vid + "_board.png"))
        sc.save(os.path.join(OUT, vid + "_screen.png"))
        tiles.append((bo, sc))
    sheet = Image.new("RGB", (640 + 512, 248 * len(tiles)))
    for i, (bo, sc) in enumerate(tiles):
        sheet.paste(bo, (0, i * 248))
        sheet.paste(sc.resize((512, 198)), (640, i * 248 + 25))
    sheet.save(os.path.join(ROOT, "build", "review", "venue_art.png"))
    print("wrote", len(tiles), "venues")
