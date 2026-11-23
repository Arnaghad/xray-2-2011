# SPDX-License-Identifier: GPL-3.0-or-later
"""Scene authoring and export operators."""

import bpy
from bpy.props import EnumProperty, FloatProperty, StringProperty
from bpy_extras.io_utils import ImportHelper
import struct
from pathlib import Path
from mathutils import Euler, Matrix, Vector
from . import preferences, exporters
from .properties import HELPER_KINDS
from ._dcc_common.constants import LOCATORS_COLLECTION_NAME


def locator_collection(scene):
    for collection in scene.collection.children:
        if collection.xray_kind == "LOCATORS" or collection.name == LOCATORS_COLLECTION_NAME:
            collection.xray_kind = "LOCATORS"
            return collection
    collection = bpy.data.collections.new(LOCATORS_COLLECTION_NAME)
    collection.xray_kind = "LOCATORS"
    scene.collection.children.link(collection)
    return collection


class XRAY_OT_check_paths(bpy.types.Operator):
    bl_idname = "xray.check_paths"
    bl_label = "Check Paths"
    bl_description = "Check that the resources folder and asset cooker exist"

    def execute(self, context):
        try:
            preferences.check_paths(context)
        except (OSError, ValueError) as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, "X-Ray resources folder and asset cooker found")
        return {"FINISHED"}


class XRAY_OT_add_helper(bpy.types.Operator):
    bl_idname = "xray.add_helper"
    bl_label = "Add X-Ray Helper"
    bl_description = "Create a physics helper, locator or static model root"
    bl_options = {"REGISTER", "UNDO"}
    kind: EnumProperty(name="Kind", items=HELPER_KINDS[1:], default="BODY")

    @classmethod
    def poll(cls, context):
        return context.scene is not None and context.mode == "OBJECT"

    def execute(self, context):
        active = context.active_object
        if self.kind == "COLLISION" and not (active and active.type == "EMPTY" and active.xray_kind == "BODY"):
            self.report({"ERROR"}, "Select an X-Ray physics body before adding a collision shape")
            return {"CANCELLED"}
        name = {"BODY": "xray_body", "COLLISION": "xray_collision", "CONSTRAINT": "xray_constraint",
                "LOCATOR": "locator", "STATIC_ROOT": "xray_static_root"}[self.kind]
        obj = bpy.data.objects.new(name, None)
        obj.xray_kind = self.kind
        obj.rotation_mode = "XYZ"
        obj.empty_display_size = 0.5
        obj.show_in_front = True
        obj.empty_display_type = {"BODY": "ARROWS", "COLLISION": "CUBE", "CONSTRAINT": "ARROWS",
                                  "LOCATOR": "PLAIN_AXES", "STATIC_ROOT": "CUBE"}[self.kind]
        obj.location = context.scene.cursor.location
        collection = locator_collection(context.scene) if self.kind == "LOCATOR" else context.collection
        collection.objects.link(obj)
        if self.kind == "COLLISION":
            obj.parent = active
            obj.location = (0.0, 0.0, 0.0)
        elif self.kind == "CONSTRAINT" and active and active.xray_kind == "BODY":
            obj.xray_constraint.body1 = active
            obj.location = active.matrix_world.translation
            if active.parent and active.parent.xray_kind == "BODY":
                obj.xray_constraint.body0 = active.parent
        elif self.kind == "LOCATOR" and active and active.xray_static_root:
            obj.parent = active
            obj.matrix_parent_inverse = active.matrix_world.inverted_safe()
        for selected in context.selected_objects:
            selected.select_set(False)
        obj.select_set(True)
        context.view_layer.objects.active = obj
        return {"FINISHED"}


class XRAY_OT_export(bpy.types.Operator):
    bl_idname = "xray.export_asset"
    bl_label = "Export X-Ray Asset"
    bl_description = "Validate the active asset, write exchange v2 and optionally run the asset cooker"

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT" and context.active_object is not None

    def execute(self, context):
        settings, root = context.scene.xray_export, context.active_object
        try:
            cli = preferences.cook_cli_path(context) if settings.cook else None
            directory = Path(bpy.path.abspath(settings.directory)).expanduser().resolve()
            exporters.require(bool(settings.directory), "Choose an output folder")
            if settings.kind == "animation":
                actions = sorted((a for a in bpy.data.actions if a.xray_export), key=lambda a: a.name) if settings.batch_actions else [
                    root.animation_data.action if root.animation_data and not settings.evaluated_scene else None]
                exporters.require(actions, "Mark at least one Action for batch export")
                results = [exporters.animation(context, root,
                    settings.name.rstrip("/") + "/" + action.name if settings.batch_actions else settings.name,
                    action=action, local_center=settings.local_center, precision_t=settings.precision_t,
                    precision_r=settings.precision_r, precision_s=settings.precision_s) for action in actions]
            elif settings.kind == "skeleton":
                results = [exporters.skeleton(context, root, settings.name)]
            elif settings.kind == "physics":
                results = [exporters.physics(context, root, settings.name)]
            else:
                results = [exporters.model(context, root, settings.name,
                    rig=root if settings.kind == "skinned_model" else None, skeleton_name=settings.skeleton_name)]
            # All scenes/actions are validated before the first filesystem mutation.
            for result in results:
                exporters.export_and_cook(result, directory / result.manifest["name"], cli)
        except (OSError, ValueError, RuntimeError) as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        self.report({"INFO"}, f"Exported {len(results)} X-Ray asset(s) to {directory}")
        return {"FINISHED"}


