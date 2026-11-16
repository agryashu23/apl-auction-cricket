import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from batting import *

def frame_q(b):
    a = V(b["ba"]).normalized(); n = ortho(b["bn"], a); e = a.cross(n)
    return Matrix((a, n, e)).transposed().to_quaternion()
def mid_bat(b1, b2, t=0.5):
    q = frame_q(b1).slerp(frame_q(b2), t); M = q.to_matrix()
    return dict(bg=V(b1["bg"]).lerp(V(b2["bg"]), t), ba=M.col[0], bn=M.col[1], batw=1.0)
def blend(p1, p2, t):
    f1, f2 = flatten(p1), flatten(p2)
    return unflatten({k: f1[k]*(1-t) + f2[k]*t for k in f1})
def unit(*v): return V(v).normalized()

# ---------------- stance & common bats
STANCE = dict(BASE, hips=V((-0.02,0.07,-0.13)), lean=26, side=-2, twist=4, pyaw=0,
              **bat_g((-0.02,-0.20,0.78), (-0.10,-0.16,-1), (1,0,0)))
BOWLER = V((20,0,1.6))
RELAX = bat_g((0.18,-0.26,0.86), (0.15,-0.2,-1), (1,0,0))
BL_HIGH = bat_g((-0.12,-0.10,1.14), (-0.45,-0.28,0.85), (0.15,-1,-0.4))      # classic high backlift
BL_LOW = bat_g((-0.08,-0.16,0.98), (-0.75,-0.35,0.45), (0.3,-1,-0.4))        # short backlift (defence / glance)
def far(S, d, lofted, dist=12):
    d = V((d[0], d[1], 0)).normalized()
    return V(S) + d*dist + V((0, 0, 4.0 if lofted else -0.6))

SHOTS, META = {}, {}

# ---------------- FRONT FOOT template (contact at frame 30 = file frame 31)
def FF(name, *, stride, ltoe, hips_c, body_c, contact, follow, finish, body_f, shot_dir, lofted,
       backlift=BL_HIGH, rheel_f=35, back=None, defence=False, n=90, meta=None):
    S = contact["bg"] + contact["ba"]*(SWEET-GRIP_R)
    land_hips = STANCE["hips"].lerp(V(hips_c), 0.6)
    kb = dict(back or {})
    trig = P(STANCE, hips=STANCE["hips"]+V((-0.03,0,0)), **mid_bat(STANCE, backlift, 0.5), look=BOWLER)
    land = P(STANCE, lball=stride, ltoe=ltoe, hips=land_hips, **blend(STANCE, P(STANCE, **body_c), 0.5) and {},
             **{k: (STANCE[k]*0.4 + body_c[k]*0.6) for k in body_c}, **backlift, look=S + V((2.5,0,0.5)), **kb)
    con = P(STANCE, lball=stride, ltoe=ltoe, hips=V(hips_c), **body_c, **contact, look=S, **kb)
    mid = P(blend(land, con, 0.5), **mid_bat(backlift, contact, 0.55))
    lf = far(S, shot_dir, lofted)
    if defence:
        f1 = P(con, look=S + V((-0.3,0,-0.1))); fin = P(con, look=S + V((-0.5,0,-0.15)), lean=con["lean"]+2)
    else:
        f1 = P(blend(con, P(con, **body_f), 0.5), **follow, look=S.lerp(lf, 0.3), rheel=rheel_f*0.5)
        fin = P(con, **body_f, **finish, look=lf, rheel=rheel_f, hips=V(hips_c)+V((0.04,0,0.03)))
    hold = P(fin, hips=fin["hips"]+V((0,0,0.01)))
    rel = P(fin, **RELAX, lean=12, side=0, twist=6, pyaw=5, rheel=0, look=BOWLER, hips=V(hips_c)+V((-0.05,0,0.06)))
    keys = [(0, STANCE), (6, STANCE), (14, trig), (22, land), (26, mid), (30, con), (36, f1), (44, fin),
            (58, hold), (72, rel), (n-1, STANCE)]
    SHOTS[name] = (n, keys, 30, dict(footwork="front_foot", lofted=lofted, shot_direction=[round(x,2) for x in V((shot_dir[0],shot_dir[1],0)).normalized()][:2], **(meta or {})))

