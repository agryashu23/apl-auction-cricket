import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from fielding import *
CLIPS = {}
def clip(name, n, keys, meta, overlay=None, loop=False, lift=True, reach=None):
    if reach is None: reach = (lambda f: True)              # reach assist on every frame
    CLIPS[name] = (n, keys, meta, overlay, loop, lift, reach)

# ---------------- base poses
READY = P(BASE, hips=H(0,0.03,0.99), lean=12, ptilt=3, rball=V((-0.20,-0.04,0)), lball=V((0.20,-0.04,0)),
          rtoe=V((-0.15,-1,0)), ltoe=V((0.15,-1,0)), rheel=6, lheel=6,
          rt=V((-0.19,-0.26,0.95)), lt=V((0.19,-0.26,0.95)), rp=V((-0.6,0.6,-0.6)), lp=V((0.6,0.6,-0.6)),
          rh=V((0.1,-1,-0.4)), rhw=0.5, lh=V((-0.1,-1,-0.4)), lhw=0.5, look=V((0,-20,0.8)))
STAND = P(BASE, hips=H(0,0.0,1.03), lean=2, rt=V((-0.21,0.02,0.90)), lt=V((0.21,0.02,0.90)))

# ================================================================= RUNNING
def loop_run(name, *, period, stride, heading=0, contact=0.38, lean=14, bob=0.035, arm_amp=0.22, hip_h=0.98,
             backwards=False, sideways=0, style="run", direction=None, speed_note=""):
    n = 2*period + 1
    d = direction if direction is not None else (fwd(heading)*(-1 if backwards else 1))
    dist = stride*4                                   # two strides = four steps over the loop
    keys, _ = run_keys(READY, f0=0, f1=n-1, p0=(0,0), p1=tuple(d*dist), heading=heading, period=period, contact=contact,
                       lean=lean, bob=bob, arm_amp=arm_amp, hip_h=hip_h, backwards=backwards, sideways=sideways, style=style)
    keys = inplace(keys, 0, n-1, (0,0), tuple(d*dist))
    v = dist / ((n-1)/30)
    clip(name, n, keys, dict(type="locomotion loop", loop=True, in_place=True, speed_m_s=round(v,2),
                             stride_m=round(stride*2,2), note=speed_note), loop=True)

idle = [(0, READY), (15, P(READY, hips=READY["hips"]+V((0,0,-0.02)), lheel=10, rheel=10)), (30, READY),
        (45, P(READY, hips=READY["hips"]+V((0.01,0,-0.015)), look=V((0.5,-20,0.8)))), (60, READY)]
clip("run_00_ready_stance_idle_loop", 61, idle, dict(type="locomotion loop", loop=True, note="crouched ready position, weight on the balls of the feet"), loop=True, lift=False)
loop_run("run_01_walk_in_loop", period=34, stride=0.72, contact=0.62, lean=3, bob=0.015, arm_amp=0.12, hip_h=1.02, speed_note="walking in with the bowler")
loop_run("run_02_jog_loop", period=22, stride=1.20, contact=0.42, lean=8, bob=0.03, arm_amp=0.18, hip_h=0.99)
loop_run("run_03_sprint_loop", period=16, stride=1.85, contact=0.30, lean=16, bob=0.04, arm_amp=0.30, hip_h=0.98)
loop_run("run_04_backpedal_loop", period=24, stride=0.80, contact=0.45, lean=6, bob=0.025, arm_amp=0.12, hip_h=0.97, backwards=True)
loop_run("run_05_side_shuffle_left_loop", period=20, stride=0.55, contact=0.5, lean=10, bob=0.02, hip_h=0.95, sideways=1, style="shuffle", direction=V((1,0,0)))
loop_run("run_06_side_shuffle_right_loop", period=20, stride=0.55, contact=0.5, lean=10, bob=0.02, hip_h=0.95, sideways=1, style="shuffle", direction=V((-1,0,0)))


# ================================================================= helpers for fielding clips
POSCH = ("rball","lball","rt","lt","look")
DIRCH = ("rtoe","ltoe","rkp","lkp","rp","lp","rh","lh","rpal","lpal")
def place(pose, x=0.0, y=0.0, heading=0.0):
    """rotate the whole pose about the origin by heading, then move it to (x, y)"""
    Rz = R('Z', heading); p = dict(pose)
    h = V(pose["hips"]); hp = Rz @ V((h.x, h.y, 0)); p["hips"] = V((hp.x + x, hp.y + y, h.z))
    for c in POSCH: q = Rz @ V(pose[c]); p[c] = V((q.x + x, q.y + y, q.z))
    for c in DIRCH: p[c] = Rz @ V(pose[c])
    p["pyaw"] = pose["pyaw"] + heading
    return p
def hands(Cpt, style, side="both", fd=None, pd=None, spread=0.05):
    """hand targets around a ball at Cpt. style: cup (palms up, fingers down), reverse (palms out, fingers up, thumbs together), custom"""
    Cpt = V(Cpt)
    if style == "cup": fd = fd or V((0,-0.55,-0.83)); pd = pd or V((0,-0.5,0.85))
    elif style == "reverse": fd = fd or V((0,-0.12,1)); pd = pd or V((0,-1,0.12))
    fd = V(fd).normalized(); pd = V(pd).normalized()
    lat = V((1,0,0)) if abs(fd.x) < 0.7 else V((0,1,0))
    lat = (lat - fd*lat.dot(fd)).normalized()
    out = {}
    for s_, sg in (("r", -1), ("l", 1)):
        if side not in ("both", s_): continue
        w = Cpt - fd*0.075 - pd*0.035 + lat*sg*(spread if side == "both" else 0)
        out.update({s_+"t": w, s_+"h": fd, s_+"hw": 1.0, s_+"pal": pd, s_+"palw": 1.0, s_+"w": 1.0,
                    s_+"p": V((sg*0.6, 0.4, -1))})
    return out
