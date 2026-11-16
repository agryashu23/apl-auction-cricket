"""
Generates motions on the exact Mixamo skeleton from the uploaded Sitting_Talking.fbx
(65 bones, 'mixamorig:' names, full fingers + end bones, cm units, 30 fps, every frame keyed).
Seated motions: the uploaded clip's mocap is the base layer; gestures are layered on top.
Authoring is in world metres (Z-up, character faces -Y); converted to the rig's local space.
"""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import bpy, math, os, sys, pickle
from mathutils import Vector, Matrix, Quaternion

SRC = APL_RIG
OUT = APL_OUT
FPS = 30
V = Vector
PFX = "mixamorig:"
C = Matrix(((1,0,0),(0,0,1),(0,-1,0)))          # world dir -> rig-local dir
def wpos(v): return C @ V(v) * 100.0              # world metres -> rig-local cm
def wrot(D): return C @ D @ C.transposed()
def R(axis, deg): return Matrix.Rotation(math.radians(deg), 3, axis)

# ---------------------------------------------------------------- load rig + mocap base
def load_rig(read_mocap=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    scn = bpy.context.scene; scn.render.fps, scn.render.fps_base = FPS, 1.0
    arm = [o for o in scn.objects if o.type == 'ARMATURE'][0]
    mocap = None
    if read_mocap:
        act = arm.animation_data.action
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        mocap = []
        for f in range(f0, f1+1):
            scn.frame_set(f)
            mocap.append(({p.name: p.matrix_basis.to_quaternion() for p in arm.pose.bones},
                          arm.pose.bones[PFX+"Hips"].matrix_basis.to_translation()))
    arm.animation_data_clear()
    for p in arm.pose.bones:
        p.rotation_mode = 'QUATERNION'; p.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    return arm, mocap

# ---------------------------------------------------------------- key-pose interpolation
def flatten(p):
    out = {}
    for k, v in p.items():
        if isinstance(v, (Vector, tuple, list)):
            for i, c in enumerate(v): out[f"{k}.{i}"] = float(c)
        else: out[k] = float(v)
    return out
def unflatten(flat):
    out = {}
    for k, v in flat.items():
        if "." in k: n, i = k.split("."); out.setdefault(n, [0,0,0])[int(i)] = v
        else: out[k] = v
    return {k: (V(v) if isinstance(v, list) else v) for k, v in out.items()}
def sample(keys, f):
    ts = [k[0] for k in keys]; ps = [flatten(k[1]) for k in keys]
    if f <= ts[0]: return unflatten(ps[0])
    if f >= ts[-1]: return unflatten(ps[-1])
    i = max(j for j in range(len(ts)-1) if ts[j] <= f)
    t0, t1 = ts[i], ts[i+1]; h = t1-t0; s = (f-t0)/h
    def tan(j, ch):
        if j == 0 or j == len(ts)-1: return 0.0
        a, b, c = ps[j-1][ch], ps[j][ch], ps[j+1][ch]
        if (b-a)*(c-b) <= 0: return 0.0
        return (c-a)/(ts[j+1]-ts[j-1])
    h00, h10, h01, h11 = 2*s**3-3*s**2+1, s**3-2*s**2+s, -2*s**3+3*s**2, s**3-s**2
    return unflatten({ch: h00*ps[i][ch]+h10*h*tan(i,ch)+h01*ps[i+1][ch]+h11*h*tan(i+1,ch) for ch in ps[i]})
def P(base, **kw):
    d = dict(base); d.update({k: (V(v) if isinstance(v, tuple) else v) for k, v in kw.items()}); return d
def pulse(f, c, w):
    x = (f-c)/w; return 0.0 if abs(x) >= 1 else 0.5*(1+math.cos(math.pi*x))

# ---------------------------------------------------------------- channels
NEUTRAL = dict(
    hips=V((0,0,0)), lean=0.0, twist=0.0, side=0.0,
    npitch=0.0, nyaw=0.0, nroll=0.0, hpitch=0.0, hyaw=0.0, hroll=0.0, rclav=0.0, lclav=0.0,
    rw=0.0, rt=V((0,0,1)), rp=V((0,1,0)), lw=0.0, lt=V((0,0,1)), lp=V((0,1,0)),   # IK weight/target/pole
    rh=V((0,0,-1)), rhw=0.0, lh=V((0,0,-1)), lhw=0.0, rhtw=0.0, lhtw=0.0,          # hand aim/weight/twist
    rpal=V((0,-1,0)), rpalw=0.0, lpal=V((0,-1,0)), lpalw=0.0,                       # palm-facing direction
    rthu=V((0,0,1)), rthuw=0.0, lthu=V((0,0,1)), lthuw=0.0,                         # thumb-pointing direction
    rfw=0.0, rT=0.3, rI=0.3, rM=0.3, rR=0.3, rP=0.3,                                # finger curls (0..1) + weight
    lfw=0.0, lT=0.3, lI=0.3, lM=0.3, lR=0.3, lP=0.3,
)
FIST = dict(T=0.9, I=1.0, M=1.0, R=1.0, P=1.0)
RELAX = dict(T=0.25, I=0.22, M=0.28, R=0.33, P=0.38)
POINT = dict(T=0.8, I=0.0, M=1.0, R=1.0, P=1.0)
def fingers(side, shape, w=1.0):
    return {side+"fw": w, **{side+k: v for k, v in shape.items()}}

STAND = P(NEUTRAL, rw=1.0, rt=(-0.20,0.03,0.90), rp=(-0.4,1,0), lw=1.0, lt=(0.20,0.03,0.90), lp=(0.4,1,0),
          **fingers("r", RELAX), **fingers("l", RELAX))
SEAT = dict(NEUTRAL)   # mocap base supplies the seated body and clasped hands

CURL_DEG = {"1": 75, "2": 95, "3": 65}          # finger joint angles at curl = 1
THUMB_DEG = {"1": 20, "2": 35, "3": 45}

# ---------------------------------------------------------------- solver
class Rig:
    def __init__(s, arm):
        s.arm = arm; bones = arm.data.bones
        s.order = [b.name for b in bones]
        s.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        s.Rr = {b.name: b.matrix_local.to_3x3() for b in bones}
        s.Hr = {b.name: b.head_local.copy() for b in bones}
        s.len = {b.name: b.length for b in bones}
        s.L1 = (s.Hr[PFX+"RightForeArm"]-s.Hr[PFX+"RightArm"]).length
        s.L2 = (s.Hr[PFX+"RightHand"]-s.Hr[PFX+"RightForeArm"]).length
        # curl axes in rig space (rest): rotate each finger bone toward the palm
        s.curl = {}
        for side in ("Right", "Left"):
            palm = V((0,-1,0))                               # T-pose palms face down
            for fing in ("Index","Middle","Ring","Pinky","Thumb"):
                for j in "123":
                    n = f"{PFX}{side}Hand{fing}{j}"
                    d = s.Rr[n].col[1]
                    if fing == "Thumb":
                        tgt = (s.Hr[f"{PFX}{side}HandPinky1"] - s.Hr[f"{PFX}{side}HandThumb1"]).normalized()
                        tgt = (tgt + palm).normalized()
                    else: tgt = palm
                    ax = d.cross(tgt)
                    s.curl[n] = (ax.normalized(), (THUMB_DEG if fing == "Thumb" else CURL_DEG)[j], fing[0])

    def solve(s, pose, base_q, base_loc, prev):
        pb = s.arm.pose.bones; W, Pos = {}, {}
        hips_off = wpos(pose["hips"])
        spineD = wrot(R('Z', pose["twist"]/3) @ R('X', pose["lean"]/3) @ R('Y', pose["side"]/3))
        add = {PFX+"Spine": spineD, PFX+"Spine1": spineD, PFX+"Spine2": spineD,
               PFX+"Neck": wrot(R('Z', pose["nyaw"]) @ R('X', pose["npitch"]) @ R('Y', pose["nroll"])),
               PFX+"Head": wrot(R('Z', pose["hyaw"]) @ R('X', pose["hpitch"]) @ R('Y', pose["hroll"])),
               PFX+"RightShoulder": wrot(R('Y', pose["rclav"])), PFX+"LeftShoulder": wrot(R('Y', -pose["lclav"]))}
        ik_aim = {}
        for n in s.order:
            p = s.parent[n]; Rr = s.Rr[n]
            if p is None:
                base = Rr.copy(); pos = s.Hr[n] + base_loc + hips_off
            else:
                base = W[p] @ s.Rr[p].transposed() @ Rr
                pos = Pos[p] + W[p] @ s.Rr[p].transposed() @ (s.Hr[n]-s.Hr[p])
            Lb = base_q[n].to_matrix()
            Wd = base @ Lb
            short = n[len(PFX):]
            side = "Right" if short.startswith("Right") else ("Left" if short.startswith("Left") else None)
            k = side[0].lower() if side else None
            if n in add:
                Wd = add[n] @ Wd
            elif side and short in (side+"Arm", side+"ForeArm"):
                w = pose[k+"w"]
                if w > 1e-4:
                    if short == side+"Arm":
                        t = wpos(pose[k+"t"]); e = two_bone(pos, t, C @ pose[k+"p"], s.L1, s.L2)
                        ik_aim[side+"Arm"] = e - pos; ik_aim[side+"ForeArm"] = t - e
                    cur = Wd.col[1]; tgt = ik_aim[short].normalized()
                    d = (cur*(1-w) + tgt*w).normalized()
                    Wd = cur.rotation_difference(d).to_matrix() @ Wd
            elif side and short == side+"Hand":
                w = pose[k+"hw"]
                if w > 1e-4:
                    cur = Wd.col[1]; d = (cur*(1-w) + (C @ pose[k+"h"]).normalized()*w).normalized()
                    Wd = cur.rotation_difference(d).to_matrix() @ Wd
                for key, vrest in (("pal", V((0,-1,0))),
                                   ("thu", (s.Hr[f"{PFX}{side}HandThumb3"]-s.Hr[f"{PFX}{side}Hand"]).normalized())):
                    w2 = pose[k+key+"w"]
                    if w2 > 1e-4:
                        ax = Wd.col[1]; v = Wd @ Rr.transposed() @ vrest; t = (C @ pose[k+key]).normalized()
                        v = v - ax*v.dot(ax); t = t - ax*t.dot(ax)
                        if v.length > 1e-6 and t.length > 1e-6:
                            ang = math.atan2(ax.dot(v.cross(t)), v.dot(t))
                            Wd = Matrix.Rotation(ang*w2, 3, ax) @ Wd
                tw = pose[k+"htw"]
                if abs(tw) > 1e-3:
                    Wd = Matrix.Rotation(math.radians(tw), 3, Wd.col[1]) @ Wd
            elif n in s.curl:
                w = pose[k+"fw"]
                if w > 1e-4:
                    ax, deg, letter = s.curl[n]
                    Lc = (Rr.transposed() @ Matrix.Rotation(math.radians(deg*pose[k+letter]), 3, ax) @ Rr)
                    Lmix = Lb.to_quaternion().slerp(Lc.to_quaternion(), w).to_matrix()
                    Wd = base @ Lmix
            W[n], Pos[n] = Wd, pos
            q = (base.transposed() @ Wd).to_quaternion()
            if n in prev and q.dot(prev[n]) < 0: q.negate()
            prev[n] = q; pb[n].rotation_quaternion = q
        pb[PFX+"Hips"].location = s.Rr[PFX+"Hips"].transposed() @ (base_loc + hips_off) if False else \
            pb[PFX+"Hips"].bone.matrix_local.to_3x3().transposed() @ (base_loc + hips_off)
        tails = {n: Pos[n] + W[n].col[1]*s.len[n] for n in Pos}
        return Pos, tails

def two_bone(sh, t, pole, L1, L2):
    v = t - sh; d = max(1e-3, min(v.length, L1+L2-1e-3)); n = v.normalized()
    a = (L1*L1 + d*d - L2*L2)/(2*d); h = math.sqrt(max(L1*L1-a*a, 0.0))
    pd = pole - n*pole.dot(n); pd = pd.normalized() if pd.length > 1e-6 else V((0,1,0))
    return sh + n*a + pd*h

# ---------------------------------------------------------------- build one motion
def make(name, nframes, keys, seated, overlay=None, mocap=None, m0=0):
    arm, _ = load_rig()
    rig = Rig(arm); scn = bpy.context.scene
    scn.frame_start, scn.frame_end = 0, nframes-1
    ident = {n: Quaternion() for n in rig.order}
    prev, frames = {}, []
    for f in range(nframes):
        pose = sample(keys, f)
        if overlay: overlay(f, pose)
        if seated:
            bq, bl = mocap[m0 + f]
            q0, l0 = mocap[m0]
            e = min(1.0, max(0.0, (f - (nframes-1-18)) / 18.0)); e = e*e*(3-2*e)   # blend back to start pose
            base_q = {n: bq[n].slerp(q0[n], e) for n in rig.order}
            base_loc = (arm.pose.bones[PFX+"Hips"].bone.matrix_local.to_3x3() @ bl.lerp(l0, e))
        else:
            breathe = math.sin(2*math.pi*f/(nframes-1)*max(1, round(nframes/75)))
            pose["lean"] += 0.8*breathe
            base_q, base_loc = ident, V((0,0,0))
        Pos, tails = rig.solve(pose, base_q, base_loc, prev)
        frames.append(({k: tuple(C.transposed() @ v / 100) for k, v in Pos.items()},
                       {k: tuple(C.transposed() @ v / 100) for k, v in tails.items()}))
        for pbn in arm.pose.bones:
            pbn.keyframe_insert("rotation_quaternion", frame=f)
            pbn.keyframe_insert("location", frame=f)
    # rest-pose key at frame 0 (outside the exported 0..N-1 range) so the FBX bone nodes are written in T-pose rest
    for pbn in arm.pose.bones:
        pbn.rotation_quaternion = Quaternion(); pbn.location = V((0,0,0))
        pbn.keyframe_insert("rotation_quaternion", frame=-1); pbn.keyframe_insert("location", frame=-1)
    scn.frame_set(-1)
    act = arm.animation_data.action; act.name = name
    for fc in act.fcurves:
        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name+".fbx"), object_types={'ARMATURE'},
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        axis_forward='-Z', axis_up='Y', apply_unit_scale=True, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    return frames

