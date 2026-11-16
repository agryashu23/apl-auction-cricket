"""
Non-striker clips for the batting pack (right-handed, plus the lhb_ mirrors via running_bat.mirror_y):

  75_non_striker_idle_loop    150 f, loop   standing tall and a little open, bat hanging from the bottom hand
                                            beside the right leg (toe hovering), one weight transfer toward the
                                            left foot and back, one 18 deg shoulder turn to the off side with a
                                            glance away, two bat taps (hand dips riding a small knee dip),
                                            free hand by the thigh / on the hip
  76_backing_up                90 f         from the idle's first pose: lifts the bat into a low trailing carry,
                                            walks ~1.5 m down the pitch (+X) and settles into a soft-kneed,
                                            weight-forward "about to run" crouch
  77_backing_up_ready_loop    120 f, loop   76's final crouch re-based over the origin, held alive: hip sway,
                                            a 5 deg head turn and back, the back heel lifting and replanting

Batting frame: origin between the feet on the popping crease, Z up, chest faces -Y in the side-on stance,
+X is down the pitch (the game turns the non-striker so +X points at the striker).

    tools\\motions\\motion.ps1 gen drivers\\non_striker.py [rhb|lhb|all] [75|76|77] [--check]

--check only prints the bottom-hand reach deficit of every key pose (how far the shoulder would have to
move before make_bat's reach assist kicks in); nothing is exported.
IDLE_KEYS / BACK_KEYS / READY_KEYS are the key lists (the game exports its bat track from them).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib"))
import batting  # noqa: E402
import mixamo_motions as mm  # noqa: E402
from batting import BAT_LEN, GRIP_R, BatRig, P, V, bat_g, load_rig, make_bat  # noqa: E402
from fielding import fwd, left  # noqa: E402
from running_bat import Hw, lean_dir, mirror_y  # noqa: E402
from shots import STANCE  # noqa: E402

# ---------------------------------------------------------------- idle: the open, upright resting stance
H0 = 40.0                                   # chest heading: 0 = side-on stance, 90 = square to the striker
F0, L0 = fwd(H0), left(H0)
R0 = -L0
HZ0 = 1.033                                 # hip height: standing tall, knees ~15 deg (1.043 = locked straight)
LB0 = L0 * 0.21 + F0 * 0.03                 # balls of the feet, about shoulder width
RB0 = -L0 * 0.21 + F0 * 0.03
LTOE0, RTOE0 = fwd(H0 + 10), fwd(H0 - 10)   # toes splayed a little
HIPC = V((-0.03, 0.04, 0))                  # hip centre over the feet (ground xy)
WEIGHT_M = 0.07                             # a full weight transfer moves the hips this far toward a foot
EYE_Z, GAZE_PITCH = 1.67, -3.0              # eye height in the idle; head pitch held 3 deg down (looking around is yaw only)


def gaze(x, y, pitch=GAZE_PITCH):
    """a look target at ground xy (x, y) that puts the face at `pitch` degrees (- = down) from the idle's eyes"""
    return V((x, y, EYE_Z + math.hypot(x - HIPC.x, y - HIPC.y) * math.tan(math.radians(pitch))))


LOOK_STRIKER = gaze(17.0, 0.0)              # down the pitch at the striker, chin a touch down
LOOK_AWAY = gaze(2.0, -12.0)                # a glance off to the off side, same pitch (yaw only)
# the bat hangs from the hand: with GRIP_R fixed and a hanging arm the toe sits ~0.85 m below the hips,
# so the hover height follows the hip height (1.04 -> ~0.165). The taps dip it by ~0.10.
TOE_HOVER = 0.175
TOE_TAP = 0.075
LHB_BAT_LIFT = 0.055                         # the rig's left arm reaches ~3-4 cm less: the left-hander's hanging bat sits that much higher
REST_CURVE = 6.5                            # the Mixamo rest spine leans back ~5 deg; cancelled along the chest heading
REST_AXIS = (R0 * 0.08 + F0 * 0.08 + V((0, 0, -1))).normalized()   # near vertical, handle ~6 deg in toward him
REST_FACE = L0 * 0.8 - F0 * 0.6             # blade face toward his leg: palm in and back, fingers wrap forward