# ---------------- BACK FOOT template
def BF(name, *, rb, rtoe, lb, ltoe, hips_c, body_c, contact, follow, finish, body_f, shot_dir, lofted,
       backlift=BL_HIGH, defence=False, n=90, lheel_f=0, meta=None):
    S = contact["bg"] + contact["ba"]*(SWEET-GRIP_R)
    step = P(STANCE, rball=rb, rtoe=rtoe, hips=STANCE["hips"].lerp(V(hips_c), 0.5), **mid_bat(STANCE, backlift, 0.6), look=S+V((3,0,0.5)))
    land = P(STANCE, rball=rb, rtoe=rtoe, lball=lb, ltoe=ltoe, hips=V(hips_c)+V((0.02,0,0)),
             **{k: (STANCE[k]*0.4 + body_c[k]*0.6) for k in body_c}, **backlift, look=S+V((1.5,0,0.3)))
    con = P(land, hips=V(hips_c), **body_c, **contact, look=S)
    mid = P(blend(land, con, 0.5), **mid_bat(backlift, contact, 0.55))
    lf = far(S, shot_dir, lofted)
    if defence:
        f1 = P(con, look=S + V((-0.3,0,-0.2))); fin = P(con, look=S + V((-0.4,0,-0.3)))
    else:
        f1 = P(blend(con, P(con, **body_f), 0.5), **follow, look=S.lerp(lf, 0.3))
        fin = P(con, **body_f, **finish, look=lf, lheel=lheel_f)
    hold = P(fin, hips=fin["hips"]+V((0,0,0.01)))
    rel = P(fin, **RELAX, lean=12, side=0, twist=6, pyaw=5, lheel=0, look=BOWLER, hips=V(hips_c)+V((0,0,0.05)))
    keys = [(0, STANCE), (6, STANCE), (16, step), (23, land), (27, mid), (30, con), (36, f1), (44, fin),
            (58, hold), (72, rel), (n-1, STANCE)]
    SHOTS[name] = (n, keys, 30, dict(footwork="back_foot", lofted=lofted, shot_direction=[round(x,2) for x in V((shot_dir[0],shot_dir[1],0)).normalized()][:2], **(meta or {})))

# ---------------- ADVANCE (down the track) template: contact at frame 40, ends standing where he got to
def ADV(name, *, lb_final, ltoe, rb_cross, hips_c, body_c, contact, follow, finish, body_f, shot_dir, lofted,
        defence=False, n=120, meta=None):
    S = contact["bg"] + contact["ba"]*(SWEET-GRIP_R)
    h0 = STANCE["hips"]
    def shifted(b, dx): return dict(b, bg=V(b["bg"])+V((dx,0,0)))
    s1 = P(STANCE, lball=(0.60,-0.03,0), ltoe=(0.6,-1,0), hips=h0+V((0.28,0,0.01)), lean=18, side=4, twist=10,
           **shifted(mid_bat(STANCE, BL_HIGH, 0.4), 0.28), look=BOWLER)
    s2 = P(s1, rball=rb_cross, rtoe=(0.3,-1,0), hips=h0+V((0.55,0.02,0.02)), lean=16, **shifted(mid_bat(STANCE, BL_HIGH, 0.7), 0.55))
    land_h = V(hips_c).lerp(h0+V((0.55,0.02,0.02)), 0.3); BLs = shifted(BL_HIGH, land_h[0]-h0[0])
    land = P(s2, lball=lb_final, ltoe=ltoe, hips=land_h,
             **{k: (STANCE[k]*0.3 + body_c[k]*0.7) for k in body_c}, **BLs, look=S+V((2,0,0.5)))
    con = P(land, hips=V(hips_c), **body_c, **contact, look=S)
    mid = P(blend(land, con, 0.5), **mid_bat(BLs, contact, 0.55))
    lf = far(S, shot_dir, lofted)
    if defence:
        f1 = P(con, look=S+V((-0.3,0,-0.1))); fin = P(con, look=S+V((-0.5,0,-0.15)))
    else:
        f1 = P(blend(con, P(con, **body_f), 0.5), **follow, look=S.lerp(lf, 0.3), rheel=15)
        fin = P(con, **body_f, **finish, look=lf, rheel=30)
    hold = P(fin, hips=fin["hips"]+V((0,0,0.01)))
    stand_x = (V(lb_final)[0] + V(rb_cross)[0]) / 2
    endp = P(STANCE, lball=(stand_x+0.18, -0.03, 0), rball=(stand_x-0.18, -0.02, 0), ltoe=(0.5,-1,0), rtoe=(0.2,-1,0),
             hips=V((stand_x, 0.04, -0.04)), lean=8, side=0, twist=12, pyaw=8,
             **bat_g((stand_x+0.15,-0.26,0.86), (0.15,-0.2,-1), (1,0,0)), look=BOWLER)
    rel = P(blend(hold, endp, 0.5), **RELAX and bat_g((stand_x+0.2,-0.28,0.9),(0.3,-0.2,-1),(1,0,0)), look=BOWLER)
    keys = [(0, STANCE), (6, STANCE), (14, s1), (22, s2), (30, land), (35, mid), (40, con), (46, f1), (54, fin),
            (70, hold), (92, rel), (n-1, endp)]
    SHOTS[name] = (n, keys, 40, dict(footwork="advance", lofted=lofted, ends_at_x=round(stand_x,2),
                                    shot_direction=[round(x,2) for x in V((shot_dir[0],shot_dir[1],0)).normalized()][:2], **(meta or {})))

