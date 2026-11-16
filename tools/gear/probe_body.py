"""
Measures a Rocketbox body in its stored (bind) pose, the pose CricketCast fits
the gear in: bone heads, and the extent of the skin around the head, shins,
hands. Coordinates printed in Unity's frame (x right, y up, z forward = the
body's facing), metres.

    blender -b --python tools/gear/probe_body.py -- client-assets/rocketbox/Male_Adult_01/Export/Male_Adult_01.fbx
"""
import sys
import bpy
from mathutils import Vector

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)

arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
print("armature", arm.name, "scale", tuple(arm.matrix_world.to_scale()))
print("meshes", [(m.name, len(m.data.vertices)) for m in meshes])


def U(v):
    # Blender Z-up, the body facing -Y (Rocketbox) -> Unity frame facing +z.
    return (round(v.x, 4), round(v.z, 4), round(-v.y, 4))


names = [b.name for b in arm.data.bones]
want = ["Head", "Neck", "Calf", "Foot", "Hand", "Finger0", "Finger2", "Finger21", "Thigh", "Spine2", "HeadNub", "Toe0"]
for b in arm.pose.bones:
    if any(w in b.name for w in want):
        print("bone", b.name, "head", U(arm.matrix_world @ b.head), "tail", U(arm.matrix_world @ b.tail))

# Skin near each bone: vertices whose strongest weight is that bone.
for m in meshes:
    groups = {g.index: g.name for g in m.vertex_groups}
    ext = {}
    for v in m.data.vertices:
        if not v.groups:
            continue
        g = max(v.groups, key=lambda x: x.weight)
        name = groups.get(g.group, "?")
        if not any(w in name for w in ["Head", "Calf", "Hand", "Finger", "Neck", "Foot"]):
            continue
        p = U(m.matrix_world @ v.co)
        lo, hi = ext.get(name, ((9, 9, 9), (-9, -9, -9)))
        ext[name] = (tuple(min(a, b) for a, b in zip(lo, p)), tuple(max(a, b) for a, b in zip(hi, p)))
    for k, (lo, hi) in sorted(ext.items()):
        print("skin", m.name, k, "min", tuple(round(x, 3) for x in lo), "max", tuple(round(x, 3) for x in hi))