# ================================================================= MOTIONS (world metres, faces -Y)
MOT = {}
# Seated rig (from mocap): shoulders ~ (+-0.13,-0.05,1.00), hands clasped in lap ~ (0.01,-0.4,0.68)

# 1. Seated nod "yes" - 3.0 s
MOT["seated_nod_yes"] = (90, True, [
    (0, SEAT), (10, SEAT),
    (19, P(SEAT, lean=4, npitch=6, hpitch=15)), (27, P(SEAT, lean=4, npitch=1, hpitch=-3)),
    (35, P(SEAT, lean=4, npitch=5, hpitch=12)), (43, P(SEAT, lean=4, npitch=1, hpitch=-2)),
    (50, P(SEAT, lean=3, npitch=3, hpitch=7)), (58, P(SEAT, lean=3, npitch=1, hpitch=1)),
    (72, P(SEAT, lean=2)), (89, SEAT)], None)

# 2. Seated fist pump - 3.5 s
fist = fingers("r", FIST)
lo = dict(rw=1.0, rt=(-0.17,-0.30,0.97), rp=(-0.2,0.3,-1), rhw=0.0, **fist)
hi = dict(rw=1.0, rt=(-0.19,-0.27,1.13), rp=(-0.2,0.3,-1), rhw=0.0, **fist)
MOT["seated_fist_pump"] = (105, True, [
    (0, SEAT), (8, SEAT),
    (17, P(SEAT, lean=7, rw=1.0, rt=(-0.10,-0.33,0.88), rp=(-0.6,0.4,-0.7), hpitch=4, **fist)),
    (25, P(SEAT, lean=-6, rclav=16, rw=1.0, rt=(-0.22,-0.14,1.52), rp=(-1,0.2,0), hpitch=-14, npitch=-4, side=-3, **fist)),
    (35, P(SEAT, lean=-5, rclav=16, rw=1.0, rt=(-0.22,-0.14,1.51), rp=(-1,0.2,0), hpitch=-12, npitch=-4, side=-3, **fist)),
    (44, P(SEAT, lean=9, hpitch=6, **lo)), (50, P(SEAT, lean=6, hpitch=2, **hi)),
    (56, P(SEAT, lean=9, hpitch=6, **lo)), (62, P(SEAT, lean=6, hpitch=2, **hi)),
    (68, P(SEAT, lean=9, hpitch=6, **lo)), (80, P(SEAT, lean=6, hpitch=3, **lo)),
    (104, SEAT)],
    lambda f, p: p.update(rt=p["rt"] + V((0.012*math.sin(f*1.9), 0, 0.012*math.sin(f*2.3)))*pulse(f,30,7)))

