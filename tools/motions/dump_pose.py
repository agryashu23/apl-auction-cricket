"""
Blender: import an FBX clip and write every bone's head and tail (world
metres, Blender Z-up) per frame to JSON, for sheet.py to draw.

    blender -b --python tools/motions/dump_pose.py -- <clip.fbx> <out.json> [step]
"""
import json
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
fbx, out = args[0], args[1]
step = int(args[2]) if len(args) > 2 else 1

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
action = arm.animation_data.action if arm.animation_data else None
f0, f1 = (int(action.frame_range[0]), int(action.frame_range[1])) if action else (0, 0)
frames = []
for f in range(f0, f1 + 1, step):
    scene.frame_set(f)
    bones = {}
    for pb in arm.pose.bones:
        h = arm.matrix_world @ pb.head
        t = arm.matrix_world @ pb.tail
        bones[pb.name] = [[h.x, h.y, h.z], [t.x, t.y, t.z]]
    frames.append({"frame": f, "bones": bones})
# any mesh riding on the rig (a bat, a ball): its world-space bounding box corners
props = []
for o in scene.objects:
    if o.type != "MESH":
        continue
    scene.frame_set(f0)
    props.append({"name": o.name, "parent_bone": o.parent_bone, "corners": [list(o.matrix_world @ __import__("mathutils").Vector(c)) for c in o.bound_box]})
json.dump({"file": os.path.basename(fbx), "fps": scene.render.fps, "first": f0, "last": f1, "frames": frames, "props": props}, open(out, "w"))
print(f"dumped {len(frames)} frames ({f0}..{f1}) of {os.path.basename(fbx)} to {out}")
