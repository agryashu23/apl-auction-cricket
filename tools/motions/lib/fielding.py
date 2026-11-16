"""Fielding motions on the Mixamo rig. World: Z up, fielder faces -Y at the start (Mixamo forward),
fielder's LEFT is +X. Origin = ground between the feet on frame 1. Batter/ball come from -Y."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, os, json, pickle; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import bpy
from mathutils import Vector, Matrix, Quaternion
import mixamo_motions as mm
from mixamo_motions import V, C, PFX, R, wpos, wrot, sample, P, pulse, two_bone, load_rig, flatten, unflatten
from batting import foot_ankle, PITCH0, FOOT_H

OUTF = _os.path.join(APL_OUT, "fielding"); os.makedirs(OUTF, exist_ok=True)
HIP0 = 1.043
def H(x, y, z): return V((x, y, z - HIP0))              # hips: give the world height, store the offset

RELAXF = dict(T=0.25, I=0.22, M=0.28, R=0.33, P=0.38)
CATCHF = dict(T=0.15, I=0.12, M=0.14, R=0.16, P=0.18)    # open, soft hands waiting for the ball
HOLDF = dict(T=0.55, I=0.55, M=0.6, R=0.62, P=0.65)      # closed around the ball
BASE = dict(mm.NEUTRAL,
    pyaw=0.0, ptilt=0.0, pside=0.0, footup=1.0,
    rball=V((-0.17,-0.06,0)), rtoe=V((-0.1,-1,0)), rheel=0.0, rkp=V((0,-1,0.1)),
    lball=V((0.17,-0.06,0)), ltoe=V((0.1,-1,0)), lheel=0.0, lkp=V((0,-1,0.1)),
    look=V((0,-20,0.9)), lookw=1.0, rdir=V((0,0,-1)), ldir=V((0,0,-1)), rdirw=0.0, ldirw=0.0, gait=0.0,
    rw=1.0, lw=1.0, rt=V((-0.22,-0.05,0.95)), lt=V((0.22,-0.05,0.95)), rp=V((-0.4,1,-0.2)), lp=V((0.4,1,-0.2)),
    **mm.fingers("r", RELAXF), **mm.fingers("l", RELAXF))
def fing(side, shape, w=1.0): return mm.fingers(side, shape, w)

class FieldRig(mm.Rig):
    def solve(s, pose, prev, write=True):
        pb = s.arm.pose.bones; W, Pos = {}, {}
        up_l = C @ V((0,0,1))
        legs = {}
        for side, k in (("Right","r"), ("Left","l")):
            ank = foot_ankle(pose[k+"ball"], pose[k+"toe"], pose[k+"heel"])
            legs[side] = (wpos(ank), C @ V(pose[k+"kp"]), pose[k+"ball"], pose[k+"toe"])
        spineD = wrot(R('Z', pose["twist"]/3) @ R('X', pose["lean"]/3) @ R('Y', pose["side"]/3))
        add = {PFX+"Hips": wrot(R('Z', pose["pyaw"]) @ R('X', pose["ptilt"]) @ R('Y', pose["pside"])),
               PFX+"Spine": spineD, PFX+"Spine1": spineD, PFX+"Spine2": spineD,
               PFX+"RightShoulder": wrot(R('Y', pose["rclav"])), PFX+"LeftShoulder": wrot(R('Y', -pose["lclav"]))}
        aim = {}
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
            if side and short == side+"UpLeg":
                ankT, pole, _, _ = legs[side]
                Lt = (s.Hr[PFX+side+"Leg"]-s.Hr[nme]).length; Ls = (s.Hr[PFX+side+"Foot"]-s.Hr[PFX+side+"Leg"]).length
                kn = two_bone(pos, ankT, pole, Lt, Ls); aim[nme] = kn - pos; aim[PFX+side+"Leg"] = ankT - kn
            if side and short == side+"Foot": aim[nme] = wpos(legs[side][2]) - pos
            if side and short == side+"ToeBase":
                t = legs[side][3]; aim[nme] = C @ V((t[0], t[1], t[2] if len(t) > 2 else 0))
            if side and short == side+"Arm":
                wr = wpos(pose[k+"t"])
                dw = pose.get(k+"dirw", 0.0)
                if dw > 1e-4:                                    # straight arm pointing along a world direction from the shoulder
                    dv = C @ V(pose[k+"dir"]); dv = dv.normalized() if dv.length > 1e-6 else C @ V((0,0,-1))
                    wr = wr.lerp(pos + dv*(s.L1+s.L2)*0.9992, dw)
                el = two_bone(pos, wr, C @ V(pose[k+"p"]), s.L1, s.L2)
                aim[nme] = el - pos; aim[PFX+side+"ForeArm"] = wr - el
            if nme in aim and aim[nme].length > 1e-6:
                Wd = Wd.col[1].rotation_difference(aim[nme].normalized()).to_matrix() @ Wd
            if side and short == side+"Foot" and pose["footup"] > 0:
                ax = Wd.col[1]; v = Wd @ Rr.transposed() @ up_l; t = up_l
                v = v - ax*v.dot(ax); t = t - ax*t.dot(ax)
                if v.length > 1e-6 and t.length > 1e-6:
                    Wd = Matrix.Rotation(pose["footup"]*math.atan2(ax.dot(v.cross(t)), v.dot(t)), 3, ax) @ Wd
            if side and short == side+"Hand":
                w = pose[k+"hw"]
                if w > 1e-4:
                    cur = Wd.col[1]; d = (cur*(1-w) + (C @ V(pose[k+"h"])).normalized()*w).normalized()
                    Wd = cur.rotation_difference(d).to_matrix() @ Wd
                w2 = pose[k+"palw"]
                if w2 > 1e-4:
                    ax = Wd.col[1]; v = Wd @ Rr.transposed() @ V((0,-1,0)); t = C @ V(pose[k+"pal"])
                    v = v - ax*v.dot(ax); t = t - ax*t.dot(ax)
                    if v.length > 1e-6 and t.length > 1e-6:
                        Wd = Matrix.Rotation(w2*math.atan2(ax.dot(v.cross(t)), v.dot(t)), 3, ax) @ Wd
            if nme in s.curl:
                axc, deg, letter = s.curl[nme]
                Wd = base @ (Rr.transposed() @ Matrix.Rotation(math.radians(deg*pose[k+letter]), 3, axc) @ Rr)
            if short in ("Neck", "Head") and pose["lookw"] > 0:
                fwd = Wd @ Rr.transposed() @ (C @ V((0,-1,0)))
                eye = pos + (Wd @ Rr.transposed() @ up_l)*(12 if short == "Neck" else 9)
                t = (wpos(pose["look"]) - eye).normalized()
                q = fwd.rotation_difference(t); w = pose["lookw"]*(0.45 if short == "Neck" else 1.0)
                Wd = Quaternion().slerp(q, w).to_matrix() @ Wd
                if short == "Head":
                    fwd = Wd @ Rr.transposed() @ (C @ V((0,-1,0))); lft = Wd @ Rr.transposed() @ V((1,0,0))
                    ldes = up_l.cross(fwd)
                    if ldes.length > 0.3:
                        ax = fwd.normalized(); v = lft - ax*lft.dot(ax); t = ldes - ax*ldes.dot(ax)
                        Wd = Matrix.Rotation(0.5*math.atan2(ax.dot(v.cross(t)), v.dot(t)), 3, ax) @ Wd
            W[nme], Pos[nme] = Wd, pos
            q = (base.transposed() @ Wd).to_quaternion()
            if nme in prev and q.dot(prev[nme]) < 0: q.negate()
            prev[nme] = q
            if write: pb[nme].rotation_quaternion = q
        if write: pb[PFX+"Hips"].location = pb[PFX+"Hips"].bone.matrix_local.to_3x3().transposed() @ wpos(pose["hips"])
        tails = {n_: Pos[n_] + W[n_].col[1]*s.len[n_] for n_ in Pos}
        return Pos, tails

def reach_vec(rig, pose, prev):
    Pos, _ = rig.solve(pose, dict(prev), write=False)
    out = []
    for side, k in (("Right","r"), ("Left","l")):
        if pose[k+"w"] < 0.5 or not pose.get(k+"reach", 1.0) or pose.get(k+"dirw", 0.0) > 0.3: out += [0,0,0]; continue
        sh = C.transposed() @ Pos[PFX+side+"Arm"] / 100; v = V(pose[k+"t"]) - sh
        over = v.length - (rig.L1+rig.L2)/100*0.98
        w = v.normalized()*max(0.0, over) if over > -0.02 else V((0,0,0))
        out += [w.x, w.y, w.z]
    return out

def reach_assist(rig, poses, active):
    """bend / drop / turn the body so the hands get to their targets, smoothed over time"""
    import numpy as np
    LIM = [(-30, 70), (-40, 40), (-40, 40), (-0.45, 0.10), (-0.15, 0.15), (-0.15, 0.15), (-15, 25), (-15, 25)]
    STEP = [3.0, 3.0, 3.0, 0.03, 0.03, 0.03, 4.0, 4.0]
    def apply(p, x):
        return dict(p, lean=p["lean"]+x[0], side=p["side"]+x[1], twist=p["twist"]+x[2],
                    hips=V(p["hips"])+V((x[4], x[5], x[3])), rclav=p["rclav"]+x[6], lclav=p["lclav"]+x[7])
    extra = []
    for f, p in enumerate(poses):
        x = np.zeros(8)
        if active(f) and p.get("gait", 0.0) < 0.5:
            for it in range(10):
                r = np.array(reach_vec(rig, apply(p, x), {}))
                if np.linalg.norm(r) < 0.004: break
                J = np.zeros((6, 8))
                for j in range(8):
                    xd = x.copy(); xd[j] += STEP[j]
                    J[:, j] = (np.array(reach_vec(rig, apply(p, xd), {})) - r) / STEP[j]
                reg = np.diag([0.0001, 0.0002, 0.0003, 0.3, 0.5, 0.5, 0.0003, 0.0003])
                dx = np.linalg.solve(J.T @ J + reg, -J.T @ r)
                x = np.array([min(max(x[j]+dx[j], LIM[j][0]), LIM[j][1]) for j in range(8)])
        extra.append(x)
    def smooth(vals, w=3):
        out = []
        for i in range(len(vals)):
            ws = [(math.exp(-((j-i)/w)**2/2), vals[j]) for j in range(max(0,i-3*w), min(len(vals), i+3*w+1))]
            tot = sum(a for a, _ in ws); out.append(sum(a*v for a, v in ws)/tot)
        return out
    for c in range(8):
        raw = [e[c] for e in extra]
        env = [max(raw[max(0,i-3):i+4], key=abs) for i in range(len(raw))]
        sm = smooth(env)
        for i in range(len(extra)): extra[i][c] = sm[i]
    return [apply(p, x) for p, x in zip(poses, extra)]

def step_lift_f(keys, f, pose, gain=2.4, cap=0.16):
    if pose.get("gait", 0.0) > 0.999: return            # pure gait frames already carry their own foot arcs
    for k in ("r", "l"):
        a = sample(keys, max(0, f-1))[k+"ball"]; b = sample(keys, f+1)[k+"ball"]
        v = (V((b[0],b[1],0)) - V((a[0],a[1],0))).length / 2
        if v > 0.004 and pose[k+"ball"][2] < 0.05:
            pose[k+"ball"] = pose[k+"ball"] + V((0,0,min(cap, gain*v)))
            pose[k+"heel"] += min(25, 300*v)

def make_field(name, nframes, keys, *, overlay=None, loop=False, lift=True, meta=None, reach=None):
    arm, _ = load_rig(); rig = FieldRig(arm); scn = bpy.context.scene
    scn.frame_start, scn.frame_end = 0, nframes-1
    prev, frames = {}, []
    poses = []
    for f in range(nframes):
        ff = f if not (loop and f == nframes-1) else 0
        pose = sample(keys, ff)
        if lift: step_lift_f(keys, ff, pose)
        if overlay: overlay(ff, pose)
        poses.append(pose)
    if reach:
        poses = reach_assist(rig, poses, reach if callable(reach) else (lambda f: True))
        if loop: poses[-1] = dict(poses[0])
    errs = []
    for f in range(nframes):
        pose = poses[f]
        Pos, tails = rig.solve(pose, prev)
        e = 0.0
        for side, k in (("Right","r"), ("Left","l")):
            if pose[k+"w"] > 0.5 and pose.get(k+"dirw", 0.0) <= 0.3: e = max(e, (C.transposed() @ Pos[PFX+side+"Hand"]/100 - V(pose[k+"t"])).length)
        errs.append(e)
        frames.append(({k: tuple(C.transposed() @ v / 100) for k, v in Pos.items()},
                       {k: tuple(C.transposed() @ v / 100) for k, v in tails.items()}))
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
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUTF, name+".fbx"), object_types={'ARMATURE'},
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    make_field.last_errs = errs
    return frames

# ---------------------------------------------------------------- footstep / running generator
def fwd(h): r = math.radians(h); return V((math.sin(r), -math.cos(r), 0))       # facing direction for heading h (0 = -Y)
def left(h): r = math.radians(h); return V((math.cos(r), math.sin(r), 0))

def run_keys(base, *, f0, f1, p0, p1, heading, period, first="l", lean=14, contact=0.38, bob=0.035,
             arm_amp=0.22, hip_h=0.98, backwards=False, sideways=0, style="run", extra=None, ease=None, start_feet=None):
    """Per-frame gait between frames f0..f1 moving p0 -> p1 (ground xy) while facing `heading`.
    Planted feet never slide; swing feet follow a speed-dependent arc (heel recovery + knee drive); hips ride tall and
    oscillate (dip at mid-stance when running, vault over the stance leg when walking); pelvis/shoulders counter-rotate;
    arms swing with the opposite leg. Returns (keys, plants)."""
    p0, p1 = V((p0[0], p0[1], 0)), V((p1[0], p1[1], 0))
    step = period / 2.0
    nsteps = max(1, int(round((f1 - f0) / step)))
    step = (f1 - f0) / nsteps
    F, Lft = fwd(heading), left(heading)
    move = p1 - p0
    speed = move.length / max(1e-6, (f1 - f0)/30.0)
    walking = contact >= 0.5
    shuffle = style == "shuffle"
    # --- gait parameters by speed
    if shuffle:                     hip_mean, hip_amp, swing_h, heel_up, arm_mode = 0.99, 0.012, 0.06, 15, "low"
    elif walking:                   hip_mean, hip_amp, swing_h, heel_up, arm_mode = 0.995, 0.012, 0.09, 30, "walk"
    elif speed < 4.2 or backwards:  hip_mean, hip_amp, swing_h, heel_up, arm_mode = 1.005, 0.028, 0.34, 45, "run"
    else:                           hip_mean, hip_amp, swing_h, heel_up, arm_mode = 1.005, 0.034, 0.30 + 0.05*min(speed, 8.0), 55, "run"
    if backwards: swing_h = 0.16
    stance_travel = speed * contact * period / 30.0                     # how far the body moves while a foot is planted
    td_ahead = 0.0 if (backwards or shuffle) else stance_travel*0.48    # land ahead, leave behind: stance centred under the body
    def at(t):
        u = (t - f0) / (f1 - f0)
        if ease == "accel": u = max(0, u)**1.7 if u >= 0 else 0.0
        elif ease == "decel": u = 1 - (1 - min(1, u))**1.7 if u <= 1 else 1.0
        return p0 + move*u
    mdir = move.normalized() if move.length > 1e-4 else F
    # continuing straight on from the previous run segment? then keep its footsteps instead of re-planning them
    prev = run_keys.last
    chained = prev is not None and abs(prev[0] - f0) < 0.51 and (V((prev[1][0], prev[1][1], 0)) - p0).length < 0.02
    if chained:
        at_join = [pp for pp in prev[2] if abs(pp[0] - f0) < 0.51]
        if at_join: first = at_join[0][2]
    plants = []
    for i in range(-3, nsteps + 4):
        if chained and i < 0: continue
        foot = first if i % 2 == 0 else ("r" if first == "l" else "l")
        td = f0 + i*step
        if shuffle:
            pos = at(td) + Lft*(0.17 if foot == "l" else -0.17)
        else:
            pos = at(td) + mdir*td_ahead + Lft*(0.09 if foot == "l" else -0.09)
        if chained and i == 0:
            same = [pp for pp in prev[2] if abs(pp[0] - f0) < 0.51 and pp[2] == foot]
            if same: pos = same[0][3]
        plants.append((td, td + contact*period, foot, pos))
    if chained:
        plants = [pp for pp in prev[2] if pp[0] < f0 - 0.51] + plants
        plants.sort(key=lambda pp: pp[0])
    elif start_feet:                                   # standing start: the feet begin exactly where the stance had them
        plants = [(td, lo, fo, (V((start_feet[fo][0], start_feet[fo][1], 0)) if td <= f0 + 0.01 else pos)) for td, lo, fo, pos in plants]
    def foot_state(fo, t):
        mine = [p for p in plants if p[2] == fo]
        for j, (td, lo, _, pos) in enumerate(mine):
            if td - 1e-6 <= t <= lo + 1e-6:                                        # stance
                s = (t - td) / max(1e-6, lo - td)
                return pos, 0.0, heel_up*max(0.0, (s - 0.55)/0.45)**1.5, True, s
            if j + 1 < len(mine) and lo < t < mine[j+1][0]:                       # swing
                nxt = mine[j+1]; s = (t - lo) / max(1e-6, nxt[0] - lo)
                u = s*s*s*(s*(6*s - 15) + 10)                      # smootherstep: foot folds under the hips mid-swing, reaches forward late, ~stationary at touchdown
                horiz = pos.lerp(nxt[3], u)
                h = swing_h*math.sin(math.pi*min(1.0, s*1.08))**1.2 * (1.0 - 0.25*s)
                heel = heel_up*(1 - s)**1.5 + (40 if not walking else 10)*math.sin(math.pi*s)*(1 - s) - 8*s*s
                return horiz + V((0, 0, max(0.0, h))), max(0.0, h), heel, False, s
        return mine[-1][3], 0.0, 0.0, True, 0.5
    frames = sorted(set([float(x) for x in range(int(math.ceil(f0)), int(math.floor(f1)) + 1)] + [float(f0), float(f1)]))
    keys = []
    for t in frames:
        g = at(t)
        rb, rh_, rheel, rst, rs = foot_state("r", t)
        lb, lh_, lheel, lst, ls = foot_state("l", t)
        # hips: running dips at mid-stance and rises in flight; walking vaults over the stance leg
        sts = [s_ for st_, s_ in ((rst, rs), (lst, ls)) if st_]
        if walking or shuffle:
            single = len(sts) == 1
            z = hip_mean + (hip_amp*math.sin(math.pi*sts[0]) if single else -hip_amp*0.6)
        else:
            z = hip_mean - hip_amp*math.sin(math.pi*sts[0]) if sts else hip_mean + hip_amp*0.6
        # sway toward the stance foot; pelvis turns with the swinging leg, shoulders counter-rotate
        dl = ((lb - rb).dot(mdir if not backwards else -mdir)) / max(0.2, (2*step/30.0)*max(speed, 0.5))
        dl = max(-1.0, min(1.0, dl))
        sway = 0.0
        if rst and not lst: sway = -0.015 if not walking else -0.025
        if lst and not rst: sway = 0.015 if not walking else 0.025
        pel = (7 if not walking else 5) * dl * (0 if shuffle else 1)
        hips = g + Lft*sway + V((0, 0, z))
        hw = V((hips.x, hips.y, 0))
        # arms: right arm forward when the left leg is forward
        if arm_mode == "run":
            amp = max(arm_amp, 0.16)
            r_off, l_off = dl*amp, -dl*amp
            rt_ = hw + Lft*(-0.20) + F*(0.06 + r_off) + V((0, 0, z + 0.12 + 0.10*max(0, dl)))
            lt_ = hw + Lft*(0.20) + F*(0.06 + l_off) + V((0, 0, z + 0.12 + 0.10*max(0, -dl)))
            rp_ = Lft*(-0.4) - F*1.0 + V((0, 0, -0.4)); lp_ = Lft*0.4 - F*1.0 + V((0, 0, -0.4))
        elif arm_mode == "walk":
            amp = 0.13
            rt_ = hw + Lft*(-0.22) + F*(0.02 + dl*amp) + V((0, 0, z - 0.14))
            lt_ = hw + Lft*(0.22) + F*(0.02 - dl*amp) + V((0, 0, z - 0.14))
            rp_ = Lft*(-0.3) - F*1.0 + V((0, 0, -0.5)); lp_ = Lft*0.3 - F*1.0 + V((0, 0, -0.5))
        else:
            rt_ = hw + Lft*(-0.22) + F*0.30 + V((0, 0, z - 0.05)); lt_ = hw + Lft*0.22 + F*0.30 + V((0, 0, z - 0.05))
            rp_ = Lft*(-0.6) + V((0, 0, -1)); lp_ = Lft*0.6 + V((0, 0, -1))
        ft = F*(-1 if backwards else 1)
        k = P(base, hips=H(hips.x, hips.y, hips.z), pyaw=heading + pel, twist=-pel*1.3, lean=0, side=0,
              ptilt=lean*(0.35 if backwards else 1.0), pside=0.0,
              rball=rb, lball=lb, rtoe=F, ltoe=F, rheel=rheel, lheel=lheel,
              rkp=F + V((0, 0, 0.15)), lkp=F + V((0, 0, 0.15)),
              rt=rt_, lt=lt_, rp=rp_, lp=lp_, rh=F + V((0, 0, 0.3)), rhw=0.3, lh=F + V((0, 0, 0.3)), lhw=0.3,
              look=g + ft*20 + V((0, 0, 1.0)), gait=1.0)
        if extra:
            k = P(k, **{a: b for a, b in extra.items() if a != "look"})
            if "look" in extra: k["look"] = extra["look"]
        keys.append((t, k))
    run_keys.last = (f1, (p1.x, p1.y), plants)
    return keys, plants

run_keys.last = None
def inplace(keys, f0, f1, p0, p1):
    """convert a root-motion run to in-place (treadmill) - subtract the travel"""
    p0, p1 = V((p0[0], p0[1], 0)), V((p1[0], p1[1], 0))
    out = []
    for t, k in keys:
        u = (t - f0) / (f1 - f0); g = p0.lerp(p1, u) - p0
        k2 = dict(k); k2["hips"] = V(k["hips"]) - g
        for c in ("rball", "lball", "rt", "lt", "look"): k2[c] = V(k[c]) - g
        out.append((t, k2))
    return out