# 3. Assistant hands a card (thumb-index pinch) - standing, 4.0 s
pinch = dict(rT=0.55, rI=0.35, rM=0.6, rR=0.7, rP=0.75, rfw=1.0)
reach = dict(lean=10, twist=-4, npitch=5, hpitch=9, rclav=4, rw=1.0,
             rt=(-0.10,-0.47,1.12), rp=(-1,0.3,-0.7), rh=(0,-1,0.05), rhw=0.6, rhtw=0.0, **pinch)
MOT["standing_hand_card"] = (120, False, [
    (0, STAND), (10, STAND), (38, P(STAND, **reach)),
    (56, P(STAND, **dict(reach, rt=(-0.10,-0.46,1.13)))),
    (64, P(STAND, **dict(reach, lean=9, rt=(-0.11,-0.41,1.13), rT=0.3, rI=0.25))),     # card released
    (94, STAND), (119, STAND)], None)

# 4. Auctioneer points at a table - standing, 4.0 s
pdir = V((-0.5,-0.82,-0.28)).normalized(); sh = V((-0.15,0.04,1.44))
pt_full = dict(twist=-12, nyaw=-5, hyaw=-10, hpitch=6, rclav=6, rw=1.0,
               rt=tuple(sh + pdir*0.56), rp=(-0.3,0.4,-1), rh=tuple(pdir), rhw=1.0, **fingers("r", POINT),
               lw=1.0, lt=(0.16,-0.20,1.02), lp=(1,0.3,-0.6), lh=(-0.3,-1,0.3), lhw=0.5, **fingers("l", FIST))