def holding(C_, side="both"):
    d = hands(C_, "reverse", side, fd=V((0,-0.3,1)), pd=V((0,-0.2,1)) if False else None)
    return d
WITH_BALL = P(STAND, rt=V((-0.07,-0.26,1.08)), lt=V((0.07,-0.27,1.06)), rp=V((-0.6,0.4,-1)), lp=V((0.6,0.4,-1)),
              rh=V((0.3,-0.6,0.75)), rhw=0.7, lh=V((-0.3,-0.6,0.75)), lhw=0.7, rpal=V((1,0,0)), rpalw=0.8, lpal=V((-1,0,0)), lpalw=0.8,
              **fing("r", HOLDF), **fing("l", dict(T=0.3, I=0.35, M=0.35, R=0.4, P=0.4)), look=V((0,-20,1.2)))
def catch_meta(kind, frame, C_, extra=None):
    d = dict(type=kind, event="catch", event_frame=frame+1,
             ball_at_event_m=dict(forward=round(-C_[1],3), left=round(C_[0],3), up=round(C_[2],3)))
    if extra: d.update(extra)
    return d
HOLD_R = fing("r", HOLDF); HOLD_L = fing("l", HOLDF); OPEN_R = fing("r", CATCHF); OPEN_L = fing("l", CATCHF)

# ================================================================= CATCHING
# 1 cup catch
C1 = V((0,-0.48,0.62))
clip("catch_01_cup_catch", 90, [
    (0, READY), (8, READY),
    (20, P(READY, hips=H(0,0.02,0.88), lean=20, **hands(C1+V((0,-0.08,0.04)), "cup"), **OPEN_R, **OPEN_L, look=V((0,-4,0.9)))),
    (30, P(READY, hips=H(0,0.02,0.86), lean=22, **hands(C1, "cup"), **OPEN_R, **OPEN_L, look=C1)),
    (38, P(READY, hips=H(0,0.04,0.86), lean=18, **hands(V((0,-0.30,0.78)), "cup"), **HOLD_R, **HOLD_L, look=V((0,-0.3,0.7)))),
    (56, WITH_BALL), (89, WITH_BALL)], catch_meta("catch", 30, C1, dict(note="low catch, palms up, fingers down; ends standing with the ball at the chest")))
# 2 reverse cup
C2 = V((0,-0.40,1.70))
clip("catch_02_reverse_cup_catch", 90, [
    (0, READY), (8, READY),
    (20, P(STAND, hips=H(0,0.02,1.00), **hands(C2+V((0,-0.08,0.0)), "reverse"), **OPEN_R, **OPEN_L, look=V((0,-4,2.0)))),
    (30, P(STAND, hips=H(0,0.02,1.00), lean=-2, **hands(C2, "reverse"), **OPEN_R, **OPEN_L, look=C2)),
    (38, P(STAND, hips=H(0,0.04,0.98), lean=-4, **hands(V((0,-0.22,1.42)), "reverse"), **HOLD_R, **HOLD_L, look=V((0,-0.3,1.3)))),
    (56, WITH_BALL), (89, WITH_BALL)], catch_meta("catch", 30, C2, dict(note="high catch at head height, palms out, fingers up, thumbs together")))
# 3/4 one-hand catches
def one_hand(name, sg):
    side = "l" if sg > 0 else "r"; other = "r" if side == "l" else "l"
    Cc = V((sg*0.98, -0.30, 1.15))
    reach = hands(Cc, "custom", side, fd=V((sg*0.6, -0.1, 0.8)), pd=V((0,-1,0.1)))
    bal = {other+"t": V((-sg*0.45, 0.05, 1.05)), other+"p": V((-sg*0.6, 0.6, -0.8)), other+"hw": 0.0}
    step = {(side+"ball"): V((sg*0.58, -0.08, 0)), (side+"toe"): V((sg*0.5,-1,0))}
    clip(name, 90, [
        (0, READY), (8, READY),
        (22, P(READY, hips=H(sg*0.26,0.02,0.92), side=sg*16, lean=10, **step, **reach, **bal, **fing(side, CATCHF), look=Cc+V((0,-2,0.2)))),
        (30, P(READY, hips=H(sg*0.30,0.02,0.91), side=sg*18, lean=10, **step, **reach, **bal, **fing(side, CATCHF), look=Cc)),
        (37, P(READY, hips=H(sg*0.26,0.02,0.93), side=sg*10, lean=8, **step, **hands(Cc+V((-sg*0.2,0.08,-0.05)), "custom", side, fd=V((sg*0.5,-0.1,0.85)), pd=V((0,-1,0.1))),
               **bal, **fing(side, HOLDF), look=Cc+V((-sg*0.2,0,-0.1)))),
        (58, place(WITH_BALL, sg*0.18, 0.0)), (89, place(WITH_BALL, sg*0.18, 0.0))],
        catch_meta("catch", 30, Cc, dict(note=("left" if sg > 0 else "right") + " hand only, ball wide at chest height")))
one_hand("catch_03_one_hand_left", 1)
one_hand("catch_04_one_hand_right", -1)