# ================================================================= DRIVES (front foot) ground + lofted
DRIVE_BODY_C = dict(lean=24, side=30, twist=12, pyaw=10)
DRIVE_BODY_F = dict(lean=10, side=10, twist=26, pyaw=24)
LOFT_BODY_F = dict(lean=4, side=4, twist=34, pyaw=28)
def drive_pair(base, stride, ltoe, S, d, hips_c=(0.42,0.02,-0.29), body_c=DRIVE_BODY_C):
    dg = unit(d[0], d[1], -0.12); dl = unit(d[0], d[1], 0.35)
    # ground: hands ahead of the ball, face slightly closed down, checked high finish
    FF(base+"_ground", stride=stride, ltoe=ltoe, hips_c=hips_c, body_c=body_c,
       contact=bat_s(S, (-0.22, -0.05*d[1], -1), dg),
       follow=bat_g(V(S)+V((0.25,0.02,0.65)), (0.75,0.1*d[1],-0.35), unit(d[0]*0.5,d[1]*0.5,0.85)),
       finish=bat_g(V(S)+V((-0.15,0.05,1.15)), (-0.05,0.35,1.0), unit(d[0],d[1],-0.3)),
       body_f=DRIVE_BODY_F, shot_dir=d, lofted=False)
    # lofted: contact a touch further forward, face open upward, full high finish over the left shoulder
    FF(base+"_lofted", stride=stride, ltoe=ltoe, hips_c=hips_c, body_c=dict(body_c, lean=body_c["lean"]-4),
       contact=bat_s(V(S)+V((0.06,0,0.02)), (0.02, -0.03*d[1], -1), dl),
       follow=bat_g(V(S)+V((0.30,0.02,0.85)), (0.75,0.1*d[1],0.1), unit(d[0]*0.3,d[1]*0.3,0.95)),
       finish=bat_g(V(S)+V((-0.40,0.15,1.40)), (-0.55,0.55,-0.6), unit(0.3,0.2,0.9)),
       body_f=LOFT_BODY_F, shot_dir=d, lofted=True, rheel_f=45)

drive_pair("ff_straight_drive", (0.74,-0.06,0), (0.55,-1,0), (0.66,-0.24,0.20), (1,0))
drive_pair("ff_off_drive",      (0.72,-0.12,0), (0.5,-1,0),  (0.64,-0.32,0.20), (1,-0.4))
drive_pair("ff_cover_drive",    (0.66,-0.30,0), (0.35,-1,0), (0.56,-0.48,0.22), (0.55,-1),
           hips_c=(0.36,-0.06,-0.28), body_c=dict(lean=32, side=24, twist=4, pyaw=4))
drive_pair("ff_on_drive",       (0.70,0.04,0), (0.7,-1,0),   (0.64,-0.14,0.20), (1,0.45),
           hips_c=(0.42,0.05,-0.29), body_c=dict(lean=20, side=30, twist=18, pyaw=16))