pt_back = dict(pt_full, rt=tuple(sh + pdir*0.43), hpitch=2)
pt_hit = dict(pt_full, hpitch=10)
MOT["standing_auctioneer_point"] = (120, False, [
    (0, STAND), (10, STAND),
    (22, P(STAND, **pt_hit)), (30, P(STAND, **pt_full)),
    (37, P(STAND, **pt_back)), (43, P(STAND, **pt_hit)),
    (51, P(STAND, **pt_back)), (57, P(STAND, **pt_hit)),
    (65, P(STAND, **pt_back)), (71, P(STAND, **pt_hit)),
    (84, P(STAND, **pt_full)), (106, STAND), (119, STAND)], None)

# 5. Photographer, camera to eye - standing, 5.0 s
cam = dict(lean=3, npitch=3, hpitch=4, rclav=6, lclav=2,
           rw=1.0, rt=(-0.10,-0.17,1.66), rp=(-1,0.3,-0.3), rh=(0.2,-0.5,0.8), rhw=0.7,
           lw=1.0, lt=(0.06,-0.23,1.60), lp=(0.3,0.3,-1), lh=(-0.4,-0.6,0.3), lhw=0.6,
           rfw=1.0, rT=0.45, rI=0.15, rM=0.75, rR=0.8, rP=0.85,          # right grips body, index on shutter
           lfw=1.0, lT=0.35, lI=0.45, lM=0.5, lR=0.55, lP=0.6)           # left cradles lens
