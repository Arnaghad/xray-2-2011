# SPDX-License-Identifier: GPL-3.0-or-later
"""Artist-facing helper menus and metadata panels."""

import bpy
from . import preferences
from ._dcc_common.constants import JOINT_DOF_NAMES


def draw_helpers(layout, context):
    for kind, label, icon in (("STATIC_ROOT", "Static Root", "OUTLINER_COLLECTION"),
                              ("BODY", "Physics Body", "EMPTY_AXIS"),
                              ("COLLISION", "Collision Shape", "MESH_CUBE"),
                              ("CONSTRAINT", "Constraint", "CONSTRAINT"),
                              ("LOCATOR", "Locator", "EMPTY_AXIS")):
        row = layout.row()
        if kind == "COLLISION":
            obj = context.active_object
            row.enabled = bool(obj and obj.type == "EMPTY" and obj.xray_kind == "BODY")
        row.operator("xray.add_helper", text=label, icon=icon).kind = kind


def draw_settings(layout):
    layout.operator("preferences.addon_show", text="Settings", icon="PREFERENCES").module = preferences.ADDON_ID
    layout.operator("xray.check_paths", icon="CHECKMARK")


def draw_export(layout, context):
    settings = context.scene.xray_export
    layout.prop(settings, "kind")
    layout.prop(settings, "name")
    layout.prop(settings, "directory")
    if settings.kind == "skinned_model":
        layout.prop(settings, "skeleton_name")
    if settings.kind == "animation":
        layout.prop(settings, "batch_actions")
        if not settings.batch_actions:
            layout.prop(settings, "evaluated_scene")
        obj = context.active_object
        if settings.batch_actions:
            for action in bpy.data.actions:
                row = layout.row()
                row.prop(action, "xray_export", text=action.name)
                row.prop(action, "xray_additive", text="Additive")
        elif not settings.evaluated_scene and obj and obj.animation_data and obj.animation_data.action:
            action = obj.animation_data.action
            layout.label(text=f"Action: {action.name}")
            layout.prop(action, "xray_additive")
        else:
            layout.label(text="Evaluated scene / NLA frame range")
        layout.prop(settings, "local_center")
        for name in ("precision_t", "precision_r", "precision_s"):
            layout.prop(settings, name)
    layout.prop(settings, "cook")
    layout.operator("xray.export_asset", icon="EXPORT")


class XRAY_MT_main(bpy.types.Menu):
    bl_label = "X-Ray 2.0"

    def draw(self, context):
        self.layout.operator("xray.import_asset", icon="IMPORT")
        draw_helpers(self.layout, context)
        self.layout.operator("xray.auto_ragdoll")
        for shape in ("box", "sphere", "cylinder"):
            self.layout.operator("xray.fit_collision", text=f"Fit {shape.title()}").shape = shape
        self.layout.operator("xray.export_asset", icon="EXPORT")
        self.layout.separator()
        draw_settings(self.layout)


class XRAY_PT_tools(bpy.types.Panel):
    bl_label = "X-Ray 2.0"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "X-Ray 2.0"

    def draw(self, context):
        self.layout.operator("xray.import_asset", icon="IMPORT")
        draw_helpers(self.layout, context)
        self.layout.operator("xray.auto_ragdoll")
        row = self.layout.row()
        for shape in ("box", "sphere", "cylinder"):
            row.operator("xray.fit_collision", text=f"Fit {shape.title()}").shape = shape
        self.layout.separator()
        draw_export(self.layout, context)
        self.layout.separator()
        draw_settings(self.layout)


class XRAY_PT_object(bpy.types.Panel):
    bl_label = "X-Ray 2.0"
    bl_space_type = "PROPERTIES"
    bl_region_type = "WINDOW"
    bl_context = "object"

    @classmethod
    def poll(cls, context):
        return context.object is not None

    def draw(self, context):
        obj, layout = context.object, self.layout
        layout.use_property_split = True
        if obj.type == "EMPTY":
            layout.prop(obj, "xray_kind")
            layout.prop(obj, "xray_export_name")
        else:
            layout.prop(obj, "xray_static_root")
        if obj.type == "ARMATURE":
            layout.prop(obj, "xray_axis_space")
            layout.prop(obj, "xray_unit_scale")
            layout.prop(obj, "xray_legacy_bone_frames")
        if obj.type == "MESH":
            layout.prop(obj, "xray_mesh_role")
        if obj.xray_kind == "BODY":
            layout.prop(obj.xray_body, "anim_joint")
            layout.prop(obj.xray_body, "mass")
            layout.prop(obj.xray_body, "mass_center")
        elif obj.xray_kind == "COLLISION":
            layout.prop(obj.xray_collision, "shape_type")
        elif obj.xray_kind == "LOCATOR":
            layout.prop(obj.xray_locator, "armature")
            if obj.xray_locator.armature:
                layout.prop_search(obj.xray_locator, "bone_name", obj.xray_locator.armature.data, "bones")
            else:
                layout.prop(obj.xray_locator, "bone_name")
        if obj.xray_kind in {"BODY", "CONSTRAINT"}:
            joint = obj.xray_constraint
            layout.prop(joint, "joint_type")
            layout.prop(joint, "body0")
            layout.prop(joint, "body1")
            for name in JOINT_DOF_NAMES:
                dof = getattr(joint, name)
                box = layout.box()
                unit = "m" if name.endswith("translate") else "rad"
                box.label(text=f"{name.replace('_', ' ').title()} ({unit})")
                box.prop(dof, "enable_state")
                column = box.column()
                column.enabled = dof.enable_state
                column.prop(dof, "limits_enable_state")
                limits = column.column()
                limits.enabled = dof.limits_enable_state
                limits.prop(dof, "low_limit")
                limits.prop(dof, "hi_limit")


class XRAY_PT_material(bpy.types.Panel):
    bl_label = "X-Ray 2.0"
    bl_space_type = "PROPERTIES"
    bl_region_type = "WINDOW"
    bl_context = "material"

    @classmethod
    def poll(cls, context):
        return context.material is not None

    def draw(self, context):
        self.layout.use_property_split = True
        self.layout.prop(context.material, "xray_shader")
        self.layout.prop(context.material, "xray_material")
        self.layout.prop(context.material, "xray_texture")


def draw_header_menu(self, context):
    self.layout.menu("XRAY_MT_main")


def draw_add_menu(self, context):
    self.layout.menu("XRAY_MT_main", text="X-Ray 2.0")


def draw_import_menu(self, context):
    self.layout.operator("xray.import_asset", text="X-Ray / Maya ASCII")


CLASSES = (XRAY_MT_main, XRAY_PT_tools, XRAY_PT_object, XRAY_PT_material)
