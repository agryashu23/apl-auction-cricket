"""
Blender: dump_pose.py for MANY clips in one Blender session (the bat-grip check needs every batting,
ceremony and running clip; one Blender start per clip is minutes). Writes <out_dir>/<clip>.pose.json in
dump_pose.py's format (bone head/tail per frame, world metres, Z up), only the bones a body/hand check needs.

    blender -b --python tools/motions/dump_many.py -- <out_dir> <clip.fbx> [<clip.fbx> ...]
    (or a single @list.txt argument: one FBX path per line)
"""
import json
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
out_dir = args[0]
paths = args[1:]
if len(paths) == 1 and paths[0].startswith("@"):
    paths = [ln.strip() for ln in open(paths[0][1:]) if ln.strip()]
os.makedirs(out_dir, exist_ok=True)
KEEP = ("Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "HeadTop_End",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "RightShoulder", "RightArm", "RightForeArm", "RightHand",
        "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase")
FING = ("Thumb1", "Index1", "Middle1", "Ring1", "Pinky1", "Thumb3", "Index3", "Middle3", "Pinky3")


def wanted(name):
    n = name.split(":")[-1]
    if n in KEEP:
        return True
    return any(n == s + "Hand" + f for s in ("Left", "Right") for f in FING)


for fbx in paths:
    name = os.path.splitext(os.path.basename(fbx))[0]
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=fbx)
        scene = bpy.context.scene
        arm = next(o for o in scene.objects if o.type == "ARMATURE")
        action = arm.animation_data.action if arm.animation_data else None
        f0, f1 = (int(action.frame_range[0]), int(action.frame_range[1])) if action else (0, 0)
        # the rest key sits at frame -1 / 0: the clip proper starts at 1 (README: metadata counts from 1)
        f0 = max(f0, 1)
        frames = []
        for f in range(f0, f1 + 1):
            scene.frame_set(f)
            bones = {}
            for pb in arm.pose.bones:
                if not wanted(pb.name):
                    continue
                h = arm.matrix_world @ pb.head
                t = arm.matrix_world @ pb.tail
                bones[pb.name.split(":")[-1]] = [[round(h.x, 4), round(h.y, 4), round(h.z, 4)], [round(t.x, 4), round(t.y, 4), round(t.z, 4)]]
            frames.append({"frame": f, "bones": bones})
        json.dump({"file": os.path.basename(fbx), "fps": scene.render.fps, "first": f0, "last": f1, "frames": frames},
                  open(os.path.join(out_dir, name + ".pose.json"), "w"))
        print(f"dumped {name} {len(frames)}")
    except Exception as ex:  # keep going: one broken clip must not stop the batch
        print(f"FAILED {name}: {ex}")