def shutter(f, p):
    for c in (60, 80, 110):
        k = pulse(f, c, 4)
        p["rI"] += 0.45*k; p["lean"] += 0.6*k; p["hpitch"] += 0.5*k
MOT["standing_photographer_camera"] = (150, False, [
    (0, STAND), (12, STAND), (40, P(STAND, **cam)), (90, P(STAND, **cam)),
    (102, P(STAND, **dict(cam, twist=-10, hyaw=-2))), (112, P(STAND, **dict(cam, twist=-10, hyaw=-2))),
    (140, STAND), (149, STAND)], shutter)

# ================================================================= STAGE POSES (standing) + SEATED TALK LOOPS
OPEN = dict(T=0.05, I=0.03, M=0.03, R=0.06, P=0.08)
THUMB_UP = dict(T=-0.6, I=1.0, M=1.0, R=1.0, P=1.0)

# 6. Owner on stage: raises open hand to the cameras ("hand flash"), little wave, holds for photos - 4.0 s
flash = dict(lean=-2, twist=3, hroll=-4, hpitch=-3, rclav=12, rw=1.0,
             rt=(-0.33,-0.17,1.63), rp=(-1,0.2,-0.6), rh=(-0.08,-0.15,1), rhw=1.0, rpal=(0,-1,0), rpalw=1.0,
             **fingers("r", OPEN),
             lw=1.0, lt=(0.21,0.02,1.04), lp=(1,0.4,0), lh=(-0.9,0.1,-0.6), lhw=0.7, **fingers("l", RELAX))   # left hand on hip
