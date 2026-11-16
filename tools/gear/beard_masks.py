"""
Beard masks for the cast heads, in each head texture's UV space (from
tools/gear/dump_heads.py's build/gear/heads/<model>.json):

    R  the full-beard area: cheeks below the cheekbone line, jaw, chin, under the jaw,
       sideburns up to the ear (soft edges), lips left bare
    G  the core: moustache and chin (goatee / "moustache" looks, and where a short
       beard is thickest)
    B  the lips

-> client-src/Assets/Apl/Resources/Heads/<model>_beard.png (256 x 256; stored sRGB-encoded,
so Unity's default sRGB import in the linear project samples the weights back), and a
review sheet build/gear/heads/beards.png (each head bare / stubble / full).

    python tools/gear/beard_masks.py
"""
import glob
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "client-src", "Assets", "Apl", "Resources", "Heads")
SIZE = 256


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def landmarks(pts):
    """Chin bottom and nose tip on the midline (head-bone frame)."""
    mid = pts[np.abs(pts[:, 0]) < 0.012]
    face = mid[mid[:, 2] > 0.09]
    chin = face[:, 1].min()
    band = mid[(mid[:, 1] > chin + 0.05) & (mid[:, 1] < chin + 0.13)]
    nose = band[np.argmax(band[:, 2]), 1]
    return chin, nose


def weights(p, chin, nose):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    mouth = chin + 0.47 * (nose - chin)
    # Cheek line: high at the sideburn (by the ear), dropping toward the mouth corner.
    k = np.clip((z - 0.0) / 0.09, 0, 1)
    top = (nose + 0.035) * (1 - k) + (nose - 0.028) * k
    upper = 1 - smooth(top - 0.012, top + 0.006, y)
    lower = smooth(chin - 0.05, chin - 0.028, y)
    front = smooth(-0.025, 0.0, z)
    # Under the jaw only toward the front of the neck, not round the back.
    neck = np.where(y < chin, smooth(0.02, 0.05, z), 1.0)
    full = upper * lower * front * neck
    # Lips bare.
    lips = np.clip(1 - ((ax / 0.026) ** 2 + ((y - mouth) / 0.0095) ** 2), 0, 1) * smooth(0.085, 0.1, z)
    lips = np.clip(lips * 3, 0, 1)
    tache = (1 - smooth(0.028, 0.036, ax)) * smooth(mouth + 0.006, mouth + 0.012, y) * (1 - smooth(nose - 0.022, nose - 0.012, y)) * smooth(0.08, 0.095, z)
    goatee = (1 - smooth(0.022, 0.032, ax)) * smooth(chin - 0.012, chin - 0.002, y) * (1 - smooth(mouth - 0.01, mouth - 0.004, y)) * smooth(0.06, 0.08, z)
    core = np.clip(tache + goatee, 0, 1)
    full = np.clip(full + core, 0, 1) * (1 - lips)
    # A: the hair on the scalp (above the brow at the front, the back of the head down to the nape);
    # the shader recolours only its non-skin texels, so a generous area is fine.
    hair = np.maximum(smooth(nose + 0.03, nose + 0.05, y), smooth(0.0, -0.03, z) * smooth(chin, chin + 0.03, y))
    return full, core * (1 - lips), lips, hair


def mask(path):
    d = json.load(open(path))
    tris = np.array(d["tris"], dtype=np.float32)  # (n, 3, 8): u v x y z nx ny nz
    pts = tris[:, :, 2:5].reshape(-1, 3)
    chin, nose = landmarks(pts)
    w = np.stack(weights(pts, chin, nose), 1).reshape(-1, 3, 4)
    S = SIZE * 4
    acc = np.zeros((S, S, 4), np.float32)
    # Rasterise each triangle with its corners' weights interpolated (barycentric), so edges stay smooth.
    for t, wt in zip(tris, w):
        P = np.array([[float(c[0] % 1.0) * S, (1 - float(c[1] % 1.0)) * S] for c in t])
        if wt.max() <= 0:
            continue
        x0, y0 = np.floor(P.min(0)).astype(int)
        x1, y1 = np.ceil(P.max(0)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, S - 1), min(y1, S - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax_, ay), (bx, by), (cx, cy) = P
        den = (by - cy) * (ax_ - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        l0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den
        l1 = ((cy - ay) * (gx - cx) + (ax_ - cx) * (gy - cy)) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -0.02) & (l1 >= -0.02) & (l2 >= -0.02)
        val = l0[..., None] * wt[0] + l1[..., None] * wt[1] + l2[..., None] * wt[2]
        region = acc[y0:y1 + 1, x0:x1 + 1]
        region[inside] = np.maximum(region[inside], val[inside])
    im = Image.fromarray((np.clip(acc, 0, 1) * 255).astype(np.uint8), "RGBA")
    # Per channel: Pillow premultiplies RGBA by alpha when filtering, which would wipe the beard outside the hair mask.
    out = Image.merge("RGBA", [c.filter(ImageFilter.GaussianBlur(2)).resize((SIZE, SIZE), Image.LANCZOS) for c in im.split()])
    return out, chin, nose


def preview(model, m):
    tex = glob.glob(os.path.join(ROOT, "client-assets", "rocketbox", model, "Textures", "*head_color*"))[0]
    base = np.asarray(Image.open(tex).convert("RGB").resize((SIZE * 2, SIZE * 2)), np.float32) / 255
    mm = np.stack([np.asarray(c.resize((SIZE * 2, SIZE * 2)), np.float32) for c in m.split()], 2) / 255
    hairmask = mm[:, :, 3:4]
    rng = np.random.default_rng(1)
    noise = rng.random(base.shape[:2]).astype(np.float32)
    hair = np.array([0.08, 0.06, 0.05], np.float32)
    lum = base.mean(2, keepdims=True)
    outs = [base * (1 - 0.5 * hairmask) + np.array([0.9, 0.2, 0.2]) * 0.5 * hairmask]
    for strength in (0.45, 0.95):
        a = mm[:, :, 0:1] * strength * (0.75 + 0.25 * noise[:, :, None])
        outs.append(base * (1 - a) + hair * (0.6 + lum) * a)
    return np.concatenate(outs, 1)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    rows = []
    for path in sorted(glob.glob(os.path.join(ROOT, "build", "gear", "heads", "*.json"))):
        model = os.path.splitext(os.path.basename(path))[0]
        m, chin, nose = mask(path)
        # RGB sRGB-encoded (Unity's sRGB import decodes it back to the weights); alpha is always linear.
        r, g, b, a = m.split()
        enc = [c.point(lambda v: int(round(255 * (v / 255) ** (1 / 2.2)))) for c in (r, g, b)]
        Image.merge("RGBA", enc + [a]).save(os.path.join(OUT, model + "_beard.png"))
        rows.append(preview(model, m))
        print(f"{model}: chin {chin:.3f} nose {nose:.3f}")
    sheet = np.concatenate(rows[:6], 0) if len(rows) <= 6 else np.concatenate([np.concatenate(rows[:6], 0), np.concatenate(rows[6:12] + [np.zeros_like(rows[0])] * (12 - len(rows)), 0)], 1)
    Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8)).save(os.path.join(ROOT, "build", "gear", "heads", "beards.png"))