def rest_bat(hw, toe_z=TOE_HOVER):
    """bat hanging in the bottom hand beside the right leg; it moves with the hips (it is not planted)"""
    toe = V((hw.x, hw.y, 0)) + R0 * 0.25 + F0 * 0.10 + V((0.06, 0, toe_z))     # +X: under the shoulder, which leans that way
    return bat_g(toe - REST_AXIS * (BAT_LEN - GRIP_R), REST_AXIS, REST_FACE)


def hips_xy(w):
    """w = weight: -1 toward the right foot .. +1 toward the left foot (WEIGHT_M per unit)"""
    return HIPC + L0 * (WEIGHT_M * w)


def idle_pose(w=0.0, *, toe_z=TOE_HOVER, look=None, twist=12.0, hand="thigh", dz=0.0, fwd_lean=9.0, bow=0.0, clav=-14.0):
    """
    w        weight transfer (hips, pelvis roll and a little pelvis yaw follow it)
    twist    shoulders relative to the pelvis: +12 opens them toward the striker, -6 turns them to the off side
    hand     free (top) hand: "thigh" hanging, "hip" resting on the left hip with the elbow out
    fwd_lean trunk tilt toward the striker (+X); bow = extra trunk bow toward the bat (taps only)
    clav     right clavicle (bat shoulder) drop in degrees
    """
    hw = hips_xy(w)
    a, b, c = lean_dir(90.0, fwd_lean), lean_dir(H0 - 90.0, bow), lean_dir(H0, REST_CURVE)
    if hand == "hip":                                           # free hand resting lightly on the left hip, elbow out
        lt = hw + L0 * 0.18 + F0 * 0.04 + V((0, 0, HZ0 + dz + 0.03))
        lpole = L0 * 1.0 - F0 * 0.4 + V((0, 0, -0.3))
    else:                                                       # free hand hanging by the left thigh, elbow soft
        lt = hw + L0 * 0.14 + F0 * 0.08 + V((0, 0, 0.92 + dz))
        lpole = L0 * 0.6 - F0 * 0.5 + V((0, 0, -0.8))
    return P(STANCE, hips=Hw(hw.x, hw.y, HZ0 + dz), pyaw=H0 - 4.0 * w, ptilt=0.0, pside=-3.0 * w, twist=twist,
             lean=a["lean"] + b["lean"] + c["lean"], side=a["side"] + b["side"] + c["side"], rclav=clav,
             rball=RB0, lball=LB0, rtoe=RTOE0, ltoe=LTOE0, rheel=0.0, lheel=0.0,
             rkp=RTOE0 + V((0, 0, 0.2)), lkp=LTOE0 + V((0, 0, 0.2)),
             **rest_bat(hw, toe_z),
             topgrip=0.0, lt=lt, lpole=lpole, **mm.fingers("l", mm.RELAX),
             look=look or LOOK_STRIKER, lookw=1.0)


def tap(w, **kw):
    """the tap: the hand dips ~10 cm (small knee dip + the bat shoulder dropping), the toe touches down and comes back"""
    return idle_pose(w, toe_z=TOE_TAP, dz=-0.05, bow=5.0, clav=-22.0, **kw)


# the first frame of 75 and 76
A = idle_pose(0.1)

IDLE_N = 150
IDLE_KEYS = [
    (0, A),
    (30, idle_pose(1.0)),                                       # weight over toward the left foot
    (42, tap(0.95)),                                            # tap 1
    (48, idle_pose(0.9)),
    (60, idle_pose(0.9)),
    (72, idle_pose(0.8, twist=-6, look=LOOK_AWAY, hand="hip")),   # shoulders turn 18 deg to the off side, glance away
    (84, idle_pose(0.7, twist=-6, look=LOOK_AWAY, hand="hip")),
    (96, idle_pose(0.6, twist=-6, hand="hip")),                 # eyes back on the striker ...
    (106, idle_pose(0.4, hand="hip")),                          # ... shoulders back open
    (120, idle_pose(-0.3)),                                     # weight back through centre, hand to the thigh
    (130, tap(-0.2)),                                           # tap 2
    (136, idle_pose(-0.1)),
    (IDLE_N - 1, A),
]

# ---------------------------------------------------------------- backing up: a short walk down the pitch
LOOK_WALK = V((18.0, 0.0, 1.35))


