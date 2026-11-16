import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import re, json, csv
txt = open(_os.path.join(APL_WORK, "cricket_bowling_types.txt")).read()
sections = re.split(r"={10,}\nSECTION (\d+) - ([^\n]+)\n={10,}", txt)
entries = []
for i in range(1, len(sections), 3):
    sec_no, sec_name, body = int(sections[i]), sections[i+1].strip(), sections[i+2]
    for line in body.splitlines():
        m = re.match(r"^(\d+)\s{2,}(.+?)\s{2,}(.+)$", line) or re.match(r"^(\d+)\s{2,}(.+?(?:ball|\)))\s+(.+)$", line)
        if m: entries.append(dict(section=sec_no, section_name=sec_name, no=int(m.group(1)), delivery=m.group(2).strip(), what_it_does=m.group(3).strip()))
SEC = {1: ("ra_express_fast", "pace", (142,152)), 2: ("ra_fast_medium", "pace", (128,138)), 3: ("ra_medium", "pace", (110,120)),
       4: ("la_fast", "pace", (132,145)), 5: ("ra_slinger", "pace", (135,148)), 6: ("ra_off_spin", "spin", (82,95)),
       7: ("la_orthodox", "spin", (82,95)), 8: ("ra_leg_spin", "spin", (78,92)), 9: ("la_chinaman", "spin", (76,90)), 10: ("ra_off_spin", "spin", (82,95))}
def pace_variant(d):
    s = d.lower()
    if "slower bouncer" in s or "bumper (slower)" in s: return "back_of_hand", True
    for k in ("bouncer", "short ball", "short", "hard length", "hit the deck", "into-the-pitch", "bumper", "back-of-length"):
        if k in s: return ("bouncer" if k != "back-of-length" else "stock"), False
    if "yorker" in s: return "yorker", False
    if "off-cut" in s or "off-cutter" in s: return "off_cutter", "slower" in s
    if "leg-cut" in s or "leg-cutter" in s: return "leg_cutter", "slower" in s
    if "knuckle" in s or "split-finger" in s: return "knuckle", True
    if "back-of-hand" in s or "dipping slower" in s or "dibbly" in s: return "back_of_hand", True
    if "nip-backer" in s or "seamer away" in s or "seams" in s.split(" ")[0]: return "stock", False
    if "outswing" in s or "swings away" in s: return "outswinger", False
    if "inswing" in s or "swings in" in s: return "inswinger", False
    return "stock", False
def spin_variant(sec, d):
    s = d.lower()
    if "doosra" in s or "leg-spin from an off-spin" in s: return "doosra"
    if "carrom" in s or "knuckle spin" in s: return "carrom"
    if "googly" in s or "wrong'un" in s or "off-spin from a leg-spin" in s: return "googly"
    if sec in (8, 9):
        if "top spinner" in s: return "top_spinner"
        if any(k in s for k in ("flipper", "slider", "zooter", "back-spinner", "back-spin")): return "flipper"
        return "leg_break"
    if any(k in s for k in ("arm ball", "floater", "slider", "teesra", "undercutter", "back-spin")): return "arm_ball"
    if "side-arm" in s: return "side_arm"
    return "off_break"
def physics(sec, d, what, kind, speed, variant, slower):
    s = (d + " " + what).lower(); lo, hi = speed
    v = round((lo+hi)/2) if not slower else round((lo+hi)/2*0.78)
    if "dibbly" in s: v = 95
    if kind == "spin" and ("quicker" in s or "flat" in s or "skid" in s or "slider" in s or "zooter" in s or "flipper" in s): v = round(v*1.08)
    if kind == "spin" and ("flight" in s or "tossed" in s): v = round(v*0.9)
    P = dict(release_speed_kmh=v)
    la = sec in (4, 7, 9)
    # swing (lateral: + = into a right-handed batter, - = away); conventional or reverse
    sw = None
    if "swing" in s or "reverse" in s:
        nm = d.lower()
        direction = "away" if ("outswing" in nm or "swings away" in s) else ("in" if ("inswing" in nm or "swings in" in s or "into" in s) else "away")
        amt = 0.30 if "late" not in s else 0.22
        sw = dict(type="reverse" if "reverse" in s else "conventional", direction=direction,
                  lateral_m=round(amt if direction == "in" else -amt, 2), onset="late (last 30% of flight)" if ("late" in s or "reverse" in s) else "progressive")
    if kind == "pace" and la and "stock angled" in s: P["angle_across_m"] = -0.25
    if "angle-in" in s or "angles in" in s: P["angle_in_m"] = 0.25
    P["swing"] = sw
    seam = None
    if any(k in s for k in ("seams back", "nip-backer", "seams away", "seamer away", "off-cut", "leg-cut", "cutter", "wobble", "cross-seam")):
        if "wobble" in s or "cross-seam" in s: seam = dict(direction="random", lateral_m=0.08, bounce_variation=0.15 if "cross" in s else 0.05)
        else:
            into = ("back" in s or "in" in s.split("moves")[-1] or "off-cut" in s or "nip" in s) and not ("away" in s and "leg-cut" in s)
            if la and "cutter" in s: into = "into rhb" in s
            seam = dict(direction="in" if into else "away", lateral_m=0.10 if "cut" not in s else 0.14)
    P["seam_off_pitch"] = seam
    spin = None
    if kind == "spin":
        turn_in = {"off_break": True, "doosra": False, "carrom": False, "arm_ball": None, "leg_break": False, "googly": True,
                   "flipper": None, "top_spinner": None, "side_arm": True}[variant]
        if la and variant != "arm_ball" and turn_in is not None: turn_in = not turn_in
        if "top spinner" in s or "goes straight" in s: turn_in = None; variant = "top_spinner"
        if "drift" in s: P["drift_m"] = 0.12 if not la else -0.12
        axis = {"top_spinner": "top-spin (forward)", "flipper": "back-spin", "arm_ball": "seam-up / back-spin"}.get(variant, "side-spin")
        rpm = 2400 if sec in (8, 9) else 2000
        if "big-turning" in s or "extra revolutions" in s: rpm = 2900
        deg = 0 if turn_in is None else (6 if "big" in s else 4)
        spin = dict(axis=axis, rpm=rpm, turn="straight on" if turn_in is None else ("in" if turn_in else "away"),
                    turn_deg=deg if turn_in is None or turn_in else -deg)
        if variant in ("top_spinner",): P["bounce_factor"] = 1.15
        if variant in ("flipper",) or "undercut" in s or "stays low" in s: P["bounce_factor"] = 0.8
    P["spin"] = spin
    if "extra bounce" in s or "rises" in s or "hard length" in s: P.setdefault("bounce_factor", 1.12)
    if "skiddy" in s or "stays low" in s: P.setdefault("bounce_factor", 0.85)
    if "knuckle" in s or "floats" in s: P["dip"] = "floats, then dips late"
    P.setdefault("bounce_factor", 1.0)
    # default length / line
    if "yorker" in s: L = "yorker"
    elif "bouncer" in s or "head height" in s or "bumper" in s: L = "bouncer"
    elif "short" in s or "ribs" in s: L = "short"
    elif "back-of-length" in s or "hard length" in s or "into-the-pitch" in s or "back of length" in s: L = "back of length"
    elif "full" in s: L = "full"
    else: L = "good length"
    line = "middle stump"
    if "wide" in s: line = "wide outside off"
    elif "toes" in s or "body" in s or "ribs" in s: line = "middle stump (at the body)"
    elif "away" in s or "outswing" in s or "angled" in s: line = "off stump"
    elif "leg" in s and "leg-cut" not in s and "leg break" not in s and "leg spin" not in s: line = "middle and leg"
    P["default_length"] = L; P["default_line"] = line
    return P
