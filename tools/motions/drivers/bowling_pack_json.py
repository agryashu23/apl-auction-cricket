"""
Merge the per-clip sidecars written by bowling_real.py into complete pack JSON files, in the exact shape of
client-src/Assets/Resources/Data/cricket/bowling.json and bowling_fast.json (same conventions block, same
clip fields, plus a 'biomechanics' block per clip; bowling.json's deliveries get the new release_frame and
release_point_m of the clip they play). Plain Python, no Blender:

    python tools/motions/drivers/bowling_pack_json.py fast|main
"""
import json, os, sys

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
SRC = _APL_REPO + "/client-src/Assets/Resources/Data/cricket"
OUT = _APL_REPO + "/build/motions"

def merge(pack, folder):
    base = json.load(open(os.path.join(SRC, pack + ".json"), encoding="utf-8"))
    d = os.path.join(OUT, folder)
    side = {f[:-5]: json.load(open(os.path.join(d, f), encoding="utf-8")) for f in os.listdir(d)
            if f.endswith(".json") and f != pack + ".json" and os.path.exists(os.path.join(d, f[:-5] + ".fbx"))}
    old = {c["file"]: c for c in base["clips"]}
    clips = []
    missing = []
    for c in base["clips"]:
        stem = c["file"][:-4]
        if stem in side: clips.append(side[stem])
        else: clips.append(c); missing.append(stem)
    base["clips"] = clips
    if pack == "bowling_fast":
        base["conventions"]["clip"] = ("5.4 s: standing at the mark (0.27 s) -> 2.0 s accelerating run-up (v = 7.2 m/s (1 - e^(-t/0.75 s)), "
                                       "6 steps, ~10 m) -> bound off the left foot (0.33-0.37 s flight) -> back-foot contact -> front-foot "
                                       "contact -> release at 2.93 s (frame 89) -> follow-through, 4 deceleration steps veering off the pitch, "
                                       "ends standing (field_clips STAND) facing the batter")
    else:
        base["conventions"]["start"] = ("each clip starts standing at the top of the mark (start_position_m, bowler_after MARK pose) and contains "
                                        "the whole run-up (speed builds as v = vmax (1 - e^(-t/tau))), bound, back-foot contact, front-foot "
                                        "contact, release, follow-through and deceleration, ending standing facing the batter")
        by = {c["file"]: c for c in clips}
        for dv in base.get("deliveries", []):
            c = by.get(dv.get("clip"))
            if c:
                dv["release_frame"] = c["release_frame"]; dv["release_point_m"] = c["release_point_m"]
    base["conventions"]["biomechanics"] = ("per clip, measured on the exported skeleton: knee angles are included angles (180 = straight); "
                                           "yaw 0 = chest square to the batter, -90 = side-on; separation = shoulders - pelvis; trunk angles "
                                           "of hips->neck from vertical (forward = toward the batter, lateral + = away from the bowling arm)")
    path = os.path.join(d, pack + ".json")
    json.dump(base, open(path, "w", encoding="utf-8"), indent=1)
    print(f"{path}: {len(clips) - len(missing)} rebuilt clips, {len(missing)} kept from the old file {missing[:5]}")

if __name__ == "__main__":
    w = sys.argv[1] if len(sys.argv) > 1 else "fast"
    if w == "fast": merge("bowling_fast", "bowling_fast")
    else: merge("bowling", "bowling")
