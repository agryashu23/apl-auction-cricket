"""
Dump a Rokoko (Mixamo-skeleton) mocap FBX to raw per-frame data for drivers/batting_mocap.py:
joint world positions (m, Z up, as imported) and, for the bones whose orientation we use, the world rotation
DELTA from the bone's own rest pose (quaternion w,x,y,z).  Frame 1 (the Rokoko T-pose) is dropped.

    motion.ps1 gen mocap_extract.py <in.fbx> <out.json>
"""
import json
import sys

import bpy

args = sys.argv[1:]
src, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
scn = bpy.context.scene
arm = [o for o in scn.objects if o.type == "ARMATURE"][0]
act = arm.animation_data.action
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
PFX = "mixamorig:"
POS = ["Hips", "Spine", "Spine2", "Neck", "Head", "HeadTop_End", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm",
       "LeftForeArm", "RightForeArm", "LeftHand", "RightHand", "LeftHandMiddle1", "RightHandMiddle1", "LeftHandIndex1",
       "RightHandIndex1", "LeftHandPinky1", "RightHandPinky1", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg",
       "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase"]
ROT = ["Hips", "Spine2", "Neck", "Head", "LeftHand", "RightHand", "LeftForeArm", "RightForeArm"]
MW = arm.matrix_world
rest = {}
for nm in ROT:
    b = arm.data.bones[PFX + nm]
    rest[nm] = (MW @ b.matrix_local).to_3x3().normalized()
frames = []
for f in range(f0 + 1, f1 + 1):                     # drop the T-pose frame
    scn.frame_set(f)
    fr = {"p": {}, "q": {}}
    for nm in POS:
        pb = arm.pose.bones.get(PFX + nm)
        if pb is None:
            continue
        fr["p"][nm] = [round(c, 5) for c in (MW @ pb.head)]
    for nm in ROT:
        pb = arm.pose.bones[PFX + nm]
        Mw = (MW @ pb.matrix).to_3x3().normalized()
        q = (Mw @ rest[nm].inverted()).to_quaternion()
        fr["q"][nm] = [round(c, 6) for c in q]
    frames.append(fr)
json.dump(dict(file=src, fps=scn.render.fps, first=f0 + 1, frames=frames), open(out, "w"))
print(f"extracted {len(frames)} frames from {src} -> {out}")
