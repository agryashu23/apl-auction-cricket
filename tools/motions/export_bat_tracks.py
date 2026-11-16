"""
Export the bat's authored path for every batting clip (the generator's own
bat channels — grip point, axis down the blade, face normal — per frame) to
Resources/Data/cricket/bat_tracks.json. In the game the bat is placed from
this track and both hands are IK'd onto its handle: Unity's humanoid
retargeting drops the hands' twist, so a bat socketed to the hand bone
wanders from pose to pose; the authored path doesn't.

Frame f here is the generator's 0-based frame = the FBX's frame f+1 = clip
time f/30 s (the import starts the clips at frame 1, past the rest key).
Coordinates are the generator's: metres, Z up, bowler at +X, batter's chest
(off side for a right-hander) at -Y. Left-handed clips are the mirror (y -> -y).

    motion.ps1 gen export_bat_tracks.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lib"))
import batting  # noqa: E402  (bat geometry)
import shots  # noqa: E402
import shots74  # noqa: E402
from mixamo_motions import V, sample  # noqa: E402

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
OUT = os.environ.get("APL_BAT_TRACKS", _APL_REPO + "/client-src/Assets/Resources/Data/cricket/bat_tracks.json")


def track(nframes, keys):
    rows = []
    for f in range(nframes):
        pose = sample(keys, f)
        a = V(pose["ba"]).normalized()
        n = batting.ortho(pose["bn"], a)
        g = V(pose["bg"])
        rows.append([round(c, 4) for c in (*g, *a, *n, float(pose.get("topgrip", 1.0)))])
    return rows


clips = {}
n, keys, _contact, _meta = shots.SHOTS["stance_idle_loop"]
clips["00_stance_idle_loop"] = track(n, keys)
for sid, sh in sorted(shots74.SHOTS74.items()):
    name = f"{sid:02d}_{shots74.slug(sh['name'])}"
    clips[name] = track(sh["n"], sh["keys"])
# The reworked shots (drivers/batting_v2.py) replace the pack's where they exist.
try:
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "drivers"))
    import batting_v2  # noqa: E402
    n2, k2, _c2, _m2 = batting_v2.STANCE_V2
    clips["00_stance_idle_loop"] = track(n2, k2)
    for sid, sh in sorted(batting_v2.SHOTS_V2.items()):
        clips[f"{sid:02d}_{shots74.slug(shots74.SHOTS74[sid]['name'])}"] = track(sh["n"], sh["keys"])
    print("v2 tracks:", len(batting_v2.SHOTS_V2) + 1)
except Exception as ex:
    print("v2 tracks skipped:", ex)
# Round 6: the real-mocap clips (drivers/batting_mocap.py writes <clip>.battrack.json, right-handed rows) replace
# their ids' tracks, and its stance replaces the stance track.
MOC = os.environ.get("APL_MOCAP_TRACKS", "")
if MOC and os.path.isdir(MOC):
    n_moc = 0
    for f in sorted(os.listdir(MOC)):
        if f.endswith(".battrack.json"):
            t = json.load(open(os.path.join(MOC, f)))
            clips[t["clip"]] = t["rows"]; n_moc += 1
    print("mocap tracks:", n_moc, "from", MOC)
# The non-striker clips (drivers/non_striker.py): the top hand is off the handle, so topgrip matters.
try:
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "drivers"))
    import non_striker  # noqa: E402
    clips["75_non_striker_idle_loop"] = track(non_striker.IDLE_KEYS[-1][0] + 1, non_striker.IDLE_KEYS)
    clips["76_backing_up"] = track(non_striker.BACK_KEYS[-1][0] + 1, non_striker.BACK_KEYS)
    ready = getattr(non_striker, "READY_KEYS", None)
    if ready:
        clips["77_backing_up_ready_loop"] = track(ready[-1][0] + 1, ready)
except Exception as ex:  # the driver may not be there yet
    print("non-striker tracks skipped:", ex)
# The ceremony clips (drivers/ceremony_*.py, round 3): each export writes the SOLVED bat per frame to
# <clip>.battrack.json; both hands are tracked under their full names (rhb_... and lhb_..., lhb rows already mirrored).
CER = os.environ.get("APL_CEREMONY_TRACKS", _APL_REPO + "/build/motions/ceremony_r3")
n_cer = 0
if os.path.isdir(CER):
    for f in sorted(os.listdir(CER)):
        if f.endswith(".battrack.json"):
            t = json.load(open(os.path.join(CER, f)))
            clips[t["clip"]] = t["rows"]; n_cer += 1
print("ceremony tracks:", n_cer, "from", CER)
# Never drop a track the game already has: anything in the installed file the generators above didn't produce is kept.
INSTALLED = _APL_REPO + "/client-src/Assets/Resources/Data/cricket/bat_tracks.json"
if os.path.exists(INSTALLED) and os.path.abspath(INSTALLED) != os.path.abspath(OUT):
    old = json.load(open(INSTALLED))["clips"]
    kept = [k for k in old if k not in clips]
    for k in kept: clips[k] = old[k]
    print("kept from the installed file:", len(kept), kept[:6])
data = {
    "conventions": {
        "frame": "row f = generator frame f (0-based) = FBX frame f+1 = clip time f/30 s",
        "row": "[grip x,y,z, axis x,y,z (handle top -> toe), face x,y,z]; metres; Z up; bowler +X; right-hander's chest/off side -Y; origin between the feet on the popping crease",
        "grip": f"the bottom hand's point on the handle, {batting.GRIP_R} m below the handle top; top hand {batting.GRIP_L} m below the top; bat {batting.BAT_LEN} m long",
        "left_handed": "lhb_ batting clips: the same rows with y negated; ceremony clips (rhb_*/lhb_* names) carry their own rows, lhb already mirrored",
    },
    "clips": clips,
}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(data, open(OUT, "w"), separators=(",", ":"))
print(f"bat tracks: {len(clips)} clips, {sum(len(v) for v in clips.values())} frames -> {OUT} ({os.path.getsize(OUT) // 1024} KB)")
