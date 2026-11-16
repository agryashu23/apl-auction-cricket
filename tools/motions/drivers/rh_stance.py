"""
The right-handed stance idle loop (the pack author defined it in shots.py but
only exported the left-handed mirror): 120 frames, bat tapped behind the back
foot, weight shifting; first and last frames are the stance every shot starts
from. Delivered as batting/00_stance_idle_loop.fbx.

    tools\\motions\\motion.ps1 gen drivers\\rh_stance.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib"))
import batting  # noqa: E402
import shots  # noqa: E402

batting.OUT = os.path.join(batting.APL_OUT, "batting")
os.makedirs(batting.OUT, exist_ok=True)
n, keys, contact, meta = shots.SHOTS["stance_idle_loop"]
frames, worst, md = batting.make_bat("00_stance_idle_loop", n, keys, contact, meta)
entry = dict(id=0, shot="Stance idle (loop)", file="00_stance_idle_loop.fbx", frames=n, loop=True)
json.dump([entry], open(os.path.join(batting.OUT, "00_stance_idle_loop.json"), "w"), indent=1)
grip = {k: round(v, 3) for k, v in worst.items() if k.endswith("Grip") and v > 0.012}
print("00_stance_idle_loop", "grip OK" if not grip else f"grip slips {grip}", "->", batting.OUT)
