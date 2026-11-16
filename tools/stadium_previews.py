"""
The Match Setup ground picker's images (Resources/Ui/Stadiums/<venueId>.png, 640x360).

    python tools/stadium_previews.py <shots dir>            # from a stadium tour (unity_bootstrap.ps1 -StadiumTour):
                                                            #   NN_<id>_preview.png (else NN_<id>_aerial.png)
    python tools/stadium_previews.py --restyle <png dir>    # re-grade existing 640x360 images in place

Every image is graded the same way: shadows lifted a little and a soft vignette.
(Round 9: no sky composite any more — the game has its own night sky, and the
preview shot is a high aerial that frames the bowl, not the sky.)
"""
import glob
import os
import re
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client-src", "Assets", "Resources", "Ui", "Stadiums")
W, H = 640, 360


def grade(im):
    im = im.convert("RGB").resize((W, H), Image.LANCZOS)
    # Lift the shadows a touch, then a soft vignette.
    im = im.point(lambda v: int(255 * ((v / 255) ** 0.88)))
    im = ImageEnhance.Contrast(im).enhance(1.05)
    vig = Image.new("L", (W, H), 0)
    ImageDraw.Draw(vig).ellipse([-W * 0.2, -H * 0.3, W * 1.2, H * 1.3], fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(60))
    return Image.composite(im, ImageEnhance.Brightness(im).enhance(0.7), vig)


def from_shots(shots):
    found = {}
    for kind in ("aerial", "preview"):  # preview wins when both are there
        for f in glob.glob(os.path.join(shots, f"*_{kind}.png")):
            m = re.match(rf"\d+_(.+)_{kind}\.png$", os.path.basename(f))
            if m:
                found[m.group(1)] = f
    os.makedirs(OUT, exist_ok=True)
    for vid, f in sorted(found.items()):
        grade(Image.open(f)).save(os.path.join(OUT, vid + ".png"))
        print("wrote", vid, "from", os.path.basename(f))
    if not found:
        sys.exit("no *_preview.png / *_aerial.png in " + shots)


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--restyle":
        for f in sorted(glob.glob(os.path.join(sys.argv[2], "*.png"))):
            grade(Image.open(f)).save(f)
            print("restyled", os.path.basename(f))
    elif len(sys.argv) == 2:
        from_shots(sys.argv[1])
    else:
        sys.exit(__doc__)
