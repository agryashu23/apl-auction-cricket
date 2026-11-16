"""
Cricket batting motions on the Mixamo rig (right-handed batter).
World layout (metres, Z up): batter's chest faces -Y (off side), bowler is at +X (batter's left shoulder),
leg side is +Y, keeper is at -X. Origin = between the feet at the popping crease, on the ground.
The bat is the driver: both hands are locked to its handle every frame.
"""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import bpy, math, os, sys, json, pickle
from mathutils import Vector, Matrix, Quaternion
sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import mixamo_motions as mm
from mixamo_motions import V, C, PFX, R, wpos, wrot, sample, P, pulse, two_bone, load_rig, flatten, unflatten

OUT = _os.path.join(APL_OUT, "batting")
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- bat geometry (from handle-top)
BAT_LEN, GRIP_R, GRIP_L, SWEET = 0.86, 0.17, 0.065, 0.66   # bottom hand 17 cm, top hand 6.5 cm, sweet spot 66 cm
def ortho(n, a): n = V(n); return (n - a*n.dot(a)).normalized()
def bat_g(bg, a, n):
    a = V(a).normalized(); return dict(bg=V(bg), ba=a, bn=ortho(n, a), batw=1.0)
def bat_s(S, a, n):
    """bat placed by its sweet spot S"""
    a = V(a).normalized(); return dict(bg=V(S) - a*(SWEET-GRIP_R), ba=a, bn=ortho(n, a), batw=1.0)
def bat_points(bg, a):
    top = bg - a*GRIP_R
    return top, top + a*BAT_LEN, top + a*SWEET

# ---------------------------------------------------------------- channels
FOOT_H, FOOT_FWD, ANKLE_H = 0.138, 0.107, 0.087
PITCH0 = math.degrees(math.atan2(ANKLE_H, FOOT_FWD))
GRIP = dict(T=0.55, I=0.7, M=0.72, R=0.75, P=0.78)
BASE = dict(mm.NEUTRAL,
    pyaw=0.0, ptilt=0.0, pside=0.0, legw=1.0,
    rball=V((-0.22,-0.03,0)), rtoe=V((0,-1,0)), rheel=0.0, rkp=V((0,-1,0.2)),
    lball=V((0.20,-0.03,0)), ltoe=V((0.35,-1,0)), lheel=0.0, lkp=V((0.3,-1,0.2)),
    look=V((20,0,1.6)), lookw=1.0, lean=0.0,
    bg=V((0,-0.24,0.75)), ba=V((0,0,-1)), bn=V((1,0,0)), batw=1.0,
    rpole=V((-0.3,0.4,-1)), lpole=V((0.6,0.2,-0.4)), topgrip=1.0, lefty=0.0, footup=1.0, gait=0.0,
    **mm.fingers("r", GRIP), **mm.fingers("l", GRIP))

def foot_ankle(ball, toe, heel_deg):
    t = V((toe[0], toe[1], 0)).normalized(); p = math.radians(PITCH0 + heel_deg)
    return V(ball) - t*FOOT_H*math.cos(p) + V((0,0,FOOT_H*math.sin(p)))

