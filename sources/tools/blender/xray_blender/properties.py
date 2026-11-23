# SPDX-License-Identifier: GPL-3.0-or-later
"""Persistent authoring data. RNA properties leave saved ID data on disable."""

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, FloatVectorProperty, IntProperty, PointerProperty, StringProperty
from ._dcc_common.constants import PHYSICS_SHAPES, JOINT_TYPES, JOINT_DOF_NAMES

HELPER_KINDS = (
    ("NONE", "None", "Ordinary Blender object"),
    ("BODY", "Physics Body", "Physics body with a link to an animation joint"),
    ("COLLISION", "Collision", "Primitive collision shape parented to a physics body"),
    ("CONSTRAINT", "Constraint", "Joint between physics bodies"),
    ("LOCATOR", "Locator", "Named attachment transform"),
    ("STATIC_ROOT", "Static Root", "Root of a static model hierarchy"),
)


def _shape_display(settings, context):
    obj = settings.id_data
    if isinstance(obj, bpy.types.Object) and obj.type == "EMPTY" and obj.xray_kind == "COLLISION":
        obj.empty_display_type = {"box": "CUBE", "sphere": "SPHERE", "cylinder": "CIRCLE"}[settings.shape_type]


def _kind_changed(obj, context):
    marker = obj.xray_kind == "STATIC_ROOT"
    if obj.xray_static_root != marker:
        obj.xray_static_root = marker


def _root_changed(obj, context):
    if obj.xray_static_root and obj.xray_kind != "STATIC_ROOT":
        obj.xray_kind = "STATIC_ROOT"
    elif not obj.xray_static_root and obj.xray_kind == "STATIC_ROOT":
        obj.xray_kind = "NONE"


def _body_poll(settings, obj):
    return obj.type == "EMPTY" and obj.xray_kind == "BODY"


def _armature_poll(settings, obj):
    return obj.type == "ARMATURE"


class XRAY_PG_dof(bpy.types.PropertyGroup):
    enable_state: BoolProperty(name="Enabled", default=False)
    limits_enable_state: BoolProperty(name="Use Limits", default=False)
    low_limit: FloatProperty(name="Lower Limit", default=0.0)
    hi_limit: FloatProperty(name="Upper Limit", default=0.0)


class XRAY_PG_body(bpy.types.PropertyGroup):
    anim_joint: StringProperty(name="Animation Joint", description="Skeleton joint controlled by this body")
    mass: FloatProperty(name="Mass (kg)", default=1.0, min=0.0)
    mass_center: FloatVectorProperty(name="Mass Center", size=3, default=(0.0, 0.0, 0.0), subtype="TRANSLATION")


class XRAY_PG_collision(bpy.types.PropertyGroup):
    shape_type: EnumProperty(name="Shape", items=[(s, s.title(), "") for s in PHYSICS_SHAPES], default="box", update=_shape_display)


class XRAY_PG_constraint(bpy.types.PropertyGroup):
    export_name: StringProperty(name="Logical Joint Name")
    joint_type: EnumProperty(name="Joint Type", items=[(s, s.title(), "") for s in JOINT_TYPES], default="rigid")
    body0: PointerProperty(name="Body 0", type=bpy.types.Object, poll=_body_poll)
    body1: PointerProperty(name="Body 1", type=bpy.types.Object, poll=_body_poll)
    x_translate: PointerProperty(type=XRAY_PG_dof)
    y_translate: PointerProperty(type=XRAY_PG_dof)
    z_translate: PointerProperty(type=XRAY_PG_dof)
    x_rotate: PointerProperty(type=XRAY_PG_dof)
    y_rotate: PointerProperty(type=XRAY_PG_dof)
    z_rotate: PointerProperty(type=XRAY_PG_dof)


class XRAY_PG_locator(bpy.types.PropertyGroup):
    armature: PointerProperty(name="Armature", type=bpy.types.Object, poll=_armature_poll)
    bone_name: StringProperty(name="Bone", description="Leave empty for an unbound locator")


class XRAY_PG_export(bpy.types.PropertyGroup):
    kind: EnumProperty(name="Asset Type", items=[(k, label, "") for k, label in (
        ("static_model", "Static Model"), ("skeleton", "Skeleton"), ("skinned_model", "Skinned Model"),
        ("animation", "Animation"), ("physics", "Physics"))])
    name: StringProperty(name="Logical Asset Name", default="models/asset")
    directory: StringProperty(name="Output Folder", subtype="DIR_PATH", default="//xray_export/")
    skeleton_name: StringProperty(name="Skeleton Resource", default="characters/actor")
    local_center: PointerProperty(name="Local Center", type=bpy.types.Object)
    batch_actions: BoolProperty(name="Export Marked Actions", default=False)
    evaluated_scene: BoolProperty(name="Sample Scene / NLA", default=False,
        description="Sample the evaluated scene frame range with all NLA tracks, instead of isolating the active Action")
    cook: BoolProperty(name="Cook Assets", default=True)
    precision_t: FloatProperty(name="Translation Precision (m)", min=0.000001, default=0.005)
    precision_r: FloatProperty(name="Rotation Precision (rad)", min=0.000001, default=0.005)
    precision_s: FloatProperty(name="Scale Precision", min=0.000001, default=0.005)


