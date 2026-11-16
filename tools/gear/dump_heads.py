"""
Dumps each cast body's head mesh for tools/gear/beard_masks.py: per head-material
triangle corner, the UV and the position relative to the head bone (Unity frame:
x right, y up, z forward), plus the vertex normal. -> build/gear/heads/<model>.json

    blender -b --python tools/gear/dump_heads.py -- Male_Adult_01 Male_Adult_06 ...
"""
import json
import sys

import bpy
from mathutils import Vector

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
names = sys.argv[sys.argv.index("--") + 1:]
for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}client-assets/rocketbox/{name}/Export/{name}.fbx")
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    hb = arm.matrix_world @ arm.pose.bones["Bip01 Head"].head
    head = Vector((-hb.x, hb.z, -hb.y))
    m = max((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    me = m.data
    uvl = me.uv_layers.active.data
    mats = [i for i, mt in enumerate(me.materials) if "head" in mt.name.lower()]
    U = lambda w: [round(-w.x, 5), round(w.z, 5), round(-w.y, 5)]  # noqa: E731
    rot = m.matrix_world.to_3x3()
    tris = []
    me.calc_loop_triangles()
    for t in me.loop_triangles:
        if t.material_index not in mats:
            continue
        corners = []
        for li, vi in zip(t.loops, t.vertices):
            p = m.matrix_world @ me.vertices[vi].co
            n = (rot @ me.vertices[vi].normal).normalized()
            q = Vector(U(p)) - head
            corners.append([round(uvl[li].uv.x, 5), round(uvl[li].uv.y, 5), round(q.x, 5), round(q.y, 5), round(q.z, 5), round(-n.x, 3), round(n.z, 3), round(-n.y, 3)])
        tris.append(corners)
    json.dump({"model": name, "tris": tris}, open(f"{ROOT}build/gear/heads/{name}.json", "w"))
    print("HEAD", name, len(tris))