# ---------------- flick (clip off the pads) through midwicket: ground + lofted
FF("ff_flick_ground", stride=(0.66,0.0,0), ltoe=(0.6,-1,0), hips_c=(0.40,0.05,-0.27), body_c=dict(lean=18, side=28, twist=18, pyaw=14),
   contact=bat_s((0.60,-0.12,0.32), (-0.25,0.1,-1), unit(0.55,1,-0.1)),
   follow=bat_g((0.80,0.05,0.75), (0.2,0.9,-0.35), unit(0,0.4,-1)),
   finish=bat_g((0.55,0.15,0.95), (-0.3,0.9,-0.2), unit(0,0,-1)),
   body_f=dict(lean=10, side=8, twist=30, pyaw=26), shot_dir=(0.6,1), lofted=False)
FF("ff_flick_lofted", stride=(0.66,0.0,0), ltoe=(0.6,-1,0), hips_c=(0.40,0.05,-0.27), body_c=dict(lean=16, side=26, twist=18, pyaw=14),
   contact=bat_s((0.62,-0.12,0.34), (-0.05,0.1,-1), unit(0.55,1,0.35)),
   follow=bat_g((0.75,0.05,0.95), (0.2,0.9,0.2), unit(0.2,0.3,0.9)),
   finish=bat_g((0.35,0.10,1.45), (-0.5,0.6,-0.6), unit(0.3,0.2,0.9)),
   body_f=dict(lean=4, side=4, twist=40, pyaw=32), shot_dir=(0.6,1), lofted=True, rheel_f=45)

# ---------------- leg glance (front foot, fine leg) - ground
FF("ff_leg_glance", stride=(0.62,0.02,0), ltoe=(0.6,-1,0), hips_c=(0.36,0.05,-0.25), body_c=dict(lean=20, side=26, twist=12, pyaw=10),
   backlift=BL_LOW, contact=bat_s((0.56,-0.06,0.30), (-0.35,0.15,-0.92), unit(-0.4,1,-0.1)),
   follow=bat_g((0.55,-0.02,0.75), (-0.4,0.35,-0.85), unit(-0.6,1,-0.2)),
   finish=bat_g((0.50,0.0,0.80), (-0.45,0.4,-0.8), unit(-0.7,1,-0.2)),
   body_f=dict(lean=16, side=10, twist=14, pyaw=12), shot_dir=(-0.8,1), lofted=False, rheel_f=10)

# ---------------- forward defence + front-foot leave
FF("ff_forward_defence", stride=(0.72,-0.08,0), ltoe=(0.55,-1,0), hips_c=(0.42,0.03,-0.30), body_c=dict(lean=26, side=32, twist=12, pyaw=8),
   backlift=BL_LOW, contact=bat_s((0.60,-0.22,0.28), (-0.36,-0.02,-0.93), unit(1,0,-0.25)),
   follow=None, finish=None, body_f={}, shot_dir=(1,0), lofted=False, defence=True,
   meta=dict(note="dead-bat block, ball dropped at feet"))
FF("ff_leave", stride=(0.58,-0.06,0), ltoe=(0.55,-1,0), hips_c=(0.22,0.03,-0.18), body_c=dict(lean=16, side=10, twist=0, pyaw=4),
   backlift=BL_HIGH, contact=bat_g((0.08,0.02,1.55), (-0.4,0.35,0.85), unit(0,1,-0.2)),
   follow=None, finish=None, body_f={}, shot_dir=(-1,-0.1), lofted=False, defence=True,
   meta=dict(note="shoulder arms; 'contact_frame' = moment ball passes the bat"))

# ================================================================= SWEEP FAMILY (front foot, back knee down)
KNEEL = dict(rball=(-0.42,0.10,0), rtoe=(0.2,-1,0), rheel=75, rkp=(0.4,-0.2,-1))
SWEEP_BODY = dict(lean=34, side=22, twist=8, pyaw=10)
def sweep_shot(name, *, contact, pre, follow, finish, body_f, shot_dir, lofted, stride=(0.82,-0.12,0), hips_c=(0.18,0.06,-0.47), meta=None):
    FF(name, stride=stride, ltoe=(0.45,-1,0), hips_c=hips_c, body_c=SWEEP_BODY, contact=contact, follow=follow, finish=finish,
       body_f=body_f, shot_dir=shot_dir, lofted=lofted, backlift=pre, back=KNEEL, rheel_f=75, meta=meta)