# 5/6 diving catches
def dive(name, sg, *, C_=None, ground=False, kind="catch"):
    """sg=+1 dive to the fielder's left (+X), -1 to the right"""
    C_ = C_ or V((sg*1.92, -0.22, 0.72))
    lead, trail = ("l", "r") if sg > 0 else ("r", "l")
    fdir = V((sg*0.95, -0.2, 0.15 if not ground else -0.25)); pdir = V((0, -1, 0.25 if not ground else 0.1))
    air = hands(C_, "custom", "both", fd=fdir, pd=pdir, spread=0.06)
    land_h = hands(V((C_.x - sg*0.08, C_.y, 0.20 if not ground else 0.10)), "custom", "both", fd=V((sg, -0.15, 0.0)), pd=pdir, spread=0.05)
    k = []
    k.append((0, READY)); k.append((6, READY))
    k.append((14, P(READY, hips=H(sg*0.30,0.02,0.80), lean=18, side=sg*20, **{lead+"ball": V((sg*0.62,-0.06,0)), lead+"toe": V((sg*0.6,-1,0))},
                    look=C_+V((0,-3,0.3)), **OPEN_R, **OPEN_L)))
    k.append((20, P(READY, hips=H(sg*0.72,0.0,0.78), pside=sg*35, lean=10, side=sg*15, footup=0.6,
                    **{lead+"ball": V((sg*0.66,-0.06,0)), lead+"heel": 40, trail+"ball": V((sg*0.25,0.0,0.25)), trail+"heel": 50},
                    **hands(C_+V((-sg*0.6,0,0.1)), "custom", "both", fd=fdir, pd=pdir), look=C_+V((0,-1.5,0.1)), **OPEN_R, **OPEN_L)))
    k.append((27, P(READY, hips=H(sg*1.18,-0.12,0.52 if not ground else 0.42), pside=sg*80, lean=6, side=sg*6, footup=0.2,
                    **{lead+"ball": V((sg*0.45,-0.05,0.42)), trail+"ball": V((sg*0.35,0.08,0.30)), lead+"heel": 60, trail+"heel": 60,
                       lead+"kp": V((0,-1,0)), trail+"kp": V((0,-1,0))},
                    **air, look=C_, **OPEN_R, **OPEN_L)))
    k.append((30, P(k[-1][1], hips=H(sg*1.25,-0.13,0.48 if not ground else 0.38), **air, look=C_)))
    k.append((36, P(READY, hips=H(sg*1.30,-0.12,0.17), pside=sg*88, lean=4, side=0, footup=0.2,
                    **{lead+"ball": V((sg*0.48,-0.08,0.10)), trail+"ball": V((sg*0.40,0.10,0.08)), lead+"heel": 70, trail+"heel": 70,
                       lead+"kp": V((0,-1,0)), trail+"kp": V((0,-1,0))},
                    **land_h, look=V(land_h["rt"] if "rt" in land_h else land_h["lt"]), **HOLD_R, **HOLD_L)))
    k.append((46, P(k[-1][1], hips=H(sg*1.40,-0.12,0.17), **{lead+"ball": V((sg*0.58,-0.08,0.10)), trail+"ball": V((sg*0.50,0.10,0.08))},
                    **hands(V((C_.x+sg*0.05, C_.y, 0.22)), "custom", "both", fd=V((sg,-0.15,0.2)), pd=V((0,-0.6,0.8))))))
    # get up: roll to kneeling, then stand
    k.append((64, P(READY, hips=H(sg*1.35,0.05,0.55), pside=sg*12, lean=20, footup=1.0,
                    **{lead+"ball": V((sg*1.62,-0.25,0)), lead+"toe": V((sg*0.3,-1,0)), trail+"ball": V((sg*1.15,0.32,0)), trail+"heel": 75,
                       trail+"kp": V((0,-0.3,-1))},
                    rt=V((sg*1.35-0.07,-0.32,0.80)), lt=V((sg*1.35+0.07,-0.33,0.79)), rh=V((0.3,-0.6,0.75)), rhw=0.7, lh=V((-0.3,-0.6,0.75)), lhw=0.7,
                    **HOLD_R, **HOLD_L, look=V((sg*1.35,-20,1.0)))))
    k.append((84, place(WITH_BALL, sg*1.40, 0.0))); k.append((119, place(WITH_BALL, sg*1.40, 0.0)))
    clip(name, 120, k, catch_meta(kind, 30, C_, dict(note="ends standing with the ball %.1f m to the %s" % (1.4, "left" if sg > 0 else "right"),
                                                     end_position_m=dict(forward=0.0, left=round(sg*1.40,2)))))
dive("catch_05_diving_left", 1)
dive("catch_06_diving_right", -1)

# 7 diving forward (low ball dropping short)
C7 = V((0,-3.30,0.14))
k7 = [(0, READY), (6, READY)]
rk, _ = run_keys(READY, f0=6, f1=26, p0=(0,0), p1=(0,-1.8), heading=0, period=16, contact=0.32, lean=18, ease="accel")
k7 += rk[1:]
k7 += [(31, P(READY, hips=H(0,-2.25,0.78), ptilt=50, lean=10, footup=0.4, rball=V((-0.12,-1.85,0.05)), rheel=50, lball=V((0.12,-1.6,0.30)), lheel=60,
             **hands(C7+V((0,0.4,0.3)), "custom", "both", fd=V((0,-1,-0.1)), pd=V((0,-0.3,1))), **OPEN_R, **OPEN_L, look=C7)),
       (36, P(READY, hips=H(0,-2.55,0.30), ptilt=84, lean=6, footup=0.2, rball=V((-0.12,-1.70,0.28)), rheel=70, lball=V((0.12,-1.65,0.38)), lheel=70,
             rkp=V((0,-1,-1)), lkp=V((0,-1,-1)),
             **hands(C7, "custom", "both", fd=V((0,-1,0.05)), pd=V((0,0,1))), **OPEN_R, **OPEN_L, look=C7)),
       (42, P(READY, hips=H(0,-2.62,0.17), ptilt=88, lean=2, footup=0.2, rball=V((-0.12,-1.75,0.06)), rheel=75, lball=V((0.12,-1.72,0.06)), lheel=75,
             rkp=V((0,0,-1)), lkp=V((0,0,-1)),
             **hands(C7+V((0,0.05,0.06)), "custom", "both", fd=V((0,-1,0.2)), pd=V((0,0,1))), **HOLD_R, **HOLD_L, look=C7)),
       (60, P(READY, hips=H(0,-2.60,0.55), ptilt=15, lean=25, rball=V((-0.15,-2.35,0)), rheel=75, lball=V((0.18,-3.0,0)), rkp=V((0,-0.3,-1)),
             rt=V((-0.07,-3.0,0.85)), lt=V((0.07,-3.0,0.84)), **HOLD_R, **HOLD_L, look=V((0,-20,1.0)))),
       (82, place(WITH_BALL, 0, -2.75)), (119, place(WITH_BALL, 0, -2.75))]
