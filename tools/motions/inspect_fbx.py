"""Inspect an FBX in Blender: armatures, bone names, actions, frame range.

blender -b --python inspect_fbx.py -- <file.fbx>
"""
import sys
import bpy

fbx = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
sc = bpy.context.scene
print("FILE", fbx)
print("FPS", sc.render.fps, "frames", sc.frame_start, sc.frame_end)
for ob in bpy.data.objects:
    print("OBJ", ob.type, ob.name, "dims", tuple(round(v, 2) for v in ob.dimensions))
    if ob.type == "ARMATURE":
        names = [b.name for b in ob.data.bones]
        print("BONES", len(names), names[:70])
for a in bpy.data.actions:
    print("ACTION", a.name, "range", tuple(a.frame_range), "fcurves", len(a.fcurves))