def trail_bat(hw, h, *, z=0.80, back=0.22, ahead=0.0):
    """bat in the bottom hand at the side, arm hanging, toe trailing a hand's width off the ground"""
    F, L = fwd(h), left(h)
    bg = V((hw.x, hw.y, z)) - L * 0.24 + F * ahead
    a = (-F * back + V((0, 0, -1))).normalized()
    return bat_g(bg, a, L * 0.85 - F * 0.5)


def walk_pose(hx, hy, hz, h, rb, lb, *, rz=0.0, lz=0.0, rtoe=None, ltoe=None, rheel=0.0, lheel=0.0, dl=0.0,
              ptilt=6.0, fwd_lean=5.5, pel_amp=5.0, bat=None, lhand=None, look=None, twist=None, gait=1.0):
    """
    dl   stride phase: +1 left foot forward, -1 right foot forward (pelvis turn, arm swing and bat swing follow)
    rz/lz swing-foot height: the walk carries its own foot arcs (gait=1 keeps batting.step_lift out)
    """
    F, L = fwd(h), left(h)
    hw = V((hx, hy, 0))
    pel = pel_amp * dl
    lt = lhand if lhand is not None else hw + L * 0.22 + F * (0.02 - dl * 0.13) + V((0, 0, hz - 0.11))
    rtoe = rtoe if rtoe is not None else F
    ltoe = ltoe if ltoe is not None else F
    ld = lean_dir(h, fwd_lean)
    return P(STANCE, hips=Hw(hx, hy, hz), pyaw=h + pel, twist=(-pel * 1.3 if twist is None else twist),
             lean=ld["lean"], side=ld["side"], ptilt=ptilt, pside=0.0, rclav=0.0,
             rball=V((rb[0], rb[1], rz)), lball=V((lb[0], lb[1], lz)), rtoe=rtoe, ltoe=ltoe, rheel=rheel, lheel=lheel,
             rkp=rtoe + V((0, 0, 0.15)), lkp=ltoe + V((0, 0, 0.15)),
             **(bat or trail_bat(hw, h, z=0.83, back=0.22 + 0.14 * dl, ahead=0.03 + 0.10 * dl)),
             topgrip=0.0, lt=lt, lpole=L * 0.3 - F * 1.0 + V((0, 0, -0.5)), **mm.fingers("l", mm.RELAX),
             look=look or LOOK_WALK, lookw=1.0, gait=gait)


READY_LEAD, READY_REAR = 0.25, -0.12        # staggered stance: lead (left) foot 25 cm ahead of the hips, rear 12 cm behind


def ready_pose(ox, oy, *, dz=0.0, tilt=15.0, rheel=15.0, lheel=0.0, look=None, sway=0.0, rock=0.0):
    """
    The "about to run" crouch with the body origin at (ox, oy): knees soft, staggered feet, weight forward over the
    front (left) foot, rear (right) heel up, bat low behind the hip with the blade angled back ~27 deg, eyes down
    the pitch. rock moves the body (not the feet) toward the striker, onto the balls of the feet.
    76 ends in ready_pose(1.5, 0); 77 loops ready_pose(0, 0) - the same pose apart from the translation.
    """
    hw = V((ox + rock, oy + sway, 0))
    return walk_pose(ox + rock, oy + sway, 0.905 + dz, 90, (ox + READY_REAR, oy - 0.09), (ox + READY_LEAD, oy + 0.09), dl=0.0,
                     ptilt=tilt, fwd_lean=3.0, twist=0.0, rheel=rheel, lheel=lheel,
                     bat=trail_bat(hw, 90, z=0.74, back=0.50),
                     lhand=hw + V((0.20, 0.20, 0.82)), look=look or V((ox + 16.5, oy, 1.35)))


