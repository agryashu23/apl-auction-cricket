"""
Round-4 match-end handshakes, cheerleader dances and the square-leg umpire (fielding conventions: start at the origin facing
-Y, x = his/her left). Cheer loops are 4.0 s at 120 bpm (a beat every 15 frames), fists closed round pom-poms, feet planted.

  handshake_line_walk_loop   slow walk along the line, right hand out shaking each passer's hand, one pat on a back (in place)
  handshake_single           steps in, shakes hands, left hand pats the other's arm, steps back (partner: same clip at (0,-1.0) facing +Y)
  cheer_pompom_vs_loop       high V, low V, T, punches and a clap on the beat, bouncing, hips swaying
  cheer_kick_loop            right kick, left kick (arms high V), claps, high V
  cheer_shake_loop           pom-poms shaken overhead, hip sway, a quarter turn and back
  cheer_jump_loop            dip, tuck jump with a high V, land, punches
  umpire_square_leg_watch_loop  square-leg umpire: knees soft, hands on the knees, eyes on the popping crease

    motion.ps1 gen drivers\\round4_misc.py [names...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from round4_catches import export
import mixamo_motions as mm
import fielding
from fielding import V, P, H, HIP0, fing, HOLDF, RELAXF, run_keys, inplace, fwd
from field_clips import STAND

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco

@clip("handshake_line_walk_loop")
def line_walk():
    per = 40; n = 2*per + 1; dist = 0.66*4
    fielding.run_keys.last = None
    kk, _ = run_keys(STAND, f0=0, f1=n - 1, p0=(0, 0), p1=(0, -dist), heading=0, period=per, contact=0.62, lean=3, bob=0.012, arm_amp=0.10, hip_h=1.02)
    kk = inplace(kk, 0, n - 1, (0, 0), (0, -dist))
    def ov(f, p):
        hp = V(p["hips"]) + V((0, 0, HIP0))
        shake = 0.04*math.sin(2*math.pi*f/(per/2.0))                          # a shake each step (each passer)
        p["rt"] = hp + V((-0.24, -0.36, 0.10 + shake)); p["rp"] = V((-1, 0.4, -0.6)); p["rh"] = V((0.1, -1, 0)); p["rhw"] = 0.6
        p["rpal"] = V((1, 0, 0)); p["rpalw"] = 0.7; p.update(fing("r", HOLDF))
        p["twist"] = -10; p["look"] = hp + V((-1.4, -4.0, 0.6))
        w = mm.pulse(f, 52, 9)                                                 # a pat on a passing back
        if w > 0:
            p["lt"] = V(p["lt"]).lerp(hp + V((-0.30, -0.32, 0.36 + 0.03*math.sin(f*1.6))), w); p["lp"] = V((0.6, 0.3, -0.6))
    export("handshake_line_walk_loop", n, kk, dict(type="match end", in_place=True, speed_m_s=round(dist/((n - 1)/30), 2),
           note="walking slowly along the handshake line: right hand out at waist height shaking each passer's hand (one shake a step), eyes on the passers to his right front, a left-hand pat on a back mid-loop"),
           overlay=ov, loop=True)

@clip("handshake_single")
def single():
    s0 = P(STAND, look=V((0, -1.0, 1.65)))
    near = P(STAND, hips=H(0, -0.22, 1.02), lball=V((0.17, -0.28, 0)), rball=V((-0.17, -0.46, 0)), lean=10, look=V((0, -1.0, 1.65)))
    shake = lambda dz, pat=False: P(near, rt=V((-0.02, -0.52, 1.00 + dz)), rp=V((-1, 0.4, -0.6)), rh=V((0.1, -1, 0)), rhw=0.6, rpal=V((1, 0, 0)), rpalw=0.8,
                                     **fing("r", HOLDF), lt=V((0.20, -0.78, 1.30)) if pat else V((0.21, -0.18, 0.92)), lp=V((1, 0.2, -0.3)))
    k = [(0, s0), (8, s0), (20, near), (30, shake(0.0)), (36, shake(0.05)), (42, shake(-0.03, True)), (46, shake(0.0, True)), (50, shake(0.04, True)),
         (56, shake(0.0)), (66, near), (80, s0), (99, s0)]
    export("handshake_single", 100, k, dict(type="match end", grip_frames=[31, 57], pat_frames=[43, 51], hand_meet_point_m=dict(forward=0.52, left=0.02, up=1.0),
           layout="the other player plays the same clip at (0, -1.0) facing +Y: right hands meet at the midpoint",
           note="steps in, shakes hands with three pumps, the left hand pats the other player's arm, steps back"))

# ---------------------------------------------------------------- cheer dances (fists round pom-poms)
FISTS = dict(**fing("r", mm.FIST), **fing("l", mm.FIST))
ARMS = {"hiV": ((-0.44, -0.04, 1.90), (0.44, -0.04, 1.90)), "loV": ((-0.44, -0.06, 0.98), (0.44, -0.06, 0.98)), "T": ((-0.70, 0.0, 1.44), (0.70, 0.0, 1.44)),
        "punchR": ((-0.20, -0.06, 1.98), (0.22, -0.02, 1.02)), "punchL": ((-0.22, -0.02, 1.02), (0.20, -0.06, 1.98)), "clap": ((-0.03, -0.36, 1.30), (0.03, -0.36, 1.30)),
        "hips": ((-0.22, -0.02, 1.02), (0.22, -0.02, 1.02)), "over": ((-0.18, -0.16, 1.92), (0.18, -0.16, 1.92))}      # all inside the 0.56 m reach
def cheer(arms, dz=0.0, sway=0.0, **kw):
    r, l = ARMS[arms]
    poles = dict(rp=V((-1, 0.4, -0.3)), lp=V((1, 0.4, -0.3))) if arms not in ("clap",) else dict(rp=V((-0.8, 0.2, -0.8)), lp=V((0.8, 0.2, -0.8)))
    base = P(STAND, hips=H(sway, 0.0, 1.02 + dz), rball=V((-0.19, -0.04, 0)), lball=V((0.19, -0.04, 0)), rtoe=V((-0.15, -1, 0)), ltoe=V((0.15, -1, 0)),
             rt=V(r) + V((sway, 0, dz)), lt=V(l) + V((sway, 0, dz)), **poles, **FISTS, look=V((0, -20, 1.8)), side=-sway*60, pside=sway*40)
    return P(base, **kw)
def beat_keys(seq):
    """seq: list of (arms, extra) per beat; keys on the beat (down) and the off-beat (up)"""
    k = []
    for i, (arms, kw) in enumerate(seq):
        sway = 0.05*(1 if i % 2 == 0 else -1)
        k.append((i*15, cheer(arms, -0.05, sway, **kw)))
        k.append((i*15 + 8, cheer(arms, 0.0, sway*0.6, **kw)))
    k.append((119, k[0][1]))
    return k
def shake_ov(f, p):                                   # pom-poms shaken overhead (every 4 frames) while the arms are up
    if V(p["rt"]).z > 1.8:
        d = 0.045*math.sin(f*math.pi/2.0)
        p["rt"] = V(p["rt"]) + V((d, 0, 0)); p["lt"] = V(p["lt"]) + V((-d, 0, 0))
def dance(name, seq, note, overlay=None):
    export(name, 120, beat_keys(seq), overlay=overlay, meta=dict(type="cheerleader dance", bpm=120, beat_frames=[1 + 15*i for i in range(8)],
           note=note + "; authored on the shared skeleton, retargets onto any Rocketbox body (e.g. Female_Adult_01/02, Business_Female_01-04)"),
           loop=True)

clip("cheer_dance_1")(lambda: dance("cheer_dance_1", [("hiV", {}), ("loV", {}), ("hiV", {}), ("T", {}), ("punchR", {}), ("punchL", {}), ("clap", {}), ("hiV", {})],
                                          "pom-pom routine on the beat: high V, low V, high V, T, right punch, left punch, clap, high V; bouncing through the knees, hips swaying"))
clip("cheer_dance_4")(lambda: dance("cheer_dance_4", [("over", {}), ("over", dict(pyaw=-30)), ("over", dict(pyaw=-45)), ("over", dict(pyaw=-30)),
                                                           ("over", {}), ("hips", {}), ("over", {}), ("hiV", {})],
                                      "pom-poms shaken overhead, a quarter turn to the right and back, hands to the hips, high V", overlay=shake_ov))

def idle_keys():
    """pom-poms on the hips, a gentle side-to-side sway on the beat with a small bounce, a look round the crowd"""
    k = []
    for i in range(8):
        sway = 0.035*(1 if i % 2 == 0 else -1)
        k.append((i*15, cheer("hips", -0.02, sway, look=V((3.0*math.sin(i*0.8), -20, 1.9)))))
        k.append((i*15 + 8, cheer("hips", 0.0, sway*0.6, look=V((3.0*math.sin(i*0.8), -20, 1.9)))))
    k.append((119, k[0][1]))
    return k
@clip("cheer_idle_loop")
def cheer_idle():
    export("cheer_idle_loop", 120, idle_keys(), meta=dict(type="cheerleader idle", bpm=120, beat_frames=[1 + 15*i for i in range(8)],
           note="between routines: pom-poms on the hips, a light sway and bounce on the beat, glancing round the crowd; for Female_Adult_01/02"), loop=True)

KICK_R = dict(rball=V((-0.12, -0.72, 0.82)), rheel=-10, rkp=V((0, -1, 0.6)), lean=-8)
KICK_L = dict(lball=V((0.12, -0.72, 0.82)), lheel=-10, lkp=V((0, -1, 0.6)), lean=-8)
clip("cheer_dance_2")(lambda: dance("cheer_dance_2", [("hips", {}), ("hiV", KICK_R), ("hips", {}), ("hiV", KICK_L), ("clap", {}), ("clap", {}), ("T", {}), ("hiV", {})],
                                     "hands on hips, a high kick with the right leg (arms high V), hands on hips, a left kick, two claps, T, high V"))
def _jump(z, feet=0.0, **kw): return dict(hips=H(0, 0.0, z), rball=V((-0.17, -0.06, feet)), lball=V((0.17, -0.06, feet)), rheel=40 if feet else 0, lheel=40 if feet else 0,
                                          rkp=V((0, -1, 0.6)), lkp=V((0, -1, 0.6)), **kw)
clip("cheer_dance_3")(lambda: dance("cheer_dance_3", [("loV", _jump(0.86)), ("hiV", _jump(1.38, 0.32)), ("loV", _jump(0.88)), ("punchR", {}), ("punchL", {}),
                                                        ("loV", _jump(0.86)), ("hiV", _jump(1.38, 0.32)), ("hiV", _jump(0.90))],
                                     "dip, tuck jump with a high V, land soft, right and left punches, dip, a second tuck jump, land in a high V"))

@clip("umpire_square_leg_watch_loop")
def square_leg():
    n = 120
    def watch(dx=0.0, dz=0.0, yaw=0.0):
        hp = V((dx, 0.0, 0.92 + dz))
        return P(STAND, hips=H(dx, 0.04, 0.92 + dz), ptilt=18, lean=30, rball=V((-0.20, -0.04, 0)), lball=V((0.20, -0.04, 0)),
                 rtoe=V((-0.2, -1, 0)), ltoe=V((0.2, -1, 0)), rkp=V((-0.3, -1, 0.2)), lkp=V((0.3, -1, 0.2)),
                 rt=V((-0.15 + dx, -0.22, 0.76 + dz)), lt=V((0.15 + dx, -0.22, 0.76 + dz)), rp=V((-1, 0.2, -0.2)), lp=V((1, 0.2, -0.2)),
                 rh=V((0, -0.3, -1)), lh=V((0, -0.3, -1)), rhw=0.6, lhw=0.6, look=V((yaw, -20, 0.35)))
    k = [(0, watch()), (30, watch(0.015, -0.008, 0.6)), (60, watch(0.0, 0.0, 0.0)), (88, watch(-0.015, -0.008, -0.5)), (n - 1, watch())]
    export("umpire_square_leg_watch_loop", n, k, dict(type="umpire",
           note="square-leg umpire watching the crease: knees soft, a slight forward hinge, hands resting just above the knees, eyes on the popping crease; small weight shifts. "
                "Idle and signals: reuse umpire_idle_square_leg_loop and the umpire pack's signal clips (out, not out, short run, run-out referral signals)"),
           loop=True)

@clip("trophy_raise")
def trophy_raise():
    PK = 44
    grip = dict(**fing("r", HOLDF), **fing("l", HOLDF), rpal=V((1, 0, 0)), lpal=V((-1, 0, 0)), rpalw=0.8, lpalw=0.8)
    def hold(z, y, dz=0.0, dx=0.0, **kw):                        # both hands on the trophy, 0.30 m apart, palms inward
        return P(STAND, hips=H(0, 0.0, 1.03 + dz), rt=V((-0.15 + dx, y, z)), lt=V((0.15 + dx, y, z)), rp=V((-1, 0.2, -0.4)), lp=V((1, 0.2, -0.4)),
                 rh=V((0, -0.2, 1)), lh=V((0, -0.2, 1)), rhw=0.6, lhw=0.6, **grip, **kw)
    chest = hold(1.20, -0.30, look=V((0, -6, 1.5)))
    dip = hold(1.10, -0.30, dz=-0.06, lean=6, look=V((0, -2, 1.2)))
    peak = hold(2.00, -0.06, dz=0.01, lean=-6, look=V((0, -0.4, 2.5)))
    shout = hold(2.00, -0.06, dz=0.01, lean=-10, look=V((0, -0.2, 3.4)))
    k = [(0, chest), (14, chest), (28, dip), (PK, peak), (58, peak), (64, shout), (68, P(shout, rt=V((-0.11, -0.06, 2.0)), lt=V((0.19, -0.06, 2.0)))),
         (72, P(shout, rt=V((-0.19, -0.06, 2.0)), lt=V((0.11, -0.06, 2.0)))), (76, shout), (86, peak), (99, peak), (112, chest), (119, chest)]
    export("trophy_raise", 120, k, dict(type="celebration", raise_peak_frame=PK + 1, hold_until_frame=100, shake_frames=[69, 73], shout_frames=[65, 77],
           prop="trophy on RightHand; the left hand IKs to its handle",
           note="holds the trophy at chest height in both hands, dips, lifts it overhead with both arms straight, holds it, one shake with a shout (head back), lowers it to the chest"),
           probes={"right_hand_at_peak_m": (PK, "RightHand"), "left_hand_at_peak_m": (PK, "LeftHand")})

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