CLASSES = (XRAY_PG_dof, XRAY_PG_body, XRAY_PG_collision, XRAY_PG_constraint, XRAY_PG_locator, XRAY_PG_export)

# Factories create fresh deferred properties for each registration cycle.
DEFINITIONS = (
    (bpy.types.Scene, "xray_export", lambda: PointerProperty(type=XRAY_PG_export)),
    (bpy.types.Object, "xray_kind", lambda: EnumProperty(name="X-Ray Role", items=HELPER_KINDS, default="NONE", update=_kind_changed)),
    (bpy.types.Object, "xray_export_name", lambda: StringProperty(name="Logical Helper Name")),
    (bpy.types.Object, "xray_static_root", lambda: BoolProperty(name="Static Root", default=False, update=_root_changed)),
    (bpy.types.Object, "xray_body", lambda: PointerProperty(type=XRAY_PG_body)),
    (bpy.types.Object, "xray_collision", lambda: PointerProperty(type=XRAY_PG_collision)),
    (bpy.types.Object, "xray_constraint", lambda: PointerProperty(type=XRAY_PG_constraint)),
    (bpy.types.Object, "xray_locator", lambda: PointerProperty(type=XRAY_PG_locator)),
    (bpy.types.Object, "xray_axis_space", lambda: EnumProperty(name="Armature Space", items=(
        ("Z_UP_RH", "Blender Z-up", "Rig authored in Blender coordinates"),
        ("Y_UP_RH", "Legacy Y-up", "Preserve imported joint coordinates; orientation belongs on the armature object")), default="Z_UP_RH")),
    (bpy.types.Material, "xray_shader", lambda: StringProperty(name="X-Ray Shader")),
    (bpy.types.Material, "xray_material", lambda: StringProperty(name="X-Ray Material")),
    (bpy.types.Material, "xray_texture", lambda: StringProperty(name="Texture", description="Logical texture path without extension")),
    (bpy.types.Object, "xray_mesh_role", lambda: EnumProperty(name="Mesh Role", items=(
        ("RENDER", "Render", ""), ("COLLISION", "Collision", ""), ("HQ_COLLISION", "HQ Collision", "")), default="RENDER")),
    (bpy.types.Object, "xray_unit_scale", lambda: FloatProperty(name="Rig Unit in Meters", min=0.000001, default=1.0)),
    (bpy.types.Object, "xray_legacy_bone_frames", lambda: BoolProperty(name="Legacy Joint Frames", default=False,
        description="Imported bones use Blender Y = original joint X; remove the fixed frame correction on export")),
    (bpy.types.Object, "xray_preserve_space", lambda: BoolProperty(name="Preserve Imported Space", default=False)),
    (bpy.types.Bone, "xray_has_joint_correction", lambda: BoolProperty(default=False)),
    (bpy.types.Bone, "xray_joint_correction", lambda: FloatVectorProperty(size=16,
        default=tuple(float(i == j) for i in range(4) for j in range(4)))),
    (bpy.types.Action, "xray_sample_count", lambda: IntProperty(name="Imported Samples", default=0, min=0)),
    (bpy.types.Action, "xray_frame_start", lambda: FloatProperty(name="Source Start Frame", default=0)),
    (bpy.types.Action, "xray_fps", lambda: FloatProperty(name="Source FPS", default=30, min=0.001)),
    (bpy.types.Action, "xray_root_already_relative", lambda: BoolProperty(name="Root Already Relative", default=False)),
    (bpy.types.Action, "xray_precision_t", lambda: FloatProperty(name="Source Translation Precision", default=0.005, min=0.000001)),
    (bpy.types.Action, "xray_precision_r", lambda: FloatProperty(name="Source Rotation Precision", default=0.005, min=0.000001)),
    (bpy.types.Action, "xray_precision_s", lambda: FloatProperty(name="Source Scale Precision", default=0.005, min=0.000001)),
    (bpy.types.Action, "xray_export", lambda: BoolProperty(name="Export Clip", default=False)),
    (bpy.types.Action, "xray_additive", lambda: BoolProperty(name="Additive", default=False)),
    (bpy.types.Collection, "xray_kind", lambda: EnumProperty(name="X-Ray Collection", items=(
        ("NONE", "None", "Ordinary collection"), ("LOCATORS", "Locators", "Attachment transforms")), default="NONE")),
)
_attached = []


def attach():
    for owner, name, factory in DEFINITIONS:
        if hasattr(owner, name):
            raise RuntimeError(f"Cannot register X-Ray: {owner.__name__}.{name} already exists")
    try:
        for owner, name, factory in DEFINITIONS:
            setattr(owner, name, factory())
            _attached.append((owner, name))
    except Exception:
        detach()
        raise


def detach():
    while _attached:
        owner, name = _attached.pop()
        delattr(owner, name)
