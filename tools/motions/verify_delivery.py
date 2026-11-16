"""
Blender (headless): verify an owner motion delivery against our rig and its sidecar claims. Data only — reads FBX
and JSON, runs nothing from the delivery.

    blender -b --python tools/motions/verify_delivery.py -- <out.json> <rig.fbx> <sidecar.json> <clip.fbx> [...] [--ref name=path.fbx ...]

Per clip it dumps: bone count / names missing against the rig, rest-pose difference (cm, armature space), fps,
frame count, per-frame world positions of the feet, toes, hips and the main joints (for verify_report.py).
"""
import json
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
out_path, rig_path = argv[0], argv[1]
clips = [a for a in argv[3:] if not a.startswith("--ref") and "=" not in a]
refs = dict(a.split("=", 1) for a in argv[3:] if "=" in a)

JOINTS = ["Hips", "Spine2", "Head", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot",
          "LeftToeBase", "RightToeBase", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand"]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    return next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")


def rest(arm):
    return {b.name: list(arm.matrix_world @ b.head_local) for b in arm.data.bones}


def frames(arm):
    act = arm.animation_data.action if arm.animation_data else None
    if act is None:
        return None, []
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    # A rest key before the clip (the packs' convention): skip it.
    keys = sorted({int(round(k.co[0])) for fc in act.fcurves for k in fc.keyframe_points})
    first = keys[1] if len(keys) > 1 and keys[0] < 0 else keys[0]
    out = []
    sc = bpy.context.scene
    for f in range(first, f1 + 1):
        sc.frame_set(f)
        pose = {}
        for j in JOINTS:
            pb = arm.pose.bones.get("mixamorig:" + j)
            if pb is not None:
                pose[j] = list(arm.matrix_world @ pb.head)
        out.append(pose)
    return {"range": [f0, f1], "first": first, "keys": len(keys)}, out


rig = load(rig_path)
rig_rest = rest(rig)
result = {"rig_bones": len(rig_rest), "clips": {}, "refs": {}}
for name, path in refs.items():
    arm = load(path)
    info, fr = frames(arm)
    result["refs"][name] = {"info": info, "frames": fr}
for path in clips:
    arm = load(path)
    r = rest(arm)
    common = set(r) & set(rig_rest)
    diff = max((sum((a - b) ** 2 for a, b in zip(r[n], rig_rest[n])) ** 0.5 for n in common), default=-1)
    info, fr = frames(arm)
    result["clips"][path.replace("\\", "/").split("/")[-1][:-4]] = {
        "bones": len(r), "missing": sorted(set(rig_rest) - set(r)), "extra": sorted(set(r) - set(rig_rest)),
        "rest_diff_cm": round(diff * 100, 2), "fps": bpy.context.scene.render.fps, "info": info, "frames": fr,
    }
    print("DONE", path)
json.dump(result, open(out_path, "w"))
print("WROTE", out_path)