sweep_shot("ff_sweep_ground",
   pre=bat_g((0.20,-0.38,0.78), (0.35,-0.75,0.55), unit(1,0.2,0)),
   contact=bat_s((0.86,-0.26,0.16), (0.82,0.05,-0.5), unit(0.1,1,0.05)),
   follow=bat_g((0.40,-0.05,0.55), (0.15,0.95,-0.15), unit(-0.2,0.1,-1)),
   finish=bat_g((0.35,0.10,0.60), (-0.35,0.9,0.05), unit(0,0,-1)),
   body_f=dict(lean=30, side=16, twist=20, pyaw=14), shot_dir=(0,1), lofted=False)
sweep_shot("ff_slog_sweep_lofted",
   pre=bat_g((0.15,-0.35,0.95), (0.2,-0.6,0.8), unit(1,0.2,0)),
   contact=bat_s((0.86,-0.24,0.20), (0.82,0.05,-0.42), unit(0.35,1,0.45)),
   follow=bat_g((0.45,0.0,0.75), (0.15,0.9,0.35), unit(0,0.3,1)),
   finish=bat_g((0.20,0.05,1.10), (-0.5,0.4,-0.75), unit(0.3,0.3,0.9)),
   body_f=dict(lean=24, side=10, twist=30, pyaw=20), shot_dir=(0.6,1), lofted=True)
sweep_shot("ff_paddle_sweep",
   pre=bat_g((0.25,-0.28,0.72), (0.6,-0.5,-0.4), unit(1,0.2,0)),
   contact=bat_s((0.72,-0.10,0.20), (0.6,-0.35,-0.6), unit(-0.6,1,0.1)),
   follow=bat_g((0.30,-0.18,0.60), (0.6,-0.1,-0.7), unit(-0.7,1,0.1)),
   finish=bat_g((0.30,-0.16,0.62), (0.6,-0.05,-0.75), unit(-0.7,1,0.1)),
   body_f=dict(lean=32, side=20, twist=10, pyaw=10), shot_dir=(-0.8,1), lofted=False)
sweep_shot("ff_reverse_sweep_ground",
   pre=bat_g((0.25,0.0,0.82), (0.2,0.75,0.6), unit(1,-0.2,0)),
   contact=bat_s((0.86,-0.18,0.16), (0.82,-0.05,-0.5), unit(-0.2,-1,0.05)),
   follow=bat_g((0.40,-0.32,0.58), (0.1,-0.95,-0.1), unit(-0.2,0,-1)),
   finish=bat_g((0.32,-0.32,0.62), (-0.3,-0.9,0.1), unit(0,0,-1)),
   body_f=dict(lean=32, side=18, twist=0, pyaw=6), shot_dir=(-0.3,-1), lofted=False)
sweep_shot("ff_reverse_sweep_lofted",
   pre=bat_g((0.20,0.0,0.95), (0.1,0.6,0.8), unit(1,-0.2,0)),
   contact=bat_s((0.86,-0.18,0.22), (0.82,-0.05,-0.42), unit(-0.1,-1,0.45)),
   follow=bat_g((0.40,-0.35,0.80), (0.1,-0.9,0.4), unit(0,-0.3,1)),
   finish=bat_g((0.15,-0.30,1.10), (-0.5,-0.4,0.75), unit(0.2,-0.3,0.9)),
   body_f=dict(lean=26, side=12, twist=-6, pyaw=2), shot_dir=(0,-1), lofted=True)

# ---------------- Dilscoop: down on the back knee, chest to the bowler, bat flat in front of the face, scooped over keeper
FF("ff_dilscoop", stride=(0.60,-0.02,0), ltoe=(0.9,-0.4,0), hips_c=(0.12,0.04,-0.45),
   body_c=dict(lean=6, side=-4, twist=55, pyaw=40), back=dict(KNEEL, rball=(-0.35,0.12,0), rtoe=(0.6,-1,0)),
   backlift=bat_g((0.10,-0.18,0.92), (0.6,-0.7,0.3), unit(0,0,1)),
   contact=bat_s((0.72,-0.16,0.98), (0.9,-0.25,0.08), unit(-0.35,0,1)),
   follow=bat_g((0.26,-0.08,1.10), (0.85,-0.2,0.35), unit(-0.7,0,0.7)),
   finish=bat_g((0.22,-0.06,1.14), (0.8,-0.2,0.45), unit(-0.75,0,0.6)),
   body_f=dict(lean=2, side=-8, twist=55, pyaw=40), shot_dir=(-1,0), lofted=True, rheel_f=75,
   meta=dict(note="ramp over the keeper's head"))