def wave(f, p):
    k = pulse(f, 40, 14)
    p["rt"] = p["rt"] + V((0.06*math.sin((f-26)*2*math.pi/14), 0, 0))*k
    p["rh"] = p["rh"] + V((0.35*math.sin((f-26)*2*math.pi/14), 0, 0))*k
MOT["standing_pose_hand_flash"] = (120, False, [
    (0, STAND), (8, STAND), (24, P(STAND, **flash)), (60, P(STAND, **flash)),
    (92, P(STAND, **dict(flash, hroll=-6, twist=4))), (112, STAND), (119, STAND)], wave)

# 7. Owner on stage: double thumbs up with two little pumps, holds for photos - 4.0 s
tu = dict(lean=-3, hroll=5, hpitch=-2, rclav=6, lclav=6,
          rw=1.0, rt=(-0.22,-0.30,1.17), rp=(-1,0.3,-0.6), rh=(0.1,-1,0.9), rhw=0.7, rthu=(0,0,1), rthuw=1.0,
          lw=1.0, lt=(0.22,-0.30,1.17), lp=(1,0.3,-0.6), lh=(-0.1,-1,0.9), lhw=0.7, lthu=(0,0,1), lthuw=1.0,
          **fingers("r", THUMB_UP), **fingers("l", THUMB_UP))
tu_up = dict(tu, rt=(-0.22,-0.34,1.22), lt=(0.22,-0.34,1.22), lean=-4, hpitch=-5)
MOT["standing_pose_double_thumbs_up"] = (120, False, [
    (0, STAND), (8, STAND), (24, P(STAND, **tu)), (31, P(STAND, **tu_up)), (37, P(STAND, **tu)),
    (43, P(STAND, **tu_up)), (50, P(STAND, **tu)), (92, P(STAND, **dict(tu, hroll=3))),
    (112, STAND), (119, STAND)], None)

# --- seated talk loops (base = your Sitting_Talking mocap; first frame == last frame)
# 8. Talking to the member on the left: turned left, right hand makes explaining beats - 6.0 s loop
talkL = dict(twist=10, nyaw=8, hyaw=16, rw=1.0, rt=(-0.10,-0.40,0.86), rp=(-0.8,0.5,-0.6),
             rh=(0.2,-1,0.15), rhw=0.5, rpal=(0.3,0,1), rpalw=0.8, **fingers("r", OPEN))
