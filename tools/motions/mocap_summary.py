"""Summarise a mocap FBX: frame count, and a fingerprint of the right hand's path, so
duplicate exports of the same take show up. Also writes the per-frame joint positions.

blender -b --python mocap_summary.py -- <file.fbx> <out.json>
"""
import json
import sys
import bpy

args = sys.argv[sys.argv.index("--") + 1:]
fbx, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = arm.animation_data.action if arm.animation_data else None
f0, f1 = (int(act.frame_range[0]), int(act.frame_range[1])) if act else (1, 1)
sc = bpy.context.scene
names = ["Hips", "Spine2", "Head", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand",
         "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase", "Neck"]
pb = {b.name.split(":")[-1]: b for b in arm.pose.bones}
frames = []
for f in range(f0, f1 + 1):
    sc.frame_set(f)
    frames.append({n: list(arm.matrix_world @ pb[n].head) for n in names if n in pb})
rh = [round(fr["RightHand"][k], 3) for fr in frames[::10] for k in range(3)] if frames and "RightHand" in frames[0] else []
json.dump({"file": fbx, "first": f0, "last": f1, "frames": frames}, open(out, "w"))
print("SUMMARY", fbx.split("\\")[-1], "frames", f0, f1, "rh", rh[:12])
