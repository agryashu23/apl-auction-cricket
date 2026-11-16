"""
Pose check for make_gear.py's previews (-- --pose): skins the preview gear to
the reference armature with exactly the exported weights (bone + bone2/weight2),
then bends the body the way a batter in his stance does — knees flexed, wrists
cocked, fingers curled round a grip — so the renders show how the pads bend at
the knee and the gloves follow the fingers.
"""
import math


def skin(objects_parts, arm, human):
    """objects_parts: [(blender object, Part)]; human: Rocketbox bone -> HumanBodyBones name (left)."""
    to_rb = {}
    for rb, hb in human.items():
        to_rb[hb] = rb
        if hb.startswith("Left"):
            to_rb["Right" + hb[4:]] = rb.replace(" L ", " R ")
    for ob, part in objects_parts:
        groups = {}
        for i, (b, b2, w) in enumerate(zip(part.bone, part.bone2, part.w2)):
            for name, weight in ((b, 1 - w), (b2, w)):
                rb = to_rb.get(name)
                if rb is None or weight <= 0:
                    continue
                if rb not in groups:
                    groups[rb] = ob.vertex_groups.new(name=rb)
                groups[rb].add([i], weight, "ADD")
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = arm


def bend(arm, knee=55, wrist=30, curl=55):
    """A batting-stance bend on both sides (degrees), about each bone's own bend axis."""
    pb = arm.pose.bones
    for side in ("L", "R"):
        for name, deg in ((f"Bip01 {side} Calf", -knee), (f"Bip01 {side} Thigh", knee * 0.5), (f"Bip01 {side} Hand", wrist)):
            if name in pb:
                pb[name].rotation_mode = "XYZ"
                pb[name].rotation_euler = (0, 0, math.radians(deg))
        for f in range(1, 5):
            for seg in ("", "1", "2"):
                n = f"Bip01 {side} Finger{f}{seg}"
                if n in pb:
                    pb[n].rotation_mode = "XYZ"
                    pb[n].rotation_euler = (0, 0, math.radians(curl))