def beat(rt, z=0.0, x=0.0, y=0.0): return tuple(V(rt) + V((x,y,z)))
MOT["seated_talk_to_left_loop"] = (180, True, [
    (0, P(SEAT, **talkL)),
    (14, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=0.06, x=0.03), hpitch=-3))),
    (24, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=-0.02), hpitch=5))),
    (40, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=0.07, x=0.06, y=-0.04), rpal=(0.6,-0.2,0.8), hpitch=-2, hyaw=20))),
    (52, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=0.0, x=0.04), hpitch=4, hyaw=18))),
    (70, P(SEAT, **dict(talkL, hpitch=0))),
    (88, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=0.05, x=-0.03), hpitch=-4, lean=2))),
    (98, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=-0.01), hpitch=4, lean=2))),
    (108, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=0.05, x=0.02), hpitch=-3, lean=2))),
    (118, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=-0.01), hpitch=4, lean=2))),
    (140, P(SEAT, **dict(talkL, rt=beat(talkL["rt"], z=0.03, x=0.05), rpal=(0.5,0,0.9), hyaw=19))),
    (160, P(SEAT, **dict(talkL, hpitch=1))), (179, P(SEAT, **talkL))], None, 300)

# 9. Listening to the member on the right: turned right, hands stay clasped, slow nods - 5.0 s loop
lis = dict(twist=-8, nyaw=-7, hyaw=-15, hroll=-5, lean=2)
MOT["seated_listen_nod_right_loop"] = (150, True, [
    (0, P(SEAT, **lis)), (22, P(SEAT, **lis)),
    (32, P(SEAT, **dict(lis, hpitch=10, npitch=3))), (42, P(SEAT, **dict(lis, hpitch=-1))),
    (51, P(SEAT, **dict(lis, hpitch=8, npitch=2))), (62, P(SEAT, **dict(lis, hpitch=0))),
    (90, P(SEAT, **dict(lis, hroll=-8, hyaw=-17, lean=3))),
    (106, P(SEAT, **dict(lis, hpitch=9, npitch=3, hroll=-7))), (118, P(SEAT, **dict(lis, hpitch=0, hroll=-6))),
    (149, P(SEAT, **lis))], None, 1195)

# 10. Leaning in to discuss over the table: looks left, at table, right; both hands open, then taps/points at table - 6.0 s loop
lean_in = dict(lean=12, npitch=4,
               rw=1.0, rt=(-0.12,-0.45,0.86), rp=(-0.8,0.4,-0.6), rh=(0.1,-1,0), rhw=0.6, rpal=(0.3,0,1), rpalw=0.7, **fingers("r", OPEN),
               lw=1.0, lt=(0.12,-0.45,0.86), lp=(0.8,0.4,-0.6), lh=(-0.1,-1,0), lhw=0.6, lpal=(-0.3,0,1), lpalw=0.7, **fingers("l", OPEN))
open_up = dict(lean_in, rt=(-0.20,-0.44,0.92), lt=(0.20,-0.44,0.92), rpal=(0.2,-0.4,1), lpal=(-0.2,-0.4,1), hpitch=-4)
pointT = dict(lean_in, lean=15, hpitch=12, rt=(-0.06,-0.52,0.84), rh=(0.1,-0.8,-0.6), rhw=1.0, rpalw=0.0,
              **fingers("r", POINT), lt=(0.10,-0.42,0.82))
tap = dict(pointT, rt=(-0.06,-0.53,0.80))
MOT["seated_lean_in_discuss_loop"] = (180, True, [
    (0, P(SEAT, **lean_in)),
    (20, P(SEAT, **dict(lean_in, twist=6, hyaw=16))),                       # look at left member
    (36, P(SEAT, **dict(open_up, twist=6, hyaw=16))),                       # open palms "what do you think?"
    (52, P(SEAT, **dict(lean_in, twist=3, hyaw=6))),
    (68, P(SEAT, **pointT)), (74, P(SEAT, **tap)), (80, P(SEAT, **pointT)), (86, P(SEAT, **tap)),   # point / tap at table
    (100, P(SEAT, **pointT)),
    (118, P(SEAT, **dict(lean_in, twist=-6, hyaw=-16))),                    # look at right member
    (132, P(SEAT, **dict(open_up, twist=-6, hyaw=-16))),
    (150, P(SEAT, **dict(lean_in, twist=-3, hyaw=-6, hpitch=4))),
    (179, P(SEAT, **lean_in))], None, 700)

