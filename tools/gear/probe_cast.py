"""
Bone heads and head-skin extents for every cast body (kit_cast.json), in
Unity's frame (x right, y up, z forward; metres), to see how much the
skeletons differ and how big each head (with hair) is around the head bone.

    blender -b --python tools/gear/probe_cast.py
"""
import json
import bpy

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/client-assets/rocketbox/"
cast = json.load(open(_APL_REPO + "/client/Assets/Resources/Data/kit_cast.json"))["cast"]
BONES = ["Bip01 Head", "Bip01 Neck", "Bip01 L Calf", "Bip01 L Foot", "Bip01 L Hand", "Bip01 L Finger2", "Bip01 L Finger0"]


def U(v):
    return (-v.x, v.z, -v.y)


for e in cast:
    name = e["model"]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}{name}/Export/{name}.fbx")
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    pb = {b.name: U(arm.matrix_world @ b.head) for b in arm.pose.bones}
    head = pb["Bip01 Head"]
    line = [name] + [f"{b.split()[-1]}=" + ",".join(f"{c:.3f}" for c in pb[b]) for b in BONES if b in pb]
    # Head skin relative to the head bone (hair included): min/max per axis.
    lo, hi = [9, 9, 9], [-9, -9, -9]
    for m in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        gi = {g.index: g.name for g in m.vertex_groups}
        for v in m.data.vertices:
            if not v.groups:
                continue
            g = max(v.groups, key=lambda x: x.weight)
            if gi.get(g.group) != "Bip01 Head":
                continue
            p = U(m.matrix_world @ v.co)
            for i in range(3):
                d = p[i] - head[i]
                lo[i] = min(lo[i], d)
                hi[i] = max(hi[i], d)
    line.append("headskin lo=" + ",".join(f"{c:.3f}" for c in lo) + " hi=" + ",".join(f"{c:.3f}" for c in hi))
    print("CAST", " ".join(line))
