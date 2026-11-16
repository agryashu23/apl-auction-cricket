"""
Renders the crowd's sprite frames from Rocketbox bodies (Blender, headless):
for each body in BODIES and each pose in crowd_pose.FRAMES, a colour frame
(lit, textured, transparent background) and a shirt-mask frame (white where
the vertex rides the spine / clavicles / upper arms: the top a fan wears),
128x256 px over a 0.9 x 1.8 m card from 0.2 m above the floor.
Writes build/gear/crowd/<body>_<pose>.png and <body>_<pose>_mask.png;
crowd_atlas.py packs them.

    blender -b --python tools/gear/crowd_sprites.py [-- Body1 Body2 ...]
"""
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import crowd_pose  # noqa: E402

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
OUT = ROOT + "build/gear/crowd/"
BODIES = [
    "Male_Adult_01", "Male_Adult_03", "Male_Adult_05", "Male_Adult_08", "Male_Adult_10", "Male_Adult_12", "Male_Adult_15",
    "Male_Adult_17", "Male_Adult_18", "Sports_Male_03", "Female_Adult_01", "Female_Adult_02", "Business_Female_02", "Business_Female_04",
]
SHIRT_BONES = ("Spine", "Clavicle", "UpperArm")
if "--" in sys.argv and sys.argv[sys.argv.index("--") + 1:]:
    BODIES = sys.argv[sys.argv.index("--") + 1:]


def load(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}client-assets/rocketbox/{name}/Export/{name}.fbx")
    tex_dir = f"{ROOT}client-assets/rocketbox/{name}/Textures/"
    for img in bpy.data.images:
        base = os.path.basename(img.filepath)
        if os.path.exists(tex_dir + base):
            img.filepath = tex_dir + base
            img.reload()
    for m in bpy.data.materials:
        if "opacity" in m.name.lower():
            m.blend_method = "CLIP"  # hair cards: cut out, not opaque sheets
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    # The shirt mask as a colour attribute: 1 on the top's bones, 0 elsewhere.
    for ob in meshes:
        groups = {g.index: g.name for g in ob.vertex_groups}
        attr = ob.data.color_attributes.new("kit", "FLOAT_COLOR", "POINT")
        for v in ob.data.vertices:
            w = sum(g.weight for g in v.groups if any(k in groups.get(g.group, "") for k in SHIRT_BONES))
            tot = sum(g.weight for g in v.groups) or 1
            s = 1.0 if w / tot > 0.5 else 0.0
            attr.data[v.index].color = (s, s, s, 1)
        ob.data.color_attributes.active_color = attr
        ob.data.color_attributes.render_color_index = list(ob.data.color_attributes).index(attr)
    return arm, meshes


def setup_camera():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = 128, 256
    sc.render.image_settings.color_mode = "RGBA"
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.8
    cam.location = (0, -5, 1.1)  # card: 0.2 .. 2.0 m (raised arms fit)
    cam.rotation_euler = (1.5708, 0, 0)
    return sc


def render(sc, path, mask):
    sh = sc.display.shading
    if mask:
        sh.light = "FLAT"
        sh.color_type = "VERTEX"
    else:
        sh.light = "STUDIO"
        sh.color_type = "TEXTURE"
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


os.makedirs(OUT, exist_ok=True)
for body in BODIES:
    arm, meshes = load(body)
    sc = setup_camera()
    base = arm.location.z
    for frame in crowd_pose.FRAMES:
        drop = crowd_pose.pose(arm, frame)
        arm.location.z = base - drop
        bpy.context.view_layer.update()
        render(sc, f"{OUT}{body}_{frame}.png", False)
        render(sc, f"{OUT}{body}_{frame}_mask.png", True)
    print("CROWD", body)
