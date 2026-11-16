"""
Geometry helpers for the cricket gear (tools/gear/make_gear.py).

Everything is built in Unity's body frame: x right, y up, z forward (the way
the body faces), metres, in the Rocketbox bind pose (all cast bodies share one
skeleton). A triangle faces outward when cross(b - a, c - a) points outward
(Unity's front face); grids are auto-oriented against an "outward" function so
nobody has to think about winding.

The export (gear.bytes) is read by Apl.Client.Match.GearMeshes:

    "APLG" int32 version(=3) int32 parts
    part:  str name, int32 slot, int32 bones, str bone[bones],
           int32 verts, verts x (3f pos, 3f normal, 2f uv, u8 bone, u8 bone2, f32 weight2),
           int32 indices, int32[indices]
    str = int32 byte length + UTF-8

Positions are relative to the vertex's bone (its bind position in the
reference body); bone "None" means absolute (the bat's own frame). A vertex may lean on a
second bone by weight2 (knee, wrist and finger joints bend smoothly).
Slots: 0 team colour, 1 white padding, 2 dark (steel, straps, vents), 3 bat.
"""
import math
import struct
import zlib

from mathutils import Vector

TEAM, WHITE, DARK, BAT = 0, 1, 2, 3


def V(x, y, z):
    return Vector((x, y, z))


class Part:
    """One piece of gear: vertices (each on one bone), triangles, one material slot."""

    def __init__(self, name, slot):
        self.name = name
        self.slot = slot
        self.v, self.uv, self.bone, self.t = [], [], [], []
        self.bone2, self.w2 = [], []
        self.n = None

    def vert(self, p, bone, uv=(0.0, 0.0), bone2=None, w2=0.0):
        """A vertex on `bone`; with bone2 it blends toward that bone by w2 (0..1)."""
        self.v.append(Vector(p))
        self.uv.append(tuple(uv))
        self.bone.append(bone)
        self.bone2.append(bone2 if bone2 and w2 > 0 else bone)
        self.w2.append(w2 if bone2 else 0.0)
        return len(self.v) - 1

    @staticmethod
    def _on(b):
        """A bone, or (bone, bone2, weight2): as vert()'s bone, bone2, w2 (keyword-free)."""
        return (b[0], (0.0, 0.0), b[1], b[2]) if isinstance(b, tuple) else (b, (0.0, 0.0), None, 0.0)

    def tri(self, a, b, c, out=None):
        """A triangle; with `out` (a direction or a function of the centre) it is flipped to face it."""
        if out is not None:
            pa, pb, pc = self.v[a], self.v[b], self.v[c]
            centre = (pa + pb + pc) / 3
            o = out(centre) if callable(out) else Vector(out)
            if (pb - pa).cross(pc - pa).dot(o) < 0:
                b, c = c, b
        self.t.append((a, b, c))

    def quad(self, a, b, c, d, out=None):
        self.tri(a, b, c, out)
        self.tri(a, c, d, out)

    def grid(self, P, ni, nj, bone, out, uvf=None, wrap_j=False):
        """
        A surface P(i, j) for i in 0..ni, j in 0..nj (j wraps round if wrap_j).
        `bone` is a name or a function (i, j) -> name. Returns the index table.
        """
        cols = nj if wrap_j else nj + 1
        idx = [[0] * cols for _ in range(ni + 1)]
        for i in range(ni + 1):
            for j in range(cols):
                b = bone(i, j) if callable(bone) else bone
                uv = uvf(i, j) if uvf else (j / max(nj, 1), i / max(ni, 1))
                idx[i][j] = self.vert(P(i, j), b, uv)
        for i in range(ni):
            for j in range(nj):
                j1 = (j + 1) % cols
                self.quad(idx[i][j], idx[i + 1][j], idx[i + 1][j1], idx[i][j1], out)
        return idx

    def tube(self, pts, radius, sides, bone, up=None, cap_start=False, cap_end=False, bones=None):
        """
        A tube along the polyline `pts`. radius(k, s) gives the radius at
        point k (s = 0..1 along) — a number or (side, up-radius, down-radius)
        for an oval with a fatter top toward `up`. `bones[k]` (optional)
        puts each ring on its own bone.
        """
        n = len(pts)
        rings = []
        prev_side = None
        for k in range(n):
            a = pts[max(k - 1, 0)]
            b = pts[min(k + 1, n - 1)]
            d = (b - a).normalized()
            ref = Vector(up) if up is not None else (V(0, 1, 0) if abs(d.y) < 0.9 else V(1, 0, 0))
            u = (ref - d * ref.dot(d))
            if u.length < 1e-6:
                u = prev_side.cross(d) if prev_side is not None else V(1, 0, 0)
            u = u.normalized()
            side = d.cross(u).normalized()
            prev_side = side
            r = radius(k, k / max(n - 1, 1)) if callable(radius) else radius
            rs, ru, rd = (r, r, r) if not isinstance(r, tuple) else r
            ring = []
            for s in range(sides):
                a_ = 2 * math.pi * s / sides
                cu, sn = math.cos(a_), math.sin(a_)
                p = pts[k] + u * (cu * (ru if cu > 0 else rd)) + side * (sn * rs)
                b1, _, b2, w = self._on(bones[k] if bones else bone)
                ring.append(self.vert(p, b1, (s / sides, k / max(n - 1, 1)), b2, w))
            rings.append(ring)
        for k in range(n - 1):
            ca, cb = pts[k], pts[k + 1]
            for s in range(sides):
                s1 = (s + 1) % sides
                axis_c = (ca + cb) / 2
                self.quad(rings[k][s], rings[k + 1][s], rings[k + 1][s1], rings[k][s1],
                          out=lambda c, ac=axis_c, d=(cb - ca).normalized(): (c - ac) - d * (c - ac).dot(d))
        for cap, k, sign in ((cap_start, 0, -1), (cap_end, n - 1, 1)):
            if not cap:
                continue
            d = (pts[min(k + 1, n - 1)] - pts[max(k - 1, 0)]).normalized() * sign
            r0 = radius(k, k / max(n - 1, 1)) if callable(radius) else radius
            r0 = r0[0] if isinstance(r0, tuple) else r0
            tip = self.vert(pts[k] + d * r0 * 0.7, *self._on(bones[k] if bones else bone))
            for s in range(sides):
                self.tri(rings[k][s], rings[k][(s + 1) % sides], tip, out=d)
        return rings

    def finish(self):
        """Smooth normals: area-weighted face normals summed per vertex."""
        acc = [Vector((0, 0, 0)) for _ in self.v]
        for a, b, c in self.t:
            fn = (self.v[b] - self.v[a]).cross(self.v[c] - self.v[a])
            for i in (a, b, c):
                acc[i] += fn
        self.n = [x.normalized() if x.length > 1e-12 else Vector((0, 1, 0)) for x in acc]
        return self

    def mirrored(self, name):
        """The other side: x flipped, Left <-> Right bones, winding reversed."""
        m = Part(name, self.slot)
        sw = lambda b: b.replace("Left", "\0").replace("Right", "Left").replace("\0", "Right")  # noqa: E731
        for p, uv, b, b2, w in zip(self.v, self.uv, self.bone, self.bone2, self.w2):
            m.vert(V(-p.x, p.y, p.z), sw(b), uv, sw(b2), w)
        m.t = [(a, c, b) for a, b, c in self.t]
        return m.finish()

    @property
    def tris(self):
        return len(self.t)


