"""
The generator's Mixamo skeleton in its rest pose, no animation, as an FBX —
the one avatar every cricket clip is retargeted through (a clip's own avatar
is built from its first pose: knees ~39 deg, head ~28 deg, which Unity then
counts as "straight").

The rest pose still leans the spine ~4 deg and the neck/head ~10 deg forward
and bends the knees ~4 deg, while the game bodies' zero is dead straight.
Unity takes the avatar's zero from the FBX rest pose, so posing here doesn't
change it; the straightening is done in Unity instead, on the avatar's
skeleton description (Editor/Pipeline.StraightenRig).

    motion.ps1 gen export_tpose_rig.py [out.fbx]
"""
import os
import sys

import bpy
from mathutils import Matrix

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lib"))
import mixamo_motions as mm  # noqa: E402

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join("D:" + os.sep, "auction-cricket", "client-assets", "cricket_rig", "mixamo_tpose.fbx")
os.makedirs(os.path.dirname(out), exist_ok=True)
arm, _ = mm.load_rig()
for p in arm.pose.bones:
    p.matrix_basis = Matrix.Identity(4)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.export_scene.fbx(filepath=out, object_types={"ARMATURE"},
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True, use_armature_deform_only=False,
    bake_anim=False)
print("T-pose rig ->", out)
