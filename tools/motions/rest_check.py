"""
Blender: an FBX's rest pose (armature bind) — knee, elbow and spine/neck
angles — to see whether it is the clean T-pose Unity's humanoid avatar
expects as its zero. Prints the angles for each FBX given.

    blender -b --python tools/motions/rest_check.py -- <a.fbx> [<b.fbx> ...]
"""
import math
import sys

import bpy

for fbx in sys.argv[sys.argv.index("--") + 1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    bones = arm.data.bones

    def vec(name):
        b = bones.get("mixamorig:" + name)
        return (arm.matrix_world @ b.tail_local) - (arm.matrix_world @ b.head_local) if b else None

    def ang(a, b):
        va, vb = vec(a), vec(b)
        return math.degrees(va.angle(vb)) if va is not None and vb is not None else float("nan")

    up = __import__("mathutils").Vector((0, 0, 1))
    def tilt(name):
        v = vec(name)
        return math.degrees(v.angle(up)) if v is not None else float("nan")

    print(f"REST {fbx.split(chr(92))[-1]}: knee L {ang('LeftUpLeg', 'LeftLeg'):.1f} R {ang('RightUpLeg', 'RightLeg'):.1f} | "
          f"elbow L {ang('LeftArm', 'LeftForeArm'):.1f} R {ang('RightArm', 'RightForeArm'):.1f} | "
          f"thigh from vertical L {180 - tilt('LeftUpLeg'):.1f} | spine {tilt('Spine'):.1f} spine2 {tilt('Spine2'):.1f} neck {tilt('Neck'):.1f} head {tilt('Head'):.1f} | "
          f"upper arm from vertical L {tilt('LeftArm'):.1f}")
