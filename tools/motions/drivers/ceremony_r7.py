"""
Round-7 toss-party idles (fielding conventions: origin at his feet, facing -Y, x = his left). Heads are level: every look
target is at eye height (1.68 m) on the person he is listening to, and the chest is up (no forward lean).

  captain_idle_relaxed_loop   4 s: weight on the left leg (right knee soft, right heel light), left hand on the hip, right arm
                              loose by the side, chest up; one shift of the weight toward centre and back, a glance round
  captain_listen_nod_loop     4 s: hands clasped in front, eyes level on the presenter (front-left), two small nods
  presenter_listen_loop       4 s: the microphone held at his chest, eyes level on the captain in front of him, nodding along

    $env:APL_CEREMONY_DIR='ceremony_r7'; motion.ps1 gen drivers\\ceremony_r7.py [names...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ceremony_field import export, CLASP, MIC, STAND, EYE_Z
import ceremony_common
from fielding import V, P, H, fing, RELAXF, HOLDF

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco
N = 120

@clip("captain_idle_relaxed_loop")
def idle_relaxed():
    def pose(w=1.0, look=V((0.0, -6.0, EYE_Z)), twist=0.0):
        """w = weight on the left leg (1 = all, 0 = centre)"""
        return P(STAND, hips=H(0.05*w, 0.0, 1.03 - 0.008*w), pside=-4*w, side=1.5*w, lean=1.5, twist=twist,
                 rball=V((-0.17, -0.10*w - 0.04, 0)), rheel=12*w, rtoe=V((-0.25, -1, 0)), rkp=V((-0.1, -1, 0.3)),
                 lball=V((0.15, -0.04, 0)), ltoe=V((0.15, -1, 0)),
                 lt=V((0.24 + 0.05*w, -0.02, 1.02)), lp=V((1, 0.6, -0.2)), lh=V((-0.4, -0.2, -1)), lhw=0.6, **fing("l", RELAXF),
                 rt=V((-0.24 + 0.05*w, 0.02, 0.88)), rp=V((-0.6, 0.6, -0.6)), **fing("r", RELAXF), look=look)
    k = [(0, pose()), (30, pose(1.0, V((1.5, -6, EYE_Z)), 4)), (52, pose(0.35)), (68, pose(0.35, V((-1.2, -6, EYE_Z)), -4)), (90, pose(0.9)), (N - 1, pose())]
    export("captain_idle_relaxed_loop", N, k, dict(type="toss idle", note="relaxed standing idle for the toss party: weight on the left leg, left hand on the hip, "
           "right arm loose, chest up, head level; one weight shift toward centre and back, a glance either side"), loop=True)

@clip("captain_listen_nod_loop")
def listen_nod():
    pres = V((0.5, -1.6, EYE_Z))
    base = P(STAND, **CLASP, lean=1, look=pres)
    nod = P(base, look=pres + V((0, 0, -0.16)))
    k = [(0, base), (24, base), (31, nod), (38, base), (62, P(base, hips=H(0.02, 0, 1.025), pside=-1.5)), (80, base), (87, nod), (94, base), (N - 1, base)]
    export("captain_listen_nod_loop", N, k, dict(type="toss idle", look_at_m=dict(x=pres.x, y=pres.y, up=pres.z), nod_frames=[32, 88],
           note="listening to the presenter (front-left, at eye height): hands clasped in front, chest up, two small nods, a slight weight shift"), loop=True)

@clip("presenter_listen_loop")
def presenter_listen():
    cap = V((-0.4, -1.5, EYE_Z))
    base = P(STAND, rt=V((-0.08, -0.22, 1.24)), **MIC, lt=V((0.22, 0.0, 0.90)), lp=V((0.6, 0.6, -0.6)), **fing("l", RELAXF), lean=1, look=cap)
    nod = P(base, look=cap + V((0, 0, -0.16)))
    k = [(0, base), (20, base), (27, nod), (34, base), (56, P(base, look=cap + V((0.3, 0, 0)))), (70, base), (78, nod), (85, base), (N - 1, base)]
    export("presenter_listen_loop", N, k, dict(type="presenter", prop="microphone on RightHand, held at the chest", look_at_m=dict(x=cap.x, y=cap.y, up=cap.z),
           nod_frames=[28, 79], note="the presenter listening to a captain in front of him: microphone at his chest, eyes level on the captain, nodding along"), loop=True)

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