rows = []
for e in entries:
    prefix, kind, speed = SEC[e["section"]]
    if kind == "pace":
        variant, slower = pace_variant(e["delivery"] + " " + e["what_it_does"]) if e["section"] != 5 or True else ("stock", False)
        if e["section"] == 5 and variant not in ("stock","yorker","inswinger","bouncer","back_of_hand"): variant = "stock"
        if e["section"] == 3 and "dibbly" in e["delivery"].lower(): prefix, variant = "ra_slow_medium", "back_of_hand"
        if e["section"] == 2 and "slower" in e["delivery"].lower(): slower = True
        if "slower" in e["delivery"].lower(): slower = True
    else:
        variant = spin_variant(e["section"], e["delivery"] + " " + e["what_it_does"]); slower = False
        if e["section"] == 10:
            prefix = {"doosra": "ra_off_spin", "carrom": "ra_off_spin", "googly": "ra_leg_spin", "flipper": "ra_leg_spin"}.get(variant, "ra_off_spin")
            if "side-arm" in e["delivery"].lower(): prefix, variant = "mystery_side_arm", "side_arm"
    stock = {"pace": "stock", "spin": "off_break" if prefix in ("ra_off_spin","la_orthodox","mystery_side_arm") else "leg_break"}[kind]
    side = "around" if ("around" in e["delivery"].lower() or "angle-in" in e["delivery"].lower() or "round-the-wicket" in e["delivery"].lower()) else "over"
    if prefix == "mystery_side_arm": clip = "mystery_side_arm_over.fbx"
    elif side == "around": clip = f"{prefix}_around_{stock}.fbx"
    else: clip = f"{prefix}_over_{variant}.fbx"
    clip_around = f"{prefix}_around_{stock}.fbx" if prefix != "mystery_side_arm" else None
    ph = physics(e["section"], e["delivery"], e["what_it_does"], kind, speed, variant if kind == "spin" else variant, slower)
    rows.append(dict(e, bowler=prefix, clip=clip, around_the_wicket_clip=clip_around, release_variant=variant, **ph))
LENGTHS = {
  "pace": {"bouncer": [10.0, 12.5], "short": [8.0, 10.0], "back of length": [6.5, 8.0], "good length": [5.0, 6.5], "full": [3.0, 5.0],
           "half volley": [2.0, 3.0], "yorker": [0.3, 1.5], "full toss": "does not pitch; reaches the batter between knee and waist",
           "beamer": "does not pitch; above waist height (illegal)"},
  "spin": {"short": [6.0, 8.0], "good length": [3.5, 5.0], "full": [2.0, 3.5], "half volley": [1.5, 2.0], "yorker": [0.3, 1.2],
           "full toss": "does not pitch", "beamer": "does not pitch; above waist (illegal)"}}
LINES = {"wide outside off": [0.60, 0.90], "outside off (corridor of uncertainty)": [0.20, 0.40], "off stump": [0.11, 0.11],
         "middle stump": [0.0, 0.0], "leg stump": [-0.11, -0.11], "down the leg side": [-0.40, -0.25]}
LENGTH_CLIP = {"bouncer": "bouncer", "short": "bouncer", "yorker": "yorker", "full toss": "yorker", "beamer": "yorker"}
json.dump(dict(rows=rows, lengths_pitching_m_from_batters_stumps=LENGTHS, lines_m_from_middle_stump_positive_off_side=LINES,
               length_to_release_variant=LENGTH_CLIP), open(_os.path.join(APL_WORK, "deliveries.json"),"w"), indent=1)
print(len(rows), "deliveries mapped")
from collections import Counter; print(Counter(r["clip"] for r in rows).most_common(8))