# 11. Working on a laptop at a table: types, uses the trackpad, pauses to read, types again - 6.0 s loop
#     Assumed layout (world, metres): table top ~0.75 m, keyboard ~0.42 m in front of the hips, trackpad just in front of it.
import random
TYPE_SHAPE = dict(T=0.28, I=0.24, M=0.26, R=0.28, P=0.3)
kb = dict(lean=10, npitch=6, hpitch=10,
          rw=1.0, rt=(-0.10,-0.40,0.835), rp=(-0.7,0.3,-1), rh=(0.15,-1,0.06), rhw=0.8, rpal=(0,0,-1), rpalw=1.0, **fingers("r", TYPE_SHAPE),
          lw=1.0, lt=(0.10,-0.40,0.835), lp=(0.7,0.3,-1), lh=(-0.15,-1,0.06), lhw=0.8, lpal=(0,0,-1), lpalw=1.0, **fingers("l", TYPE_SHAPE))
pad = dict(kb, rt=(-0.03,-0.30,0.83), rh=(0.2,-1,-0.02), rI=0.08, rM=0.28, rR=0.32, rP=0.36, rT=0.3, hpitch=12,
           lt=(0.09,-0.40,0.835), lI=0.3, lM=0.3, lR=0.35, lP=0.4)
read = dict(kb, lean=7, hpitch=4, hroll=-3, rt=(-0.11,-0.38,0.84), lt=(0.11,-0.38,0.84),
            rI=0.3, rM=0.3, rR=0.35, rP=0.4, lI=0.3, lM=0.3, lR=0.35, lP=0.4)
rng = random.Random(7)
TAPS = []                                     # (frame, hand, finger) keystrokes in the typing stretches
for a0, a1 in ((4, 58), (120, 150)):
    f = a0
    while f < a1:
        TAPS.append((f, rng.choice("rl"), rng.choice("IMRIMT"))); f += rng.choice((2, 3, 3, 4, 5))
CLICKS = (84, 98)
def typing(f, p):
    for t, h, fin in TAPS:
        k = pulse(f, t, 2.5)
        if k:
            p[h+fin] += 0.17*k
            p[h+"t"] = p[h+"t"] + V((0, 0, -0.006))*k
    for c in CLICKS:
        p["rI"] += 0.3*pulse(f, c, 3)
    trk = pulse(f, 90, 16)                    # small trackpad strokes
    p["rt"] = p["rt"] + V((0.025*math.sin((f-74)*0.35), 0.015*math.cos((f-74)*0.27), 0))*trk
MOT["seated_laptop_typing_loop"] = (180, True, [
    (0, P(SEAT, **kb)), (58, P(SEAT, **kb)),
    (72, P(SEAT, **pad)), (106, P(SEAT, **pad)),
    (118, P(SEAT, **kb)), (150, P(SEAT, **kb)),
    (158, P(SEAT, **read)), (168, P(SEAT, **dict(read, hpitch=7))),
    (179, P(SEAT, **kb))], typing)

# ================================================================= run
def calm_start(mocap, length):
    """Pick the mocap window with the least arm/head movement for the seated base layer."""
    def score(i):
        return sum(sum((mocap[i+j][0][n].rotation_difference(mocap[i+j+1][0][n]).angle)
                       for n in (PFX+"Head", PFX+"Neck", PFX+"RightArm", PFX+"LeftArm", PFX+"Spine2"))
                   for j in range(0, length-1, 3))
    return min(range(0, len(mocap)-length-1, 10), key=score)

if __name__ == "__main__":
    _, mocap = load_rig(read_mocap=True)
    only = sys.argv[1:] or list(MOT)
    data = {}
    for name in only:
        n, seated, keys, ov, *rest = MOT[name]
        m0 = (rest[0] if rest else calm_start(mocap, n)) if seated else 0
        data[name] = make(name, n, keys, seated, ov, mocap, m0)
        print("exported", name, n, "frames", f"(mocap base from source frame {m0+1})" if seated else "")
    pickle.dump(data, open(_os.path.join(APL_WORK, "mx_frames.pkl"), "wb"))