clip("catch_07_diving_forward", 120, k7, catch_meta("catch", 36, C7, dict(note="two running steps, dives forward under a dropping ball", end_position_m=dict(forward=2.75, left=0))))

# 8 front running catch
k8 = [(0, READY), (6, READY)]
rk, pl = run_keys(READY, f0=6, f1=56, p0=(0,0), p1=(0,-7.0), heading=0, period=16, contact=0.30, lean=16, ease="accel"); k8 += rk[1:]
rk2, _ = run_keys(READY, f0=56, f1=80, p0=(0,-7.0), p1=(0,-9.2), heading=0, period=18, contact=0.4, lean=8, ease="decel"); k8 += rk2[1:]
k8 += [(90, place(WITH_BALL, 0, -9.5)), (109, place(WITH_BALL, 0, -9.5))]
def ov8(f, p):
    w = min(1, max(0, (f-36)/6)) * (1 - min(1, max(0, (f-76)/10)))
    if w <= 0: return
    p["gait"] = 0.0
    hp = V(p["hips"]) + V((0,0,HIP0))
    t1 = hands(V((0, hp.y-0.55, 0.70)), "cup"); t2 = hands(V((0, hp.y-0.32, 0.95)), "reverse", fd=V((0,-0.3,1)), pd=V((0,-0.9,0.4)))
    u = min(1.0, max(0.0, (f-45)/7.0)); u = u*u*(3-2*u)                      # ease from the catch into the chest over ~7 frames
    tgt = {k_: (V(t1[k_]).lerp(V(t2[k_]), u) if isinstance(t1[k_], Vector) else t1[k_]*(1-u) + t2[k_]*u) for k_ in t1}
    fg = {k_: OPEN_R.get(k_, OPEN_L.get(k_))*(1-u) + {**HOLD_R, **HOLD_L}[k_]*u for k_ in {**HOLD_R, **HOLD_L}}
    for k_, v_ in tgt.items():
        p[k_] = (V(p[k_]).lerp(v_, w) if isinstance(v_, Vector) else p[k_]*(1-w) + v_*w)
    for k_, v_ in fg.items(): p[k_] = p[k_]*(1-w) + v_*w
    p["ptilt"] = p["ptilt"] + 10*w*pulse(f, 44, 8)
    if f <= 46: p["look"] = V((0, hp.y-0.55, 0.70)).lerp(V((0, hp.y-6, 3.0)), max(0, (44-f)/8))
C8 = None
clip("catch_08_front_running_catch", 110, k8, dict(type="catch", event="catch", event_frame=45, note="sprints in, takes the ball low in front while running, slows to a stop",
                                                   end_position_m=dict(forward=9.5, left=0)), overlay=ov8)

# 9 back running catch (over the shoulder)
k9 = [(0, READY), (4, READY),
      (12, P(READY, hips=H(-0.05,0.10,0.95), pyaw=110, rball=V((-0.25,0.30,0)), rtoe=V((0.2,1,0)), ltoe=V((0.6,-0.4,0)), look=V((1,-6,6))))]
rk, _ = run_keys(READY, f0=12, f1=66, p0=(0,0.3), p1=(0,7.5), heading=180, period=17, contact=0.32, lean=12, ease="accel",
                 extra=dict(look=V((0,0,0))))
k9 += rk[1:]
rk2, _ = run_keys(READY, f0=66, f1=90, p0=(0,7.5), p1=(0,9.6), heading=180, period=18, contact=0.4, lean=6, ease="decel"); k9 += rk2[1:]
k9 += [(98, place(WITH_BALL, 0, 9.8, 180)), (109, place(WITH_BALL, 0, 9.8, 180))]
def ov9(f, p):
    hp = V(p["hips"]) + V((0,0,HIP0))
    # head turned back over the left shoulder to watch the ball dropping from behind
    if 12 <= f <= 64:
        u = (f-12)/52; ball = V((0.6, hp.y + 4.0 - 3.6*u, 8.0 - 6.3*u)); p["look"] = ball
    w = min(1, max(0, (f-50)/7)) * (1 - min(1, max(0, (f-82)/10)))
    if w <= 0: return
    p["gait"] = 0.0
    t1 = hands(V((0.12, hp.y+0.48, 1.62)), "custom", fd=V((0,1,0.35)), pd=V((0,0.2,1))); t2 = hands(V((0.05, hp.y+0.30, 1.05)), "custom", fd=V((0,0.3,1)), pd=V((0,1,0.3)))
    u = min(1.0, max(0.0, (f-60)/8.0)); u = u*u*(3-2*u)
    tgt = {k_: (V(t1[k_]).lerp(V(t2[k_]), u) if isinstance(t1[k_], Vector) else t1[k_]*(1-u) + t2[k_]*u) for k_ in t1}
    fg = {k_: OPEN_R.get(k_, OPEN_L.get(k_))*(1-u) + {**HOLD_R, **HOLD_L}[k_]*u for k_ in {**HOLD_R, **HOLD_L}}
    for k_, v_ in tgt.items(): p[k_] = (V(p[k_]).lerp(v_, w) if isinstance(v_, Vector) else p[k_]*(1-w) + v_*w)
    for k_, v_ in fg.items(): p[k_] = p[k_]*(1-w) + v_*w
    if f > 60: p["look"] = V(p["look"]).lerp(V((0, hp.y+20, 1.0)), min(1,(f-60)/12))
clip("catch_09_back_running_catch", 110, k9, dict(type="catch", event="catch", event_frame=61,
     note="turns and sprints back (away from the batter), watches the ball over the left shoulder, takes it in front of the face; ends facing away from the batter",
     end_position_m=dict(forward=-9.8, left=0), end_heading_deg=180), overlay=ov9)

