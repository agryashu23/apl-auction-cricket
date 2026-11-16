"""
Report on verify_delivery.py's dump against the delivery's sidecars (data only; run with python -I).

    python -I tools/motions/verify_report.py <dump.json> <sidecar.json> [<sidecar.json> ...]

Per clip: rig (bones, rest-pose difference), fps, frame count against the sidecar, planted-foot drift in each stated
range (ToeBase, cm), the turn (hips yaw, first to last frame, + = left) and travel (hips, in the start frame) against the
sidecar, the largest one-frame jerk of any joint (second difference, cm: a pop), and how close the first / last frames
are to our clips (joints relative to the hips with the facing taken out; loops: the nearest frame).
"""
import json
import math
import sys

dump = json.load(open(sys.argv[1]))
side = {}
for p in sys.argv[2:]:
    for c in json.load(open(p))["clips"]:
        side[c["file"][:-4]] = c


def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]


def norm(v):
    return math.sqrt(sum(x * x for x in v))


def yaw(f):
    """Facing angle (rad, CCW from above) from the hips' right vector: forward = up x right."""
    r = sub(f["RightUpLeg"], f["LeftUpLeg"])
    fx, fy = -r[1], r[0]  # up x right, Z up
    return math.atan2(fy, fx)


def local(f):
    a = -yaw(f)
    c, s = math.cos(a), math.sin(a)
    h = f["Hips"]
    out = {}
    for k, p in f.items():
        if k == "Hips":
            continue
        d = sub(p, h)
        out[k] = (c * d[0] - s * d[1], s * d[0] + c * d[1], d[2])
    return out


def pose_gap(f, g):
    a, b = local(f), local(g)
    return max(norm(sub(a[k], b[k])) for k in a if k in b) * 100


def nearest(f, frames):
    return min((pose_gap(f, g), i + 1) for i, g in enumerate(frames))


refs = {k: v["frames"] for k, v in dump["refs"].items()}
expect = {
    "fielder_turn_90_left": ("ready", "ready"), "fielder_turn_90_right": ("ready", "ready"),
    "fielder_turn_180_left": ("ready", "ready"), "fielder_turn_180_right": ("ready", "ready"),
    "fielder_turn_and_go_180": ("ready", "jog*"), "fielder_walk_turn_90_left": ("walk*", "walk*"),
    "fielder_walk_turn_90_right": ("walk*", "walk*"), "rhb_turn_90_settle": (None, "rstance|rdef"),
    "lhb_turn_90_settle": (None, "lstance|ldef"), "rhb_turn_180_after_run": (None, "rns*"),
    "lhb_turn_180_after_run": (None, "lns*"), "bowler_walkback_turn_180": (None, "rawb*"),
    "fielder_start_walk": ("ready", "walk*"), "fielder_start_jog": ("ready", "jog*"), "fielder_start_sprint": ("ready", "sprint*"),
    "fielder_stop_from_walk": ("walk*", "ready"), "fielder_stop_from_jog": ("jog*", "ready"), "fielder_stop_from_sprint": ("sprint*", "ready"),
}


def compare(f, spec):
    if spec is None:
        return "-"
    best = None
    for one in spec.split("|"):
        loop = one.endswith("*")
        name = one.rstrip("*")
        fr = refs[name]
        g, i = nearest(f, fr) if loop else (pose_gap(f, fr[0]), 1)
        if best is None or g < best[0]:
            best = (g, f"{name}{'@' + str(i) if loop else ' f1'}")
    return f"{best[0]:.1f} cm to {best[1]}"


print(f"rig bones {dump['rig_bones']}")
for name, c in sorted(dump["clips"].items()):
    fr = c["frames"]
    s = side.get(name, {})
    n = len(fr)
    lines = [f"== {name}: bones {c['bones']} (missing {len(c['missing'])}, extra {len(c['extra'])}), rest diff {c['rest_diff_cm']} cm, "
             f"fps {c['fps']}, frames {n} (sidecar {s.get('frames')}), keys {c['info']['keys'] if c['info'] else '-'}"]
    # Planted drift.
    worst = []
    for foot, key in (("Left", "left_foot_planted"), ("Right", "right_foot_planted")):
        for a, b in s.get(key, []):
            if a < 1 or b > n:
                worst.append(f"{foot} {a}-{b} OUT OF RANGE")
                continue
            p0 = fr[a - 1][foot + "ToeBase"]
            d = max(math.hypot(fr[i][foot + "ToeBase"][0] - p0[0], fr[i][foot + "ToeBase"][1] - p0[1]) for i in range(a - 1, b))
            h = max(fr[i][foot + "ToeBase"][2] for i in range(a - 1, b)) - min(fr[i][foot + "ToeBase"][2] for i in range(a - 1, b))
            worst.append((d * 100, f"{foot} {a}-{b}: {d * 100:.2f} cm (height range {h * 100:.1f} cm)"))
    drifts = [w for w in worst if isinstance(w, tuple)]
    bad = [w for w in worst if not isinstance(w, tuple)]
    maxd = max((w[0] for w in drifts), default=0)
    lines.append(f"   planted drift max {maxd:.2f} cm over {len(drifts)} ranges" + (f"; {bad}" if bad else "")
                 + ("" if maxd <= 1.0 else "  <-- worst: " + max(drifts)[1]))
    # Turn and travel.
    y0, y1 = yaw(fr[0]), yaw(fr[-1])
    turn = math.degrees(math.atan2(math.sin(y1 - y0), math.cos(y1 - y0)))
    h0, h1 = fr[0]["Hips"], fr[-1]["Hips"]
    dx, dy = h1[0] - h0[0], h1[1] - h0[1]
    fwd = dx * math.cos(y0) + dy * math.sin(y0)
    left = -dx * math.sin(y0) + dy * math.cos(y0)
    tm = s.get("travel_m", {})
    lines.append(f"   turn {turn:+.1f} deg (sidecar {s.get('turn_deg', '-')}), travel fwd {fwd:+.2f} left {left:+.2f} m "
                 f"(sidecar fwd {tm.get('forward', '-') if isinstance(tm, dict) else tm} left {tm.get('left', '-') if isinstance(tm, dict) else '-'})")
    # Pops.
    jerk = (0, 0, "")
    for i in range(1, n - 1):
        for k in fr[i]:
            d = norm([fr[i + 1][k][j] - 2 * fr[i][k][j] + fr[i - 1][k][j] for j in range(3)])
            if d > jerk[0]:
                jerk = (d, i + 1, k)
    lines.append(f"   largest one-frame jerk {jerk[0] * 100:.1f} cm ({jerk[2]} at frame {jerk[1]})")
    sp, ep = expect.get(name, (None, None))
    lines.append(f"   first frame {compare(fr[0], sp)}; last frame {compare(fr[-1], ep)}")
    print("\n".join(lines))
