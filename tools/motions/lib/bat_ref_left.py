import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from running_bat import *
import bmesh
arm,_ = load_rig(); rig = BatRig(arm)
pose = mirror_y(dict(STANCE))
rig.solve(pose, {}, write=True); bpy.context.view_layer.update()
MW = arm.matrix_world
def rigid(M):
    loc, rot, _ = M.decompose(); return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
hand_pose_w = rigid(MW @ arm.pose.bones[PFX+"LeftHand"].matrix)
a = V(pose["ba"]).normalized(); n = ortho(pose["bn"], a); z = n.cross(a)
batM = Matrix((n, a, z)).transposed().to_4x4(); batM.translation = V(pose["bg"])
rel = hand_pose_w.inverted() @ batM
me = bpy.data.meshes.new("Bat"); bm = bmesh.new()
def box(cx, cy, cz, sx, sy, sz):
    r = bmesh.ops.create_cube(bm, size=1.0)
    for vtx in r["verts"]: vtx.co = V((vtx.co.x*sx+cx, vtx.co.y*sy+cy, vtx.co.z*sz+cz))
top = -GRIP_R
box(0, top+0.15, 0, 0.034, 0.30, 0.034); box(-0.008, top+0.30+0.28, 0, 0.045, 0.56, 0.108)
bm.to_mesh(me); bm.free()
bat = bpy.data.objects.new("Bat", me); bpy.context.scene.collection.objects.link(bat)
bpy.ops.object.mode_set(mode='POSE')
for p in arm.pose.bones: p.matrix_basis = Matrix.Identity(4)
bpy.ops.object.mode_set(mode='OBJECT'); bpy.context.view_layer.update()
bat.parent = arm; bat.parent_type = 'BONE'; bat.parent_bone = PFX+"LeftHand"; bpy.context.view_layer.update()
bat.matrix_world = rigid(MW @ arm.data.bones[PFX+"LeftHand"].matrix_local) @ rel; bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode='POSE'); rig.solve(pose, {}, write=True); bpy.ops.object.mode_set(mode='OBJECT'); bpy.context.view_layer.update()
print("left grip check err (m):", round((bat.matrix_world.translation - V(pose["bg"])).length, 5))
bpy.ops.object.mode_set(mode='POSE')
for p in arm.pose.bones: p.matrix_basis = Matrix.Identity(4)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(OUTR, "bat_attachment_reference_LEFT_HANDED.fbx"), object_types={'ARMATURE','MESH'},
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, use_armature_deform_only=False, bake_anim=False)