# 10 jumping catch
C10 = V((0,-0.15,2.62))
clip("catch_10_jumping_catch", 90, [
    (0, READY), (8, READY),
    (16, P(READY, hips=H(0,0.04,0.78), lean=22, rheel=10, lheel=10, rt=V((-0.25,0.15,0.70)), lt=V((0.25,0.15,0.70)), look=V((0,-3,3.5)))),
    (23, P(STAND, hips=H(0,0.0,1.10), rheel=70, lheel=70, **hands(C10+V((0,-0.1,-0.5)), "reverse"), **OPEN_R, **OPEN_L, look=C10)),
    (30, P(STAND, hips=H(0,-0.02,1.48), footup=0.6, rball=V((-0.12,0.0,0.42)), lball=V((0.12,-0.04,0.46)), rheel=60, lheel=60,
           **hands(C10, "reverse"), **OPEN_R, **OPEN_L, look=C10)),
    (36, P(STAND, hips=H(0,-0.02,1.35), footup=0.6, rball=V((-0.14,0.0,0.25)), lball=V((0.14,-0.04,0.28)), rheel=40, lheel=40,
           **hands(V((0,-0.20,2.15)), "reverse"), **HOLD_R, **HOLD_L, look=V((0,-0.3,2.0)))),
    (42, P(READY, hips=H(0,0.03,0.80), lean=18, **hands(V((0,-0.26,1.25)), "reverse"), **HOLD_R, **HOLD_L, look=V((0,-3,1.0)))),
    (60, WITH_BALL), (89, WITH_BALL)], catch_meta("catch", 30, C10, dict(note="two-footed leap, reverse-cup catch above the head, lands and absorbs")))

# 11 boundary relay catch (rope 0.9 m behind the fielder)
REL_R, REL_F = V((0.06,0.48,1.98)), 48            # toss release point / frame
CAT_R, CAT_F = V((0.0,0.02,1.55)), 100            # re-catch point / frame
tt = (CAT_F-REL_F)/30.0; g = 9.81
vel = V(((CAT_R.x-REL_R.x)/tt, (CAT_R.y-REL_R.y)/tt, (CAT_R.z-REL_R.z + 0.5*g*tt*tt)/tt))
def ball11(f):
    t = (f-REL_F)/30.0; return REL_R + vel*t + V((0,0,-0.5*g*t*t))
C11 = V((0,0.42,2.44))
k11 = [(0, READY), (6, READY),
       (14, P(READY, hips=H(0,0.20,0.95), rball=V((-0.20,0.28,0)), look=V((0,-3,4)))),
       (22, P(READY, hips=H(0,0.38,0.93), lball=V((0.20,0.40,0)), look=V((0,-1,4)))),
       (26, P(READY, hips=H(0,0.40,0.80), lean=18, rball=V((-0.20,0.38,0)), lball=V((0.20,0.40,0)), look=C11)),
       (30, P(STAND, hips=H(0,0.44,1.46), footup=0.6, rball=V((-0.12,0.45,0.44)), lball=V((0.12,0.42,0.48)), rheel=60, lheel=60,
              **hands(C11, "reverse"), **OPEN_R, **OPEN_L, look=C11)),
       (38, P(READY, hips=H(0,0.62,0.85), lean=10, rball=V((-0.18,0.66,0)), lball=V((0.16,0.70,0)),
              **hands(V((0,0.50,1.80)), "reverse"), **HOLD_R, **HOLD_L, look=V((0,0.9,0.0)))),
       (44, P(STAND, hips=H(0.05,0.70,0.98), lean=-6, side=-6, lball=V((0.12,0.76,0)), rball=V((-0.20,0.95,0.20)),
              **hands(V((0.03,0.52,1.70)), "reverse"), **HOLD_R, **HOLD_L, look=V((0,0.95,0.0)))),
       (REL_F, P(STAND, hips=H(0.05,0.74,1.00), lean=-8, side=-4, lball=V((0.12,0.78,0)), rball=V((-0.20,1.00,0.25)),
              **hands(REL_R, "reverse"), **OPEN_R, **OPEN_L, look=REL_R+V((0,-0.3,1.0)))),
       (56, P(STAND, hips=H(0.0,1.05,0.99), lean=-4, rball=V((-0.16,1.18,0)), lball=V((0.12,0.80,0)),
              rt=V((-0.40,1.0,1.35)), lt=V((0.40,1.0,1.35)), look=ball11(56))),
       (64, P(STAND, hips=H(0.0,1.22,1.00), rball=V((-0.16,1.18,0)), lball=V((0.16,1.30,0)), rt=V((-0.35,1.2,1.25)), lt=V((0.35,1.2,1.25)), look=ball11(64))),
       (74, P(STAND, hips=H(0.0,0.85,1.00), rball=V((-0.16,0.50,0)), lball=V((0.16,1.30,0)), rt=V((-0.3,0.8,1.2)), lt=V((0.3,0.8,1.2)), look=ball11(74))),
       (84, P(STAND, hips=H(0.0,0.30,1.00), rball=V((-0.16,0.50,0)), lball=V((0.16,0.25,0)), rt=V((-0.3,0.3,1.2)), lt=V((0.3,0.3,1.2)), look=ball11(84))),
       (94, P(STAND, hips=H(0.0,0.20,1.00), rball=V((-0.17,0.18,0)), lball=V((0.16,0.25,0)), **hands(CAT_R+V((0,-0.03,0.22)), "reverse"), **OPEN_R, **OPEN_L, look=ball11(94))),
       (CAT_F, P(STAND, hips=H(0.0,0.20,0.98), rball=V((-0.17,0.18,0)), lball=V((0.16,0.25,0)), **hands(CAT_R, "reverse"), **OPEN_R, **OPEN_L, look=CAT_R)),
       (108, P(STAND, hips=H(0.0,0.20,0.96), rball=V((-0.17,0.18,0)), lball=V((0.16,0.25,0)), **hands(V((0,0.0,1.25)), "reverse"), **HOLD_R, **HOLD_L, look=V((0,-1,1.0)))),
       (124, place(WITH_BALL, 0, 0.20)), (149, place(WITH_BALL, 0, 0.20))]
