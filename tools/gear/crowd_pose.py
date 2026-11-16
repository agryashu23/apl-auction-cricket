"""
Poses a Rocketbox armature for the crowd sprites (crowd_sprites.py) by aiming
bones: each limb segment is turned so it points along a direction given in
Unity's body frame (x = the body's right, y up, z forward), parents first.

    POSES[name] = {"L_upper": dir, "L_fore": dir, ... , "sit": bool}
Right-side directions are mirrored from the left ones unless given.
"""
import bpy
from mathutils import Vector

SEGMENTS = {
    "L_upper": ("Bip01 L UpperArm", "Bip01 L Forearm"), "L_fore": ("Bip01 L Forearm", "Bip01 L Hand"),
    "R_upper": ("Bip01 R UpperArm", "Bip01 R Forearm"), "R_fore": ("Bip01 R Forearm", "Bip01 R Hand"),
    "L_thigh": ("Bip01 L Thigh", "Bip01 L Calf"), "L_calf": ("Bip01 L Calf", "Bip01 L Foot"),
    "R_thigh": ("Bip01 R Thigh", "Bip01 R Calf"), "R_calf": ("Bip01 R Calf", "Bip01 R Foot"),
}
ORDER = ["L_thigh", "R_thigh", "L_calf", "R_calf", "L_upper", "R_upper", "L_fore", "R_fore"]

DOWN_ARM = {"L_upper": (-0.18, -1, 0.04), "L_fore": (-0.06, -1, 0.18)}
SIT_LEGS = {"L_thigh": (-0.08, -0.1, 1), "L_calf": (0, -1, 0.12), "sit": True}
POSES = {
    "sit": {**SIT_LEGS, "L_upper": (-0.12, -1, 0.35), "L_fore": (0.25, -0.35, 1)},
    "stand": dict(DOWN_ARM),
    "cheer_a": {"L_upper": (-0.65, 0.75, 0.12), "L_fore": (-0.35, 1, 0.1)},
    "cheer_b": {"L_upper": (-0.3, 1, 0.1), "L_fore": (-0.12, 1, 0.05)},
    "clap_a": {"L_upper": (-0.2, -0.85, 0.5), "L_fore": (0.25, 0.45, 1)},
    "clap_b": {"L_upper": (-0.15, -0.8, 0.55), "L_fore": (0.6, 0.4, 0.7)},
    "wave_a": {**DOWN_ARM, "R_upper": (0.55, 0.8, 0.12), "R_fore": (-0.25, 1, 0.1)},
    "wave_b": {**DOWN_ARM, "R_upper": (0.55, 0.8, 0.12), "R_fore": (0.55, 0.85, 0.1)},
}
FRAMES = ["sit", "stand", "cheer_a", "cheer_b", "clap_a", "clap_b", "wave_a", "wave_b"]


def blender_dir(u):
    """Unity body-frame direction -> Blender world (the Rocketbox body faces -Y, its left at +X)."""
    return Vector((-u[0], -u[2], u[1])).normalized()


def pose(arm, name):
    """Puts `arm` in POSES[name]; returns how far the hips drop (m) for a seated pose."""
    spec = dict(POSES[name])
    for key in list(spec):
        if key.startswith("L_"):
            r = "R_" + key[2:]
            if r not in spec:
                x, y, z = spec[key]
                spec[r] = (-x, y, z)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    inv = arm.matrix_world.inverted()
    for seg in ORDER:
        if seg not in spec:
            continue
        bone, child = SEGMENTS[seg]
        pb, pc = arm.pose.bones.get(bone), arm.pose.bones.get(child)
        if pb is None or pc is None:
            continue
        cur = (pc.head - pb.head).normalized()  # armature space
        want = (inv.to_3x3() @ blender_dir(spec[seg])).normalized()
        rot = cur.rotation_difference(want).to_matrix().to_4x4()
        from mathutils import Matrix
        head = Matrix.Translation(pb.head)
        pb.matrix = head @ rot @ head.inverted() @ pb.matrix
        bpy.context.view_layer.update()
    return 0.42 if spec.get("sit") else 0.0
