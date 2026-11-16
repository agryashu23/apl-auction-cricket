"""
Line-up of the cast bodies (kit_cast.json) with their own textures, hair cards
on: build/gear/lineup_heads.png (head and shoulders, front and three-quarter)
and build/gear/lineup_bodies.png (full length), labelled left to right in
kit_cast order — for judging which bodies read as cricketers.

    blender -b --python tools/gear/lineup.py [-- Male_Adult_01 Male_Adult_05 ...]
"""
import json
import os
import sys

import bpy

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
OUT = ROOT + "build/gear/"
if "--" in sys.argv and sys.argv[sys.argv.index("--") + 1:]:
    names = sys.argv[sys.argv.index("--") + 1:]
else:
    names = [e["model"] for e in json.load(open(ROOT + "client/Assets/Resources/Data/kit_cast.json"))["cast"]]

def shoot(n):
    """One body alone at the origin, its textures relinked, head and full-length renders."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}client-assets/rocketbox/{n}/Export/{n}.fbx")
    tex_dir = f"{ROOT}client-assets/rocketbox/{n}/Textures/"
    for img in bpy.data.images:
        base = os.path.basename(img.filepath)
        if os.path.exists(tex_dir + base):
            img.filepath = tex_dir + base
            img.reload()
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    for tag, (x, z, scale, res, rot) in {"head": (0.0, 1.66, 0.42, (300, 300), 0.0), "head34": (-0.0, 1.66, 0.42, (300, 300), 0.6),
                                          "body": (0.0, 0.95, 2.0, (200, 400), 0.0)}.items():
        import math
        cam.location = (math.sin(rot) * -4, -4 * math.cos(rot), z)
        cam.rotation_euler = (1.5708, 0, -rot)
        cam.data.ortho_scale = scale
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.filepath = f"{OUT}lineup/{n}_{tag}.png"
        bpy.ops.render.render(write_still=True)


os.makedirs(OUT + "lineup", exist_ok=True)
for n in names:
    shoot(n)
print("LINEUP", names)