# ---------------------------------------------------------------- solver
class BatRig(mm.Rig):
    def reach_deficit(s, pose, prev):
        """vector (world m) the shoulders must move so both hands reach the bat grip"""
        Pos, _, err = s.solve(pose, dict(prev), write=False)
        a = V(pose["ba"]).normalized(); n = ortho(pose["bn"], a); e = a.cross(n).normalized(); f = (e + a*0.35).normalized()
        top = V(pose["bg"]) - a*GRIP_R
        tg = {"Right": top + a*GRIP_R - f*0.075 - n*0.03, "Left": top + a*GRIP_L - f*0.075 + n*0.03}
        d = V((0,0,0))
        for side in ("Right","Left"):
            sh = C.transposed() @ Pos[PFX+side+"Arm"] / 100; v = tg[side] - sh
            over = v.length - (s.L1+s.L2)/100*0.985
            if over > 0: d += v.normalized()*over
        return d
    def reach_vec(s, pose, prev):
        Pos, _, _ = s.solve(pose, dict(prev), write=False)
        a = V(pose["ba"]).normalized(); n = ortho(pose["bn"], a); e = a.cross(n).normalized()
        lefty = pose.get("lefty", 0.0) > 0.5
        f = ((-e if lefty else e) + a*0.35).normalized()
        top = V(pose["bg"]) - a*GRIP_R
        BOT, TOP = ("Left", "Right") if lefty else ("Right", "Left")
        tg = {BOT: top + a*GRIP_R - f*0.075 - n*0.03, TOP: top + a*GRIP_L - f*0.075 + n*0.03}
        out = []
        for side in ("Right","Left"):
            if side == TOP and pose.get("topgrip", 1.0) < 0.5: out += [0,0,0]; continue
            sh = C.transposed() @ Pos[PFX+side+"Arm"] / 100; v = tg[side] - sh
            over = v.length - (s.L1+s.L2)/100*0.98
            w = v.normalized()*max(0.0, over) if over > -0.02 else V((0,0,0))
            out += [w.x, w.y, w.z]
        return out
    def solve(s, pose, prev, write=True):
        pb = s.arm.pose.bones; W, Pos = {}, {}
        up_l = C @ V((0,0,1))
        # ---- bat -> hand targets (rigid grip)
        a = V(pose["ba"]).normalized(); n = ortho(pose["bn"], a)
        lefty = pose.get("lefty", 0.0) > 0.5
        e = a.cross(n).normalized(); f = ((-e if lefty else e) + a*0.35).normalized()   # finger direction, diagonal grip
        top = V(pose["bg"]) - a*GRIP_R
        gR, gL = top + a*GRIP_R, top + a*GRIP_L
        BOT, TOP = ("Left", "Right") if lefty else ("Right", "Left")
        tgt = {BOT: (gR - f*0.075 - n*0.03, f, n), TOP: (gL - f*0.075 + n*0.03, f, -n)}
        topgrip = pose.get("topgrip", 1.0)
        # ---- legs
        legs = {}
        for side, k in (("Right","r"), ("Left","l")):
            ank = foot_ankle(pose[k+"ball"], pose[k+"toe"], pose[k+"heel"])
            legs[side] = (wpos(ank), C @ V(pose[k+"kp"]), pose[k+"ball"], pose[k+"toe"])
        spineD = wrot(R('Z', pose["twist"]/3) @ R('X', pose["lean"]/3) @ R('Y', pose["side"]/3))
        add = {PFX+"Hips": wrot(R('Z', pose["pyaw"]) @ R('X', pose["ptilt"]) @ R('Y', pose["pside"])),
               PFX+"Spine": spineD, PFX+"Spine1": spineD, PFX+"Spine2": spineD,
               PFX+"RightShoulder": wrot(R('Z', pose.get("rcpro", 0.0)) @ R('Y', pose["rclav"])),
               PFX+"LeftShoulder": wrot(R('Z', -pose.get("lcpro", 0.0)) @ R('Y', -pose["lclav"]))}
        aim, err = {}, {}
        for nme in s.order:
            p = s.parent[nme]; Rr = s.Rr[nme]
            if p is None: base = Rr.copy(); pos = s.Hr[nme] + wpos(pose["hips"])
            else:
                base = W[p] @ s.Rr[p].transposed() @ Rr
                pos = Pos[p] + W[p] @ s.Rr[p].transposed() @ (s.Hr[nme]-s.Hr[p])
            Wd = base.copy(); short = nme[len(PFX):]
            side = "Right" if short.startswith("Right") else ("Left" if short.startswith("Left") else None)
            k = side[0].lower() if side else None
            if nme in add: Wd = add[nme] @ Wd
            # legs: two-bone IK + planted foot
            if side and short == side+"UpLeg":
                ankT, pole, _, _ = legs[side]
                Lt = (s.Hr[PFX+side+"Leg"]-s.Hr[nme]).length; Ls = (s.Hr[PFX+side+"Foot"]-s.Hr[PFX+side+"Leg"]).length
                kn = two_bone(pos, ankT, pole, Lt, Ls)
                aim[nme] = kn - pos; aim[PFX+side+"Leg"] = ankT - kn
                err[side+"Foot"] = max(0.0, (ankT-pos).length - (Lt+Ls-1e-3))/100
            if side and short == side+"Foot":
                _, _, ball, toe = legs[side]
                aim[nme] = wpos(ball) - pos
            if side and short == side+"ToeBase":
                toe = legs[side][3]; aim[nme] = C @ V((toe[0], toe[1], 0))
            # arms: IK to bat grip
            if side and short == side+"Arm":
                wr, _, _ = tgt[side]; wr_l = wpos(wr)
                if side == TOP and topgrip < 0.999:                       # top hand off the handle (running)
                    wr_l = wpos(pose[k+"t"]).lerp(wr_l, topgrip)
                el = two_bone(pos, wr_l, C @ V(pose[k+"pole"]), s.L1, s.L2)
                aim[nme] = el - pos; aim[PFX+side+"ForeArm"] = wr_l - el
                err[side+"Hand"] = max(0.0, (wr_l-pos).length - (s.L1+s.L2-1e-3))/100
            if nme in aim and aim[nme].length > 1e-6:
                Wd = Wd.col[1].rotation_difference(aim[nme].normalized()).to_matrix() @ Wd
            if side and short == side+"Foot":                         # keep foot upright
                ax = Wd.col[1]; v = Wd @ Rr.transposed() @ up_l; t = up_l
                v = v - ax*v.dot(ax); t = t - ax*t.dot(ax)
                if v.length > 1e-6 and t.length > 1e-6:
                    Wd = Matrix.Rotation(math.atan2(ax.dot(v.cross(t)), v.dot(t)), 3, ax) @ Wd
            if side and short == side+"Hand":                         # rigid bat grip orientation
                _, fdir, palm = tgt[side]
                Wfree = Wd.copy()
                Wd = Wd.col[1].rotation_difference((C @ fdir).normalized()).to_matrix() @ Wd
                ax = Wd.col[1]; v = Wd @ Rr.transposed() @ V((0,-1,0)); t = C @ palm
                v = v - ax*v.dot(ax); t = t - ax*t.dot(ax)
                Wd = Matrix.Rotation(math.atan2(ax.dot(v.cross(t)), v.dot(t)), 3, ax) @ Wd
                if side == TOP and topgrip < 0.999:
                    Wd = Wfree.to_quaternion().slerp(Wd.to_quaternion(), topgrip).to_matrix()
            if nme in s.curl:                                         # fingers wrap the handle
                axc, deg, letter = s.curl[nme]
                Wd = base @ (Rr.transposed() @ Matrix.Rotation(math.radians(deg*pose[k+letter]), 3, axc) @ Rr)
            if short in ("Neck", "Head") and pose["lookw"] > 0:       # look-at, eyes kept level
                fwd = Wd @ Rr.transposed() @ (C @ V((0,-1,0)))
                eye = pos + up_l*(12 if short == "Neck" else 9)
                t = (wpos(pose["look"]) - eye).normalized()
                q = fwd.rotation_difference(t); w = pose["lookw"]*(0.45 if short == "Neck" else 1.0)
                Wd = Quaternion().slerp(q, w).to_matrix() @ Wd
                if short == "Head":
                    fwd = Wd @ Rr.transposed() @ (C @ V((0,-1,0))); lft = Wd @ Rr.transposed() @ V((1,0,0))
                    ldes = up_l.cross(fwd)
                    if ldes.length > 1e-4:
                        ax = fwd.normalized(); v = lft - ax*lft.dot(ax); t = ldes - ax*ldes.dot(ax)
                        Wd = Matrix.Rotation(0.75*math.atan2(ax.dot(v.cross(t)), v.dot(t)), 3, ax) @ Wd
            W[nme], Pos[nme] = Wd, pos
            q = (base.transposed() @ Wd).to_quaternion()
            if nme in prev and q.dot(prev[nme]) < 0: q.negate()
            prev[nme] = q
            if write: pb[nme].rotation_quaternion = q
        if write: pb[PFX+"Hips"].location = pb[PFX+"Hips"].bone.matrix_local.to_3x3().transposed() @ wpos(pose["hips"])
        # achieved grip error (hands vs bat)
        for side in ("Right","Left"):
            err[side+"Grip"] = (Pos[PFX+side+"Hand"] - wpos(tgt[side][0])).length/100 if (side == BOT or topgrip > 0.5) else 0.0
        tails = {n_: Pos[n_] + W[n_].col[1]*s.len[n_] for n_ in Pos}
        return Pos, tails, err

