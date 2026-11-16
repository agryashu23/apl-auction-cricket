import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, json, pickle, os, math; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import shots74
import shots
import batting
from batting import make_bat, V
from running_bat import mirror_y
OUTL = _os.path.join(APL_OUT, "batting74_left_handed"); os.makedirs(OUTL, exist_ok=True)
batting.OUT = OUTL

if __name__ == "__main__":
    ids = sys.argv[1:]
    data, metas = {}, []
    for tok in ids:
        if tok == "idle":
            n, keys, contact, meta = shots.SHOTS["stance_idle_loop"]
            keys = [(t, mirror_y(k)) for t, k in keys]
            frames, worst, md = make_bat("lhb_00_stance_idle_loop", n, keys, None, dict(loop=True))
            data["idle"] = frames
            metas.append(dict(id=0, file="lhb_00_stance_idle_loop.fbx", name="Stance idle (loop)", frames=n, loop=True))
            print("idle OK", flush=True); continue
        sid = int(tok); sh = shots74.SHOTS74[sid]
        fname = f"lhb_{sid:02d}_{shots74.slug(sh['name'])}"
        keys = [(t, mirror_y(k)) for t, k in sh["keys"]]
        frames, worst, md = make_bat(fname, sh["n"], keys, sh["contact"], dict(loop=False))
        bad = {k: round(v, 3) for k, v in worst.items() if not k.endswith("@") and k.endswith("Grip") and v > 0.012}
        data[sid] = frames
        metas.append(dict(id=sid, file=fname+".fbx", name=sh["name"], angle_deg=sh["angle"], field_angle_deg=(360 - sh["angle"]) % 360,
                          frames=sh["n"], contact_frame=sh["contact"]+1, lofted=sh["lofted"],
                          sweet_spot_at_contact=md.get("sweet_spot_at_contact"), bat_face_at_contact=md.get("bat_face_at_contact"), **sh["info"]))
        print(sid, fname, "OK" if not bad else bad, flush=True)
    tag = "_".join(ids[:1] + ids[-1:])
    pickle.dump(data, open(f_os.path.join(APL_WORK, "lhb_frames_{tag}.pkl"), "wb")); json.dump(metas, open(f_os.path.join(APL_WORK, "lhb_meta_{tag}.json"), "w"), indent=1)