# ================================================================= BACK FOOT
BF_FEET = dict(rb=(-0.42,-0.16,0), rtoe=(0.15,-1,0), lb=(-0.02,-0.12,0), ltoe=(0.5,-1,0))
BF_HIPS = (-0.20,-0.02,-0.08)
BF("bf_defence", **BF_FEET, hips_c=BF_HIPS, body_c=dict(lean=14, side=4, twist=0, pyaw=0),
   backlift=BL_LOW, contact=bat_s((0.02,-0.36,0.72), (-0.38,0,-0.92), unit(1,0,-0.2)),
   follow=None, finish=None, body_f={}, shot_dir=(1,0), lofted=False, defence=True,
   meta=dict(note="high elbow, ball dropped under the eyes"))
BF("bf_leave", **BF_FEET, hips_c=BF_HIPS, body_c=dict(lean=8, side=0, twist=-4, pyaw=-2),
   contact=bat_g((-0.12,0.04,1.55), (-0.45,0.35,0.82), unit(0,1,-0.2)),
   follow=None, finish=None, body_f={}, shot_dir=(-1,-0.1), lofted=False, defence=True,
   meta=dict(note="bat up, lets the ball go through to the keeper"))
BF("bf_duck_bouncer", rb=(-0.30,-0.05,0), rtoe=(0.1,-1,0), lb=(0.05,-0.05,0), ltoe=(0.4,-1,0),
   hips_c=(-0.12,0.10,-0.42), body_c=dict(lean=50, side=0, twist=0, pyaw=0),
   backlift=BL_LOW, contact=bat_g((-0.10,-0.40,0.58), (-0.55,-0.55,-0.6), unit(1,0,0)),
   follow=None, finish=None, body_f={}, shot_dir=(-1,0), lofted=False, defence=True,
   meta=dict(note="sways/ducks under a bouncer; 'contact_frame' = ball passes overhead"))
BF("bf_punch_ground", **BF_FEET, hips_c=BF_HIPS, body_c=dict(lean=12, side=6, twist=-4, pyaw=0),
   contact=bat_s((0.10,-0.46,0.70), (-0.3,-0.1,-0.95), unit(0.8,-1,-0.15)),
   follow=bat_g((0.30,-0.40,1.05), (0.6,-0.35,-0.5), unit(0.4,-0.4,0.8)),
   finish=bat_g((0.12,-0.30,1.38), (-0.05,0.25,1), unit(0.8,-1,-0.3)),
   body_f=dict(lean=8, side=4, twist=12, pyaw=10), shot_dir=(0.6,-1), lofted=False)
BF("bf_square_cut", rb=(-0.28,-0.38,0), rtoe=(0.35,-1,0), lb=(0.05,-0.18,0), ltoe=(0.5,-1,0),
   hips_c=(-0.10,-0.12,-0.12), body_c=dict(lean=22, side=2, twist=-12, pyaw=-8),
   backlift=bat_g((-0.10,-0.16,1.38), (-0.25,-0.45,0.86), unit(0,-1,-0.2)),
   contact=bat_s((0.20,-0.80,0.72), (0.7,-0.6,-0.25), unit(-0.2,-0.9,-0.4)),
   follow=bat_g((0.05,-0.40,0.78), (0.2,0.15,-1), unit(0,-1,-0.1)),
   finish=bat_g((0.10,-0.20,0.95), (-0.2,0.6,-0.75), unit(0,0,-1)),
   body_f=dict(lean=24, side=0, twist=-4, pyaw=-4), shot_dir=(0,-1), lofted=False)
BF("bf_upper_cut_lofted", rb=(-0.30,-0.30,0), rtoe=(0.35,-1,0), lb=(0.0,-0.14,0), ltoe=(0.5,-1,0),
   hips_c=(-0.14,-0.08,-0.10), body_c=dict(lean=4, side=-6, twist=-10, pyaw=-8),
   backlift=bat_g((-0.10,-0.16,1.30), (-0.4,-0.5,0.75), unit(0,-1,-0.2)),
   contact=bat_s((0.0,-0.82,1.35), (0.65,-0.6,0.25), unit(-0.45,-0.5,0.75)),
   follow=bat_g((-0.10,-0.40,1.55), (0.0,-0.6,0.8), unit(-0.6,0,0.8)),
   finish=bat_g((-0.05,-0.15,1.62), (-0.3,0.3,0.9), unit(-0.7,-0.3,0.5)),
   body_f=dict(lean=-2, side=-8, twist=-2, pyaw=-4), shot_dir=(-0.6,-1), lofted=True)