def export(parts, path, bind):
    """Writes gear.bytes; `bind` maps bone name -> its bind position (body frame)."""
    out = bytearray(b"APLG")
    out += struct.pack("<ii", 3, len(parts))

    def s(x):
        b = x.encode("utf-8")
        return struct.pack("<i", len(b)) + b

    for p in parts:
        if p.n is None:
            p.finish()
        bones = sorted(set(p.bone) | set(p.bone2))
        bi = {b: i for i, b in enumerate(bones)}
        out += s(p.name) + struct.pack("<ii", p.slot, len(bones))
        for b in bones:
            out += s(b)
        out += struct.pack("<i", len(p.v))
        for pos, nrm, uv, b, b2, w in zip(p.v, p.n, p.uv, p.bone, p.bone2, p.w2):
            o = bind.get(b, Vector((0, 0, 0)))
            r = pos - o
            out += struct.pack("<8fBBf", r.x, r.y, r.z, nrm.x, nrm.y, nrm.z, uv[0], uv[1], bi[b], bi[b2], w)
        flat = [i for t in p.t for i in t]
        out += struct.pack("<i", len(flat)) + struct.pack(f"<{len(flat)}i", *flat)
    with open(path, "wb") as f:
        f.write(out)
    return len(out)


def write_png(path, w, h, px):
    """px: rows top to bottom, each a list of (r, g, b, a) bytes."""
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in px)

    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)