def backing_up_keys():
    hw0 = hips_xy(0.1)
    # foot plants (ground xy): right steps first, left closes up in front at the end
    R1, L1 = (0.55, -0.09), (1.02, 0.09)
    R2, L2 = (1.50 + READY_REAR, -0.09), (1.50 + READY_LEAD, 0.09)      # = the ready crouch's feet
    rb0, lb0 = (RB0.x, RB0.y), (LB0.x, LB0.y)
    SW, HEEL_LIFT, HEEL_MID = 0.08, 22.0, 12.0          # swing-foot clearance, heel-up before lift-off, mid-swing heel
    # weight onto the left foot, bat lifted into the carry, body starts turning down the pitch
    lift = walk_pose(0.07, 0.07, 0.99, 60, rb0, lb0, rtoe=RTOE0, ltoe=LTOE0, rheel=20, dl=0.4, ptilt=4,
                     twist=6, lhand=hw0 + L0 * 0.14 + F0 * 0.10 + V((0, 0, 0.92)))
    k = [(0, A), (8, lift)]
    # step 1: right foot
    k.append((15, walk_pose(0.14, 0.05, 1.0, 75, (0.18, -0.12), lb0, rz=SW, rheel=HEEL_MID, ltoe=fwd(62), dl=-0.5, ptilt=5)))
    k.append((22, walk_pose(0.28, 0.02, 0.99, 85, R1, lb0, ltoe=fwd(75), lheel=HEEL_LIFT, dl=-1.0)))
    # step 2: left foot
    k.append((30, walk_pose(0.47, -0.025, 1.0, 90, R1, (0.60, 0.10), lz=SW, lheel=HEEL_MID, dl=0.0)))
    k.append((38, walk_pose(0.66, 0.0, 0.99, 90, R1, L1, rheel=HEEL_LIFT, dl=1.0)))
    # step 3: right foot
    k.append((46, walk_pose(0.90, 0.025, 1.0, 90, (0.96, -0.10), L1, rz=SW, rheel=HEEL_MID, dl=0.0)))
    k.append((54, walk_pose(1.14, 0.0, 0.99, 90, R2, L1, lheel=HEEL_LIFT, dl=-1.0)))
    # closing step: left foot comes through, he starts to sink
    k.append((62, walk_pose(1.32, -0.02, 0.985, 90, R2, (1.40, 0.10), lz=SW, lheel=HEEL_MID, dl=0.0, ptilt=8)))
    k.append((70, walk_pose(1.47, 0.0, 0.95, 88, R2, L2, dl=0.7, pel_amp=3, ptilt=11, rheel=8,
                            lhand=V((1.47 + 0.10, 0.22, 0.86)))))
    # ready crouch, settling
    k.append((80, ready_pose(1.50, 0.0, dz=0.005, tilt=14)))
    k.append((89, ready_pose(1.50, 0.0)))
    return k


BACK_N = 90
BACK_KEYS = backing_up_keys()

# ---------------------------------------------------------------- the ready crouch, held alive, over the origin
READY_N = 120
LOOK_READY_ASIDE = V((16.5, -1.5, 1.35))                     # 5 deg to the off side of the striker
RP = ready_pose(0.0, 0.0)
READY_KEYS = [
    (0, RP),
    (20, ready_pose(0.0, 0.0, sway=0.012, rheel=26, rock=0.010, lheel=4, look=LOOK_READY_ASIDE)),   # head turns 5 deg, rocking forward
    (40, ready_pose(0.0, 0.0, sway=0.015, rheel=30, rock=0.018, lheel=8, dz=0.004, look=LOOK_READY_ASIDE)),  # onto the balls of the feet
    (56, ready_pose(0.0, 0.0, sway=-0.008, rheel=0, rock=0.004, dz=-0.003)),       # rear heel replanted, eyes back
    (80, ready_pose(0.0, 0.0, sway=-0.015, rheel=0, rock=-0.006)),                 # rocked back a touch
    (100, ready_pose(0.0, 0.0, sway=-0.005, rheel=8, rock=-0.002, dz=0.003)),
    (READY_N - 1, RP),
]

CLIPS = {
    75: dict(stem="75_non_striker_idle_loop", n=IDLE_N, keys=IDLE_KEYS, loop=True,
             entry=dict(id=75, shot="Non-striker idle (loop)", frames=IDLE_N, loop=True)),
    76: dict(stem="76_backing_up", n=BACK_N, keys=BACK_KEYS, loop=False,
             entry=dict(id=76, shot="Backing up", frames=BACK_N, loop=False,
                        end_position_m=dict(toward_bowler=1.5, toward_off_side=0.0))),
    77: dict(stem="77_backing_up_ready_loop", n=READY_N, keys=READY_KEYS, loop=True,
             entry=dict(id=77, shot="Backing up ready (loop)", frames=READY_N, loop=True)),
}