clip("catch_11_boundary_relay_catch", 150, k11, dict(type="catch", event="catch", event_frame=31,
     ball_at_event_m=dict(forward=-C11.y, left=C11.x, up=C11.z),
     toss=dict(release_frame=REL_F+1, release_point_m=dict(forward=round(-REL_R.y,3), left=round(REL_R.x,3), up=round(REL_R.z,3)),
               release_velocity_m_s=dict(forward=round(-vel.y,3), left=round(vel.x,3), up=round(vel.z,3)), gravity=9.81),
     recatch_frame=CAT_F+1, recatch_point_m=dict(forward=round(-CAT_R.y,3), left=round(CAT_R.x,3), up=round(CAT_R.z,3)),
     rope_m_behind_start=0.9,
     note="leaps and catches near the rope, momentum carries back; tosses the ball up before crossing, steps over the rope and back in, re-catches"))

# ================================================================= FIELDING (stopping the ball)
def stop_meta(frame, C_, extra=None):
    d = dict(type="ground fielding", event="ball collected", event_frame=frame+1,
             ball_at_event_m=dict(forward=round(-C_[1],3), left=round(C_[0],3), up=round(C_[2],3)))
    if extra: d.update(extra)
    return d
# 12 long barrier
C12 = V((0.02,-0.48,0.06))
LB = dict(hips=H(0.02,-0.02,0.55), pyaw=-25, lean=30, rball=V((-0.30,0.32,0)), rtoe=V((0.1,-1,0)), rheel=75, rkp=V((0.2,-0.4,-1)),
          lball=V((0.22,-0.12,0)), ltoe=V((0.7,-1,0)))
clip("field_12_long_barrier", 100, [
    (0, READY), (8, READY),
    (16, P(READY, hips=H(0,0.0,0.85), lball=V((0.22,-0.12,0)), ltoe=V((0.7,-1,0)), lean=18, look=V((0,-5,0.1)))),
    (26, P(READY, **LB, **hands(C12+V((0,-0.04,0.04)), "custom", fd=V((0,-0.3,-1)), pd=V((0,-1,0.1))), **OPEN_R, **OPEN_L, look=C12)),
    (32, P(READY, **LB, **hands(C12, "custom", fd=V((0,-0.3,-1)), pd=V((0,-1,0.1))), **OPEN_R, **OPEN_L, look=C12)),
    (40, P(READY, **LB, **hands(V((0.0,-0.30,0.40)), "custom", fd=V((0,-0.3,1)), pd=V((0,-0.6,0.8))), **HOLD_R, **HOLD_L, look=V((0,-3,0.5)))),
    (62, WITH_BALL), (99, WITH_BALL)], stop_meta(32, C12, dict(note="turns slightly side-on, right knee down beside the left heel, body behind the hands")))

# 13 two-hand pick-up
C13 = V((-0.02,-2.62,0.06))
k13 = [(0, READY), (6, READY)]
rk, _ = run_keys(READY, f0=6, f1=26, p0=(0,0), p1=(0,-2.0), heading=0, period=18, contact=0.38, lean=10, ease="decel"); k13 += rk[1:]
k13 += [(32, P(READY, hips=H(-0.02,-2.12,0.70), lean=46, ptilt=8, rball=V((-0.16,-1.98,0)), rheel=25, lball=V((0.14,-2.42,0)), ltoe=V((0.3,-1,0)),
               **hands(C13, "cup", fd=V((0,-0.4,-1)), pd=V((0,-1,0.3))), **OPEN_R, **OPEN_L, look=C13)),
        (40, P(READY, hips=H(-0.02,-2.12,0.80), lean=30, rball=V((-0.16,-1.98,0)), lball=V((0.14,-2.42,0)), ltoe=V((0.3,-1,0)),
               **hands(V((-0.06,-2.40,0.75)), "reverse", fd=V((0,-0.3,1)), pd=V((0,-0.9,0.4))), **HOLD_R, **HOLD_L, look=V((0,-20,1.0)))),
        (58, place(WITH_BALL, 0, -2.2)), (89, place(WITH_BALL, 0, -2.2))]
clip("field_13_two_hand_pickup", 90, k13, stop_meta(32, C13, dict(note="runs in, gets low, collects with both hands beside the front foot", end_position_m=dict(forward=2.2, left=0))))

# 14 one-hand pick-up on the run (starts already running at jog pace)
k14 = []
rk, _ = run_keys(READY, f0=0, f1=44, p0=(0,0), p1=(0,-5.6), heading=0, period=20, contact=0.36, lean=12, first="r"); k14 += rk
rk2, _ = run_keys(READY, f0=44, f1=62, p0=(0,-5.6), p1=(0,-6.8), heading=0, period=20, contact=0.42, lean=6, ease="decel"); k14 += rk2[1:]
k14 += [(70, place(WITH_BALL, 0, -7.0)), (74, place(WITH_BALL, 0, -7.0))]
PICK14 = 28
def ov14(f, p):
    w = pulse(f, PICK14, 10)
    if w <= 0 and f < PICK14: return
    if w > 0: p["gait"] = 0.0
    hp = V(p["hips"]) + V((0,0,HIP0))
    if f <= PICK14 + 10:
        Cg = V((-0.20, hp.y-0.30, 0.05))
        t = hands(Cg, "custom", "r", fd=V((0,-0.4,-1)), pd=V((0,-1,0.2)))
        for k_, v_ in t.items(): p[k_] = (V(p[k_]).lerp(v_, w) if isinstance(v_, Vector) else p[k_]*(1-w) + v_*w)
        p["ptilt"] += 28*w; p["hips"] = V(p["hips"]) + V((0,0,-0.18*w)); p["side"] += -12*w
        p["look"] = V(p["look"]).lerp(Cg, w)
    if f >= PICK14:
        for k_, v_ in HOLD_R.items(): p[k_] = v_