class XRAY_OT_fit_collision(bpy.types.Operator):
    bl_idname = "xray.fit_collision"
    bl_label = "Fit Collision to Meshes"
    bl_options = {"REGISTER", "UNDO"}
    shape: EnumProperty(name="Shape", items=[(s, s.title(), "") for s in ("box", "sphere", "cylinder")])

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT" and any(o.type == "MESH" for o in context.selected_objects)

    def execute(self, context):
        try:
            result = exporters.fit_collision(context, context.selected_objects, self.shape, preferences.cook_cli_path(context))
            transform = Matrix.LocRotScale(Vector(result["position"]),
                Euler(result["rotation"], "XYZ"), Vector(result["scale"]))
            active = context.active_object
            body = active if active and active.xray_kind == "BODY" else next(
                (p for p in exporters.ancestors(active) if p.xray_kind == "BODY"), None) if active else None
            if body is None:
                self.report({"ERROR"}, "Parent the mesh to a physics body or make the body active")
                return {"CANCELLED"}
            obj = bpy.data.objects.new("xray_collision", None)
            context.collection.objects.link(obj)
            obj.xray_kind = "COLLISION"
            obj.xray_collision.shape_type = self.shape
            obj.empty_display_size = 0.5
            obj.parent = body
            obj.matrix_world = transform
        except (OSError, ValueError, RuntimeError) as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        return {"FINISHED"}


class XRAY_OT_auto_ragdoll(bpy.types.Operator):
    bl_idname = "xray.auto_ragdoll"
    bl_label = "Create Ragdoll from Armature"
    bl_description = "Create editable box bodies and ball joints from the rest skeleton"
    bl_options = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT" and context.active_object and context.active_object.type == "ARMATURE"

    def execute(self, context):
        rig = context.active_object
        try:
            bones = exporters.rig_bones(rig)
            for bone in bones:
                exporters.trs(exporters.joint_matrix(rig, bone.matrix_local, bone), bone.name)
                exporters.require(bone.length > 1e-6, f"{bone.name}: bone is too short for ragdoll generation")
        except ValueError as error:
            self.report({"ERROR"}, str(error))
            return {"CANCELLED"}
        def helper(name, kind, parent, matrix):
            obj = bpy.data.objects.new(name, None)
            context.collection.objects.link(obj)
            obj.parent, obj.matrix_local = parent, matrix
            obj.xray_kind = kind
            obj.empty_display_size = 0.5 if kind == "COLLISION" else 1.0
            return obj
        root = helper("xray_ragdoll", "NONE", rig, Matrix.Identity(4))
        bodies = {}
        for bone in bones:
            matrix = exporters.joint_matrix(rig, bone.matrix_local, bone)
            body = helper(bone.name + "_body", "BODY", root, matrix)
            body.xray_body.anim_joint = bone.name
            body.xray_body.mass = max(0.1, bone.length * rig.xray_unit_scale * 5)
            bodies[bone.name] = body
            radius = max(bone.length * 0.15, 0.001)
            collision = matrix.inverted() @ bone.matrix_local @ Matrix.Translation((0, bone.length/2, 0))
            collision = collision @ Matrix.Diagonal((2*radius, bone.length, 2*radius, 1.0))
            helper(bone.name + "_collision", "COLLISION", body, collision)
            joint = helper(bone.name + "_joint", "CONSTRAINT", root, matrix)
            joint.xray_constraint.body1 = body
            if bone.parent:
                joint.xray_constraint.body0 = bodies[bone.parent.name]
                joint.xray_constraint.joint_type = "ball"
                for key in ("x_rotate", "y_rotate", "z_rotate"):
                    dof = getattr(joint.xray_constraint, key)
                    dof.enable_state, dof.limits_enable_state = True, True
                    dof.low_limit, dof.hi_limit = -0.785398, 0.785398
        context.view_layer.update()
        self.report({"INFO"}, f"Created {len(bones)} editable ragdoll bodies; review masses and joint limits")
        return {"FINISHED"}


class XRAY_OT_import(bpy.types.Operator, ImportHelper):
    bl_idname = "xray.import_asset"
    bl_label = "Import X-Ray / Maya ASCII"
    bl_description = "Import exchange, legacy resources or a basic Maya ASCII scene without Maya"
    bl_options = {"REGISTER", "UNDO"}
    filter_glob: StringProperty(default="*.json;*.model;*.skinned_model;*.skeleton;*.clip;*.physics;*.ph;*.ma;*.mb", options={"HIDDEN"})
    skeleton_path: StringProperty(name="Skeleton File", subtype="FILE_PATH", description="Needed for a skin or clip without a selected matching armature")
    fps: FloatProperty(name="Fallback Clip FPS", default=30, min=0.001, description="Used only when legacy check data does not supply fps")

    @classmethod
    def poll(cls, context):
        return context.mode == "OBJECT"

    def execute(self, context):
        from .importers import import_asset
        rig = context.active_object if context.active_object and context.active_object.type == "ARMATURE" else None
        try:
            result = import_asset(context, bpy.path.abspath(self.filepath), rig=rig,
                skeleton_path=bpy.path.abspath(self.skeleton_path) if self.skeleton_path else None, fps=self.fps)
        except (OSError, ValueError, RuntimeError, KeyError, IndexError, struct.error) as error:
            self.report({"ERROR"}, f"Cannot import asset: {error}")
            return {"CANCELLED"}
        message = f"Imported {len(result['roots'])} root(s)"
        if result["reports"]:
            message += f"; verified poses for {len(result['reports'])} clip(s)"
        if result.get("warnings"):
            message += f"; repaired geometry in {len(result['warnings'])} mesh(es), see collection import warnings"
        self.report({"WARNING"} if result.get("warnings") else {"INFO"}, message)
        return {"FINISHED"}


CLASSES = (XRAY_OT_check_paths, XRAY_OT_add_helper, XRAY_OT_export, XRAY_OT_fit_collision, XRAY_OT_auto_ragdoll, XRAY_OT_import)