def _same_apart_from_translation(p, q, d):
    """every channel of p equals q, positions shifted by d"""
    POS = ("hips", "rball", "lball", "lt", "bg", "look")        # the positions set by ready_pose (rt is unused)
    for key in sorted(set(p) | set(q)):
        a, b = p[key], q[key]
        if key in POS:
            a = V(a) + d
        if hasattr(a, "x"):
            if (V(a) - V(b)).length > 1e-6: return False, f"{key}: {tuple(round(x, 4) for x in a)} vs {tuple(round(x, 4) for x in b)}"   # mathutils is float32
        elif abs(a - b) > 1e-6: return False, f"{key}: {a} vs {b}"
    return True, None


def check(keys, label):
    arm, _ = load_rig()
    rig = BatRig(arm)
    for t, k in keys:
        r = rig.reach_vec(k, {})
        bot = V(r[3:6]) if k.get("lefty", 0.0) > 0.5 else V(r[0:3])
        a = V(k["ba"]).normalized()
        toe = V(k["bg"]) + a * (BAT_LEN - GRIP_R)
        print(f"  {label} key f{t + 1:3d}: bottom-hand reach deficit {bot.length:.3f} m, bat toe z {toe.z:.3f}, grip z {k['bg'].z:.3f}", flush=True)


def _ang(a, e, w):
    v1 = V(a) - V(e); v2 = V(w) - V(e)
    return math.degrees(math.acos(max(-1.0, min(1.0, v1.dot(v2) / (v1.length * v2.length)))))


def posture(fr, lefty=False):
    """trunk tilt toward +X (deg, + = toward the striker), knee flexion, bat-arm / free-arm elbow angles, head height"""
    pos = fr[0]
    hips, neck = V(pos["mixamorig:Hips"]), V(pos["mixamorig:Neck"])
    t = neck - hips
    trunk_x = math.degrees(math.atan2(t.x, t.z))
    trunk_y = math.degrees(math.atan2(t.y, t.z))
    knee = {s: 180 - _ang(pos[f"mixamorig:{s}UpLeg"], pos[f"mixamorig:{s}Leg"], pos[f"mixamorig:{s}Foot"]) for s in ("Right", "Left")}
    elbow = {s: _ang(pos[f"mixamorig:{s}Arm"], pos[f"mixamorig:{s}ForeArm"], pos[f"mixamorig:{s}Hand"]) for s in ("Right", "Left")}
    bat, free = ("Left", "Right") if lefty else ("Right", "Left")
    return (f"trunk {trunk_x:+.1f} deg toward +X / {trunk_y:+.1f} toward +Y, knees {knee['Right']:.0f}/{knee['Left']:.0f} deg flexion, "
            f"bat elbow {elbow[bat]:.0f}, free elbow {elbow[free]:.0f}, hips z {hips.z:.3f}, head z {pos['mixamorig:Head'][2]:.3f}")


def head_pitch(stem, n):
    """per frame: elevation of the head's rest-forward axis (what the solver aims at the look target), degrees"""
    import bpy
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE" and o.animation_data and o.animation_data.action
            and o.animation_data.action.name == stem]
    if not arms:
        return None
    arm, scn, out = arms[-1], bpy.context.scene, []
    for f in range(n):
        scn.frame_set(f)
        pb = arm.pose.bones["mixamorig:Head"]
        fw = mm.C.transposed() @ (pb.matrix.to_3x3() @ pb.bone.matrix_local.to_3x3().transposed() @ (mm.C @ V((0, -1, 0))))
        out.append(math.degrees(math.asin(max(-1.0, min(1.0, fw.normalized().z)))))
    scn.frame_set(-1)
    return out