C14 = V((-0.20, -3.9, 0.05))
clip("field_14_one_hand_pickup", 75, k14, stop_meta(PICK14, C14, dict(note="collects with the right hand beside the right foot while running at jog pace, then slows; clip starts already running",
                                                                       end_position_m=dict(forward=7.0, left=0))), overlay=ov14)

# 15/16 dive stops
dive("field_15_dive_stop_left", 1, C_=V((1.80,-0.18,0.06)), ground=True, kind="ground fielding")
dive("field_16_dive_stop_right", -1, C_=V((-1.80,-0.18,0.06)), ground=True, kind="ground fielding")

# 17 slide stop (chasing the ball toward the boundary)
k17 = [(0, READY), (6, READY)]
rk, _ = run_keys(READY, f0=6, f1=42, p0=(0,0), p1=(0,-5.2), heading=0, period=16, contact=0.30, lean=16, ease="accel"); k17 += rk[1:]
SL = dict(footup=0.3, pside=24, lean=-20, side=8, rkp=V((0,-1,0.6)), lkp=V((0.6,-0.3,-0.2)), rtoe=V((0,-0.5,1)), ltoe=V((0.3,-1,0)))
C17 = V((-0.26,-7.02,0.08))
k17 += [(47, P(READY, hips=H(0.05,-5.85,0.45), **SL, rball=V((-0.12,-6.75,0.05)), lball=V((0.18,-5.85,0.0)), lheel=70,
               rt=V((-0.35,-6.4,0.5)), lt=V((0.45,-5.6,0.30)), look=C17)),
        (53, P(READY, hips=H(0.08,-6.40,0.24), **SL, rball=V((-0.12,-7.30,0.05)), lball=V((0.22,-6.30,0.0)), lheel=80,
               **hands(C17, "custom", "r", fd=V((-0.4,-0.6,-0.7)), pd=V((0,-1,0.2))), lt=V((0.48,-6.25,0.20)), lp=V((1,0.3,-0.5)),
               **OPEN_R, look=C17)),
        (62, P(READY, hips=H(0.10,-6.62,0.22), **SL, rball=V((-0.12,-7.50,0.05)), lball=V((0.24,-6.50,0.0)), lheel=80,
               **hands(V((-0.25,-7.0,0.20)), "custom", "r", fd=V((0,-0.3,1)), pd=V((0,-1,0.2))), lt=V((0.48,-6.45,0.20)), lp=V((1,0.3,-0.5)),
               **HOLD_R, look=V((-0.2,-7.0,0.2)))),
        (78, P(READY, hips=H(0.0,-6.70,0.60), lean=20, rball=V((-0.18,-7.20,0)), rtoe=V((0,-1,0)), lball=V((0.18,-6.40,0)), lheel=75, lkp=V((0,-0.3,-1)),
               rt=V((-0.10,-7.0,0.90)), lt=V((0.10,-7.0,0.88)), **HOLD_R, look=V((0,-20,1.0)))),
        (96, place(WITH_BALL, 0, -6.85, 180)), (119, place(WITH_BALL, 0, -6.85, 180))]
clip("field_17_slide_stop", 120, k17, stop_meta(53, C17, dict(note="sprints after the ball, slides on the left hip with the right leg out, stops it with the right hand, gets up and turns back to face the field",
                                                               end_position_m=dict(forward=6.85, left=0), end_heading_deg=180)))

# ================================================================= THROWING (target straight ahead, -Y)
REL18 = 30
k18 = [(0, WITH_BALL), (4, WITH_BALL),
       (10, P(WITH_BALL, hips=H(0,-0.20,0.98), pyaw=-60, rball=V((-0.10,-0.10,0.08)), lball=V((0.15,-0.40,0)), ltoe=V((-0.4,-1,0)),
              rt=V((-0.05,-0.05,1.10)), lt=V((0.05,-0.35,1.10)), look=V((0,-25,1.5)))),
       (16, P(WITH_BALL, hips=H(0,-0.55,0.97), pyaw=-88, rball=V((0.0,-0.55,0)), rtoe=V((-1,0.15,0)), lball=V((0.05,-0.80,0.12)), ltoe=V((-0.3,-1,0)),
              rt=V((0.10,-0.25,1.20)), lt=V((0.0,-0.95,1.25)), rhw=0.0, lhw=0.0, rpalw=0, lpalw=0, look=V((0,-25,1.5)))),
       (23, P(WITH_BALL, hips=H(0,-0.85,0.93), pyaw=-88, lean=-6, side=-8, rball=V((0.0,-0.55,0)), rtoe=V((-1,0.15,0)), rheel=10,
              lball=V((0.0,-1.40,0)), ltoe=V((-0.3,-1,0)),
              rt=V((0.05,-0.20,1.55)), rp=V((0.3,1,-0.6)), lt=V((0.02,-1.40,1.35)), lp=V((0.6,-0.3,-0.6)), rhw=0.4, rh=V((0,1,0.6)), lhw=0,
              rpalw=0, lpalw=0, look=V((0,-25,1.5)))),
       (27, P(WITH_BALL, hips=H(0,-0.95,0.92), pyaw=-50, twist=-10, lean=0, rball=V((0.0,-0.55,0)), rtoe=V((-1,0.15,0)), rheel=30,
              lball=V((0.0,-1.40,0)), ltoe=V((-0.3,-1,0)),
              rt=V((-0.20,-0.70,1.85)), rp=V((-1,0.3,0.2)), lt=V((0.25,-1.00,1.10)), lp=V((0.8,0.3,-0.6)), rhw=0.5, rh=V((0,-0.2,1)), lhw=0,
              rpalw=0, lpalw=0, look=V((0,-25,1.5)))),
       (REL18, P(WITH_BALL, hips=H(0,-1.02,0.92), pyaw=-10, twist=10, lean=18, rball=V((0.0,-0.60,0)), rtoe=V((-1,0.15,0)), rheel=50,
              lball=V((0.0,-1.40,0)), ltoe=V((-0.3,-1,0)),
              rt=V((-0.20,-1.55,1.75)), rp=V((-1,0.3,-0.3)), lt=V((0.30,-0.95,0.95)), lp=V((0.8,0.3,-0.6)), rhw=0.8, rh=V((0,-1,0.3)), lhw=0,
              rpalw=0.6, rpal=V((0,-1,0)), lpalw=0, **fing("r", CATCHF), look=V((0,-25,1.5)))),
       (38, P(WITH_BALL, hips=H(0,-1.10,0.88), pyaw=15, twist=25, lean=38, rball=V((-0.05,-0.85,0.05)), rtoe=V((-0.5,-1,0)), rheel=70,
              lball=V((0.0,-1.40,0)), ltoe=V((-0.3,-1,0)),
              rt=V((0.30,-1.50,0.60)), rp=V((-0.5,0,-1)), lt=V((0.35,-0.95,0.85)), rhw=0.3, rh=V((0.5,0,-1)), rpalw=0, lpalw=0,
              **fing("r", RELAXF), look=V((0,-25,1.2)))),
       (48, P(place(READY, -0.02, -1.48), hips=H(-0.05,-1.40,0.93), lean=16, rball=V((-0.22,-1.60,0)), lball=V((0.18,-1.45,0)), look=V((0,-25,1.0)))),
       (74, place(READY, 0, -1.50))]