BF("bf_late_cut", rb=(-0.32,-0.34,0), rtoe=(0.2,-1,0), lb=(0.02,-0.18,0), ltoe=(0.5,-1,0),
   hips_c=(-0.14,-0.10,-0.12), body_c=dict(lean=26, side=-2, twist=-16, pyaw=-10),
   backlift=bat_g((-0.12,-0.16,1.30), (-0.3,-0.4,0.86), unit(0,-1,-0.2)),
   contact=bat_s((-0.38,-0.62,0.62), (-0.25,-0.35,-0.9), unit(-1,-0.45,-0.1)),
   follow=bat_g((-0.25,-0.42,0.95), (-0.35,-0.25,-0.9), unit(-1,-0.3,-0.2)),
   finish=bat_g((-0.22,-0.40,0.98), (-0.35,-0.2,-0.9), unit(-1,-0.3,-0.2)),
   body_f=dict(lean=28, side=-2, twist=-16, pyaw=-10), shot_dir=(-0.8,-1), lofted=False)
PULL_FEET = dict(rb=(-0.30,-0.30,0), rtoe=(0.6,-1,0), lb=(0.10,0.12,0), ltoe=(1,-0.5,0))
BF("bf_pull_ground", **PULL_FEET, hips_c=(-0.10,-0.05,-0.10), body_c=dict(lean=10, side=4, twist=55, pyaw=40),
   backlift=bat_g((-0.10,-0.12,1.36), (-0.3,-0.5,0.8), unit(0.3,-1,0)),
   contact=bat_s((0.40,-0.42,1.00), (0.35,-1,-0.05), unit(1,0.6,-0.15)),
   follow=bat_g((0.20,0.15,0.98), (-0.2,1,-0.3), unit(0.2,0,-1)),
   finish=bat_g((0.05,0.25,0.95), (-0.5,0.8,-0.35), unit(0,0,-1)),
   body_f=dict(lean=12, side=0, twist=75, pyaw=55), shot_dir=(0.4,1), lofted=False)
BF("bf_pull_lofted", **PULL_FEET, hips_c=(-0.10,-0.05,-0.10), body_c=dict(lean=8, side=2, twist=55, pyaw=40),
   backlift=bat_g((-0.10,-0.12,1.40), (-0.3,-0.5,0.8), unit(0.3,-1,0)),
   contact=bat_s((0.40,-0.42,1.05), (0.35,-1,0.05), unit(1,0.6,0.35)),
   follow=bat_g((0.25,0.10,1.20), (-0.1,1,0.25), unit(0.2,0,1)),
   finish=bat_g((0.10,0.08,1.42), (-0.55,0.5,-0.65), unit(0.3,0.3,0.9)),
   body_f=dict(lean=2, side=-2, twist=80, pyaw=58), shot_dir=(0.3,1), lofted=True)
BF("bf_hook_lofted", rb=(-0.35,-0.20,0), rtoe=(0.6,-1,0), lb=(0.05,0.15,0), ltoe=(1,-0.4,0),
   hips_c=(-0.18,0.0,-0.08), body_c=dict(lean=-4, side=-6, twist=60, pyaw=45),
   backlift=bat_g((-0.10,-0.12,1.45), (-0.3,-0.5,0.8), unit(0.3,-1,0)),
   contact=bat_s((0.25,-0.40,1.52), (0.3,-1,0.2), unit(0.3,1,0.5)),
   follow=bat_g((0.05,0.10,1.50), (-0.35,1,0.1), unit(-0.2,0,1)),
   finish=bat_g((-0.05,0.10,1.50), (-0.6,0.45,-0.65), unit(-0.2,0.3,0.9)),
   body_f=dict(lean=-6, side=-10, twist=85, pyaw=60), shot_dir=(-0.4,1), lofted=True)