def generate(hand, cid):
    c = CLIPS[cid]
    stem = c["stem"] if hand == "rhb" else "lhb_" + c["stem"]
    keys = c["keys"] if hand == "rhb" else [(t, mirror_y(k)) for t, k in c["keys"]]
    if hand == "lhb":                                           # hanging / carried bat only (grip above the crouch's 0.74), not the crouch
        keys = [(t, (dict(k, bg=V(k["bg"]) + V((0, 0, LHB_BAT_LIFT))) if k["bg"].z > 0.78 else k)) for t, k in keys]
    frames, worst, md = make_bat(stem, c["n"], keys, None, dict(loop=c["loop"]))
    # ---- self-checks
    bad = {f"{k}@f{worst.get(k + '@', -1) + 1}": round(v, 3) for k, v in worst.items() if not k.endswith("@") and v > 0.012}
    toe_z = [fr[2][1][2] for fr in frames]
    hips = [fr[0]["mixamorig:Hips"] for fr in frames]
    print(f"{stem}: {'grip/reach OK' if not bad else 'solver worst ' + str(bad)}", flush=True)
    print(f"  bat toe z: min {min(toe_z):.3f} (f{toe_z.index(min(toe_z)) + 1}) max {max(toe_z):.3f}; at f1 {toe_z[0]:.3f}, "
          f"f{len(toe_z) // 2 + 1} {toe_z[len(toe_z) // 2]:.3f}, f{len(toe_z)} {toe_z[-1]:.3f}", flush=True)
    print(f"  hips f1 ({hips[0][0]:+.3f},{hips[0][1]:+.3f},{hips[0][2]:.3f}) -> f{len(hips)} "
          f"({hips[-1][0]:+.3f},{hips[-1][1]:+.3f},{hips[-1][2]:.3f})", flush=True)
    lefty = hand == "lhb"
    hp = head_pitch(stem, c["n"])
    if hp:
        i_lo, i_hi = hp.index(min(hp)), hp.index(max(hp))
        print(f"  head pitch (face fwd elevation, + = chin up): min {hp[i_lo]:+.1f} (f{i_lo + 1}) max {hp[i_hi]:+.1f} (f{i_hi + 1}); "
              + ", ".join(f"f{q + 1} {hp[q]:+.1f}" for q in (0, c['n'] // 4, c['n'] // 2, 3 * c['n'] // 4)), flush=True)
    for f in sorted({0, len(frames) // 3, len(frames) // 2, 2 * len(frames) // 3, len(frames) - 1}):
        print(f"  posture f{f + 1:3d}: {posture(frames[f], lefty)}", flush=True)
    if c["loop"]:
        d = max(abs(a_ - b_) for n_ in frames[0][0] for a_, b_ in zip(frames[0][0][n_], frames[-1][0][n_]))
        print(f"  loop: first/last frame max bone difference {d * 1000:.2f} mm", flush=True)
    for side in ("Right", "Left"):
        z = [fr[0][f"mixamorig:{side}ToeBase"][2] for fr in frames]
        print(f"  {side} toe base z: min {min(z):+.3f} max {max(z):.3f}", flush=True)
    entry = dict(c["entry"])
    entry = dict(id=entry.pop("id"), shot=entry.pop("shot"), file=stem + ".fbx",
                 **({"batter": "left-handed"} if hand == "lhb" else {}), **entry)
    with open(os.path.join(batting.OUT, stem + ".json"), "w") as fh:
        json.dump([entry], fh, indent=1)
    print(f"  -> {os.path.join(batting.OUT, stem + '.fbx')}", flush=True)


if __name__ == "__main__":
    args = [a for a in sys.argv[1:]]
    do_check = "--check" in args
    args = [a for a in args if a != "--check"]
    hands = [a for a in args if a in ("rhb", "lhb", "all")] or ["all"]
    hands = ["rhb", "lhb"] if "all" in hands else hands
    ids = [int(a) for a in args if a.isdigit()] or sorted(CLIPS)
    ok, why = _same_apart_from_translation(BACK_KEYS[-1][1], READY_KEYS[0][1], V((-1.5, 0.0, 0.0)))
    print(f"76 last key == 77 first key apart from the (-1.5, 0) translation: {'yes' if ok else 'NO (' + str(why) + ')'}", flush=True)
    if do_check:
        for cid in ids:
            check(CLIPS[cid]["keys"], CLIPS[cid]["stem"])
        sys.exit(0)
    for hand in hands:
        batting.OUT = os.path.join(batting.APL_OUT, "batting" if hand == "rhb" else "batting_lh")
        os.makedirs(batting.OUT, exist_ok=True)
        for cid in ids:
            generate(hand, cid)