clip("throw_18_overarm_throw", 75, k18, dict(type="throw", event="release", event_frame=REL18+1, throw_direction="forward (-Y in Blender = the rig's facing direction at frame 1)",
     note="crow hop, side-on, arm cocked high, whips over the top; attach the ball to RightHand until the release frame",
     end_position_m=dict(forward=1.5, left=0)))
REL19 = 18
CR = P(READY, hips=H(0,0.05,0.80), lean=30, rt=V((-0.22,-0.18,0.48)), rp=V((-0.6,0.5,-0.5)), lt=V((0.18,-0.32,0.62)),
       rh=V((0,-0.4,-1)), rhw=0.6, **HOLD_R, look=V((0,-12,0.4)))
clip("throw_19_underarm_throw", 50, [
    (0, CR), (6, CR),
    (11, P(CR, rt=V((-0.24,0.12,0.50)), rh=V((0,0.5,-1)), lball=V((0.18,-0.30,0)), ltoe=V((0.1,-1,0)), hips=H(0,-0.05,0.80))),
    (REL19, P(CR, rt=V((-0.16,-0.60,0.62)), rh=V((0,-1,0.3)), rhw=0.8, lball=V((0.18,-0.45,0)), hips=H(0,-0.18,0.82), lean=26,
              **fing("r", CATCHF))),
    (26, P(CR, rt=V((-0.10,-0.70,1.05)), rh=V((0,-0.6,1)), lball=V((0.18,-0.45,0)), hips=H(0,-0.22,0.86), lean=18, **fing("r", RELAXF))),
    (49, place(READY, 0, -0.25))],
    dict(type="throw", event="release", event_frame=REL19+1, note="from a crouch after a pick-up: short backswing, step, underarm flick at the stumps; attach the ball to RightHand until release"))

# ================================================================= RUNNING: transitions
k = [(0, READY), (6, READY)]; rk, _ = run_keys(READY, f0=6, f1=46, p0=(0,0), p1=(0,-6.5), heading=0, period=16, contact=0.30, lean=16, ease="accel"); k += rk[1:]
clip("run_07_start_sprint", 47, k, dict(type="locomotion", note="from ready position to full sprint (root motion forward ~6.5 m); blend into run_03_sprint_loop at the end"))
rk, _ = run_keys(READY, f0=0, f1=26, p0=(0,0), p1=(0,-3.6), heading=0, period=16, contact=0.32, lean=14, ease="decel")
k = rk + [(34, place(READY, 0, -3.8)), (49, place(READY, 0, -3.8))]
clip("run_08_sprint_stop", 50, k, dict(type="locomotion", note="from sprint to a stop in the ready position (root motion ~3.8 m)"))
k = [(0, READY), (3, READY),
     (10, P(READY, hips=H(-0.05,0.10,0.94), pyaw=110, rball=V((-0.25,0.30,0)), rtoe=V((0.2,1,0)), ltoe=V((0.6,-0.4,0)), look=V((0,10,1.2))))]
rk, _ = run_keys(READY, f0=10, f1=46, p0=(0,0.3), p1=(0,5.5), heading=180, period=16, contact=0.30, lean=16, ease="accel"); k += rk[1:]
clip("run_09_turn_and_sprint_back", 47, k, dict(type="locomotion", note="drop-step turn and sprint back toward the boundary behind (root motion ~5.5 m, ends facing away)", end_heading_deg=180))

if __name__ == "__main__":
    names = sys.argv[1:] or list(CLIPS)
    data, metas = {}, {}
    for nm in names:
        n, keys, meta, ov, loop, lift, reach = CLIPS[nm]
        data[nm] = make_field(nm, n, keys, overlay=ov, loop=loop, lift=lift, reach=reach)
        e = make_field.last_errs; ev = meta.get("event_frame")
        metas[nm] = dict(file=nm+".fbx", frames=n, **meta)
        print(nm, "ok", "max hand err %.3f" % max(e), ("event err %.3f" % e[ev-1]) if ev else "", flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "fd_frames.pkl"),"wb")); json.dump(metas, open(_os.path.join(APL_WORK, "fd_meta.json"),"w"), indent=1)