# ================================================================= ADVANCE (down the track)
ADV_BASE = dict(lb_final=(1.20,-0.06,0), ltoe=(0.6,-1,0), rb_cross=(0.66,0.02,0), hips_c=(0.92,0.03,-0.28))
ADV("adv_drive_ground", **ADV_BASE, body_c=DRIVE_BODY_C,
    contact=bat_s((1.14,-0.24,0.22), (-0.22,0,-1), unit(1,0,-0.12)),
    follow=bat_g((1.47,-0.22,0.85), (0.75,0,-0.35), unit(0.5,0,0.85)),
    finish=bat_g((1.07,-0.19,1.33), (-0.05,0.35,1.0), unit(1,0,-0.3)),
    body_f=DRIVE_BODY_F, shot_dir=(1,0.1), lofted=False)
ADV("adv_lofted_straight", **ADV_BASE, body_c=dict(DRIVE_BODY_C, lean=20),
    contact=bat_s((1.18,-0.24,0.28), (0.05,0,-1), unit(1,0,0.4)),
    follow=bat_g((1.52,-0.22,1.05), (0.75,0,0.1), unit(0.3,0,0.95)),
    finish=bat_g((0.88,-0.09,1.62), (-0.55,0.55,-0.6), unit(0.3,0.2,0.9)),
    body_f=LOFT_BODY_F, shot_dir=(1,0.15), lofted=True)
ADV("adv_inside_out_over_cover", lb_final=(1.12,0.16,0), ltoe=(0.5,-1,0), rb_cross=(0.66,0.22,0), hips_c=(0.86,0.16,-0.24),
    body_c=dict(lean=26, side=22, twist=4, pyaw=2),
    contact=bat_s((1.08,-0.30,0.46), (0.0,-0.25,-1), unit(0.65,-1,0.45)),
    follow=bat_g((1.30,-0.42,1.05), (0.55,-0.6,0.2), unit(0.2,-0.3,0.95)),
    finish=bat_g((0.80,-0.10,1.58), (-0.4,0.55,-0.7), unit(0.3,0,0.9)),
    body_f=dict(lean=6, side=2, twist=20, pyaw=16), shot_dir=(0.65,-1), lofted=True)
ADV("adv_slog_midwicket", lb_final=(1.14,-0.12,0), ltoe=(0.8,-0.8,0), rb_cross=(0.68,-0.02,0), hips_c=(0.88,0.0,-0.24),
    body_c=dict(lean=18, side=18, twist=36, pyaw=26),
    contact=bat_s((1.10,-0.30,0.48), (0.5,-0.85,-0.2), unit(0.6,1,0.45)),
    follow=bat_g((1.00,0.10,0.95), (-0.1,1,0.3), unit(0,0.2,1)),
    finish=bat_g((0.80,0.05,1.40), (-0.55,0.5,-0.65), unit(0.3,0.3,0.9)),
    body_f=dict(lean=2, side=0, twist=70, pyaw=50), shot_dir=(0.6,1), lofted=True)
ADV("adv_defence", **ADV_BASE, body_c=dict(lean=26, side=30, twist=12, pyaw=8),
    contact=bat_s((1.10,-0.22,0.30), (-0.36,0,-0.93), unit(1,0,-0.25)),
    follow=None, finish=None, body_f={}, shot_dir=(1,0), lofted=False, defence=True)

# ================================================================= STANCE IDLE LOOP (taps the bat, small weight shifts)
tap_up = P(STANCE, **bat_g((-0.02,-0.20,0.84), (-0.10,-0.16,-1), (1,0,0)))
SHOTS["stance_idle_loop"] = (120, [
    (0, STANCE), (20, P(STANCE, hips=STANCE["hips"]+V((0.015,0,0.005)))), (30, tap_up), (36, STANCE), (44, tap_up), (50, STANCE),
    (78, P(STANCE, hips=STANCE["hips"]+V((-0.01,0,0)), lean=27)), (92, tap_up), (98, STANCE), (119, STANCE)], None,
    dict(footwork="stance", loop=True))

if __name__ == "__main__":
    names = sys.argv[1:] or list(SHOTS)
    data, metas = {}, []
    for nm in names:
        n, keys, contact, meta = SHOTS[nm]
        frames, worst, md = make_bat(nm, n, keys, contact, meta)
        data[nm] = frames; metas.append(md)
        bad = {k: round(v,3) for k, v in worst.items() if v > 0.01}
        print(nm, "OK" if not bad else bad)
    pickle.dump(data, open(_os.path.join(APL_WORK, "bat_frames.pkl"),"wb"))
    json.dump(metas, open(_os.path.join(APL_WORK, "bat_meta.json"),"w"), indent=1)