def step_lift(keys, f, pose):
    """lift a foot while it travels, so it never slides along the ground"""
    if pose.get("gait", 0.0) > 0.999: return            # pure gait frames already carry their own foot arcs
    for k in ("r", "l"):
        a = sample(keys, max(0, f-1))[k+"ball"]; b = sample(keys, f+1)[k+"ball"]
        v = (V((b[0],b[1],0)) - V((a[0],a[1],0))).length / 2
        if v > 0.004:
            pose[k+"ball"] = pose[k+"ball"] + V((0,0,min(0.13, 2.2*v)))
            pose[k+"heel"] += min(15, 250*v)

def make_bat(name, nframes, keys, contact=None, meta=None, overlay=None):
    arm, _ = load_rig(); rig = BatRig(arm); scn = bpy.context.scene
    scn.frame_start, scn.frame_end = 0, nframes-1
    prev, frames, worst = {}, [], {}
    info = {}
    poses = []
    for f in range(nframes):
        pose = sample(keys, f); step_lift(keys, f, pose)
        if overlay: overlay(f, pose)
        poses.append(pose)
    # reach assist: bend the spine toward the bat where the arms are too short, then smooth over time
    extra = []
    pv = {}
    import numpy as np
    LIM = [(-30, 40), (-35, 35), (-40, 40), (-0.15, 0.0), (-0.12, 0.12), (-0.10, 0.10), (-15, 25), (-15, 25), (-25, 25), (-25, 25)]
    STEP = [3.0, 3.0, 3.0, 0.03, 0.03, 0.03, 4.0, 4.0, 4.0, 4.0]
    ND = len(LIM)
    def apply(pose, x):
        return dict(pose, lean=pose["lean"]+x[0], side=pose["side"]+x[1], twist=pose["twist"]+x[2],
                    hips=pose["hips"]+V((x[4],x[5],x[3])), rclav=pose["rclav"]+x[6], lclav=pose["lclav"]+x[7],
                    rcpro=pose.get("rcpro",0.0)+x[8], lcpro=pose.get("lcpro",0.0)+x[9])
    for pose in poses:
        x = np.zeros(ND)
        for it in range(10):
            r = np.array(rig.reach_vec(apply(pose, x), pv))
            if np.linalg.norm(r) < 0.004: break
            J = np.zeros((6, ND))
            for j in range(ND):
                xd = x.copy(); xd[j] += STEP[j]
                J[:, j] = (np.array(rig.reach_vec(apply(pose, xd), pv)) - r) / STEP[j]
            reg = np.diag([0.0001, 0.0001, 0.0002, 0.6, 0.4, 0.5, 0.0002, 0.0002, 0.0002, 0.0002])
            dx = np.linalg.solve(J.T @ J + reg, -J.T @ r)
            x = np.array([min(max(x[j]+dx[j], LIM[j][0]), LIM[j][1]) for j in range(ND)])
        extra.append(tuple(x))
    def smooth(vals, w=4):
        out = []
        for i in range(len(vals)):
            ws = [(math.exp(-((j-i)/w)**2/2), vals[j]) for j in range(max(0,i-3*w), min(len(vals), i+3*w+1))]
            tot = sum(x for x, _ in ws); out.append(sum(x*v for x, v in ws)/tot)
        return out
    # take the envelope (max |value|) before smoothing so smoothing doesn't under-reach
    for comp in range(ND):
        raw = [e_[comp] for e_ in extra]
        env = [max(raw[max(0,i-3):i+4], key=abs) for i in range(len(raw))]
        sm = smooth(env)
        for i in range(len(extra)):
            extra[i] = tuple(sm[i] if c == comp else extra[i][c] for c in range(ND))
    if (meta or {}).get("loop"):                                   # loops: last frame identical to the first
        poses[-1] = dict(poses[0]); extra[-1] = extra[0]
    for f in range(nframes):
        poses[f] = apply(poses[f], extra[f])
    # bat pull-in: anything the body still can't reach, the bat comes toward the hands (smoothed)
    shifts = []
    for f in range(nframes):
        sh = V((0,0,0))
        for it in range(4):
            r = rig.reach_vec(dict(poses[f], bg=V(poses[f]["bg"])-sh), pv)
            dv = V(r[0:3]) + V(r[3:6])
            if dv.length < 0.003: break
            sh += dv
        shifts.append(sh)
    for comp in range(3):
        raw = [s_[comp] for s_ in shifts]
        env = [max(raw[max(0,i-3):i+4], key=abs) for i in range(len(raw))]
        sm = smooth(env, 3)
        for i in range(nframes): shifts[i] = V(tuple(sm[i] if c == comp else shifts[i][c] for c in range(3)))
    if (meta or {}).get("loop"): shifts[-1] = shifts[0]
    for f in range(nframes):
        poses[f]["bg"] = V(poses[f]["bg"]) - shifts[f]
    for f in range(nframes):
        pose = poses[f]
        Pos, tails, err = rig.solve(pose, prev)
        for k_, v_ in err.items():
            if v_ > worst.get(k_, 0): worst[k_] = v_; worst[k_+"@"] = f
        a = V(pose["ba"]).normalized(); top, toe, sw = bat_points(pose["bg"], a)
        frames.append(({k: tuple(C.transposed() @ v / 100) for k, v in Pos.items()},
                       {k: tuple(C.transposed() @ v / 100) for k, v in tails.items()},
                       (tuple(top), tuple(toe), tuple(sw))))
        if contact is not None and f == contact:
            info = dict(sweet_spot_at_contact=[round(x,3) for x in sw], bat_face_at_contact=[round(x,3) for x in ortho(pose["bn"], a)])
        for pbn in arm.pose.bones:
            pbn.keyframe_insert("rotation_quaternion", frame=f); pbn.keyframe_insert("location", frame=f)
    for pbn in arm.pose.bones:
        pbn.rotation_quaternion = Quaternion(); pbn.location = V((0,0,0))
        pbn.keyframe_insert("rotation_quaternion", frame=-1); pbn.keyframe_insert("location", frame=-1)
    scn.frame_set(-1)
    act = arm.animation_data.action; act.name = name
    for fc in act.fcurves:
        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name+".fbx"), object_types={'ARMATURE'},
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    md = dict(file=name+".fbx", frames=nframes, fps=30, contact_frame=(contact+1 if contact is not None else None), **info, **(meta or {}))
    return frames, worst, md
