"""Autodesk Maya Python API 2.0 entry module for X-Ray Engine 2.0 (Windows & Linux).

Load in Maya via:
    maya.cmds.loadPlugin("/path/to/sources/tools/maya/plugin/xray_maya_plugin.py")
"""

from __future__ import annotations

import inspect
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any


def _resolve_plugin_dir() -> Path:
    if "__file__" in globals():
        return Path(globals()["__file__"]).resolve().parent
    filename = inspect.getfile(_resolve_plugin_dir)
    if filename:
        p = Path(filename)
        if p.is_file():
            return p.resolve().parent
    import os

    for env_var in ("MAYA_PLUG_IN_PATH",):
        for entry in os.environ.get(env_var, "").split(os.pathsep):
            if entry:
                cand = Path(entry) / "xray_maya_plugin.py"
                if cand.is_file():
                    return cand.resolve().parent
    for entry in sys.path:
        if entry:
            cand = Path(entry) / "xray_maya_plugin.py"
            if cand.is_file():
                return cand.resolve().parent
    raise RuntimeError("Cannot locate xray_maya_plugin.py directory")


# Ensure sibling modules in plugin/ and exchange/ remain importable when Maya loads this file directly
_PLUGIN_DIR = _resolve_plugin_dir()
_MAYA_TOOLS_DIR = _PLUGIN_DIR.parent
for _p in (str(_PLUGIN_DIR), str(_MAYA_TOOLS_DIR)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import constants  # noqa: E402
import cook_bridge  # noqa: E402
import physics_extractor  # noqa: E402
import shelf_menu  # noqa: E402
import skeletal_extractor  # noqa: E402
import static_extractor  # noqa: E402


def maya_useNewAPI() -> None:  # noqa: N802 - required sentinel for Maya Python API 2.0
    """Tell Maya's plugin loader to use maya.api.OpenMaya (API 2.0)."""


def _import_maya_api() -> tuple[Any, Any]:
    import maya.api.OpenMaya as om2  # type: ignore
    import maya.api.OpenMayaUI as om2ui  # type: ignore

    return om2, om2ui


def _parse_flag_string(arg_db: Any, short_flag: str, long_flag: str) -> str | None:
    for fl in (short_flag, long_flag):
        if arg_db.isFlagSet(fl):
            return str(arg_db.flagArgumentString(fl, 0))
    return None


def _is_any_flag_set(arg_db: Any, *flags: str) -> bool:
    return any(arg_db.isFlagSet(fl) for fl in flags)


def execute_solid_visual_export(
    *,
    out_filename: str,
    root_name: str,
    optimize_mesh: bool = True,
    high_quality_overdraw: bool = True,
    no_out_to_db: bool = False,
    overwrite_settings: bool = False,
    converted_out: str | None = None,
    exchange_out: str | None = None,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
) -> Path:
    """Execute extract -> xray-dcc-exchange/v2 -> xray_maya_cook_cli pipeline."""
    out_str = out_filename.replace("\\", "/").strip()
    if not out_str:
        raise ValueError("Output file name (-o / -outfilename) cannot be empty")
    if not out_str.endswith(".model"):
        out_str = f"{out_str}.model"
    target_path = Path(out_str)
    logical_name = cook_bridge.make_logical_name(target_path, "models")

    with tempfile.TemporaryDirectory(prefix="xray_maya_export_") as tmp:
        tmp_path = Path(tmp)
        bundle_dir = Path(exchange_out) if exchange_out else (tmp_path / "exchange")
        static_extractor.extract_static_scene_to_exchange(
            root_name,
            bundle_dir,
            logical_name=logical_name,
            om2=om2,
            optimize_mesh=optimize_mesh,
        )

        if no_out_to_db:
            raw_dir = target_path
            archive_path = None
        else:
            raw_dir = tmp_path / "cooked_raw.model"
            archive_path = target_path
            target_path.parent.mkdir(parents=True, exist_ok=True)

        cook_bridge.run_cook_cli(
            bundle_dir,
            raw_dir,
            converted_dir=converted_out,
            archive_path=archive_path,
            optimize_mesh=optimize_mesh,
            high_quality_overdraw=high_quality_overdraw,
            overwrite_settings=overwrite_settings,
            cli_path=cli_path,
        )

    return target_path


def _normalize_output_path(out_filename: str, suffix: str) -> Path:
    out_str = out_filename.replace("\\", "/").strip()
    if not out_str:
        raise ValueError("Output file name (-o / -outfilename) cannot be empty")
    if not out_str.endswith(suffix):
        out_str = f"{out_str}{suffix}"
    target = Path(out_str)
    target.parent.mkdir(parents=True, exist_ok=True)
    return target


def execute_skeleton_export(
    *,
    out_filename: str,
    root_name: str | None = None,
    converted_out: str | None = None,
    exchange_out: str | None = None,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
) -> Path:
    target_path = _normalize_output_path(out_filename, ".skeleton")
    logical_name = cook_bridge.make_logical_name(target_path, "animations")

    with tempfile.TemporaryDirectory(prefix="xray_maya_skel_") as tmp:
        tmp_path = Path(tmp)
        bundle_dir = Path(exchange_out) if exchange_out else (tmp_path / "exchange")
        skeletal_extractor.extract_skeleton_to_exchange(
            bundle_dir,
            logical_name=logical_name,
            root_name=root_name,
            om2=om2,
        )
        cook_bridge.run_cook_cli(
            bundle_dir,
            target_path,
            converted_dir=converted_out,
            cli_path=cli_path,
        )
    return target_path


def execute_skin_export(
    *,
    out_filename: str,
    skin_root_name: str,
    skeleton_name: str,
    optimize_mesh: bool = True,
    high_quality_overdraw: bool = True,
    no_out_to_db: bool = False,
    overwrite_settings: bool = False,
    converted_out: str | None = None,
    exchange_out: str | None = None,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
    om2_anim: Any | None = None,
    cmds: Any | None = None,
) -> Path:
    target_path = _normalize_output_path(out_filename, ".skinned_model")
    logical_name = cook_bridge.make_logical_name(target_path, "models")

    with tempfile.TemporaryDirectory(prefix="xray_maya_skin_") as tmp:
        tmp_path = Path(tmp)
        bundle_dir = Path(exchange_out) if exchange_out else (tmp_path / "exchange")
        skeletal_extractor.extract_skinned_scene_to_exchange(
            skin_root_name,
            skeleton_name,
            bundle_dir,
            logical_name=logical_name,
            om2=om2,
            om2_anim=om2_anim,
            cmds=cmds,
            optimize_mesh=optimize_mesh,
        )
        if no_out_to_db:
            raw_dir = target_path
            archive_path = None
        else:
            raw_dir = tmp_path / "cooked_raw.skinned_model"
            archive_path = target_path
        cook_bridge.run_cook_cli(
            bundle_dir,
            raw_dir,
            converted_dir=converted_out,
            archive_path=archive_path,
            optimize_mesh=optimize_mesh,
            high_quality_overdraw=high_quality_overdraw,
            overwrite_settings=overwrite_settings,
            cli_path=cli_path,
        )
    return target_path


def execute_anim_export(
    *,
    out_filename: str,
    root_name: str | None = None,
    local_center_node: str | None = None,
    is_additive: bool = False,
    precision_t: float = 0.005,
    precision_r: float = 0.005,
    precision_s: float = 0.005,
    converted_out: str | None = None,
    exchange_out: str | None = None,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
    om2_anim: Any | None = None,
) -> Path:
    target_path = _normalize_output_path(out_filename, ".clip")
    logical_name = cook_bridge.make_logical_name(target_path, "animations")

    with tempfile.TemporaryDirectory(prefix="xray_maya_anim_") as tmp:
        tmp_path = Path(tmp)
        bundle_dir = Path(exchange_out) if exchange_out else (tmp_path / "exchange")
        skeletal_extractor.extract_animation_to_exchange(
            bundle_dir,
            logical_name=logical_name,
            root_name=root_name,
            local_center_node=local_center_node,
            is_additive=is_additive,
            precision_t=precision_t,
            precision_r=precision_r,
            precision_s=precision_s,
            om2=om2,
            om2_anim=om2_anim,
        )
        cook_bridge.run_cook_cli(
            bundle_dir,
            target_path,
            converted_dir=converted_out,
            cli_path=cli_path,
        )
    return target_path


def execute_ph_export(
    *,
    out_filename: str,
    root_name: str | None = None,
    converted_out: str | None = None,
    exchange_out: str | None = None,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
    cmds: Any | None = None,
) -> Path:
    """Extract scene physics hierarchy and cook .physics/.ph (+ optional binary config)."""
    ext = ".ph" if out_filename.lower().endswith(".ph") else ".physics"
    target_path = _normalize_output_path(out_filename, ext)
    logical_name = cook_bridge.make_logical_name(target_path, "physics")

    with tempfile.TemporaryDirectory(prefix="xray_maya_physics_") as tmp:
        tmp_path = Path(tmp)
        bundle_dir = Path(exchange_out) if exchange_out else (tmp_path / "exchange")
        physics_extractor.extract_physics_scene_to_exchange(
            bundle_dir,
            root_name=root_name,
            logical_name=logical_name,
            om2=om2,
            cmds=cmds,
        )
        cook_bridge.run_cook_cli(
            bundle_dir,
            target_path,
            converted_dir=converted_out,
            cli_path=cli_path,
        )
    return target_path


try:
    _om2, _om2ui = _import_maya_api()

    class FakeDrawingLocatorNode(_om2ui.MPxLocatorNode):
        node_id = _om2.MTypeId(constants.FAKE_DRAWING_LOCATOR_ID)
        node_name = constants.FAKE_DRAWING_LOCATOR_NAME

        @staticmethod
        def creator() -> Any:
            return FakeDrawingLocatorNode()

        @staticmethod
        def initialize() -> None:
            return None

    def _register_joint_attributes(cls: Any) -> None:
        enum_fn = _om2.MFnEnumAttribute()
        cls.jointType = enum_fn.create("jointType", "jt", 0)
        for idx, label in enumerate(constants.JOINT_TYPES):
            enum_fn.addField(label, idx)
        enum_fn.keyable = True
        cls.addAttribute(cls.jointType)

        msg_fn = _om2.MFnMessageAttribute()
        cls.body0 = msg_fn.create("body0", "b0")
        cls.addAttribute(cls.body0)
        cls.body1 = msg_fn.create("body1", "b1")
        cls.addAttribute(cls.body1)

        num_fn = _om2.MFnNumericAttribute()
        short_dofs = ("xt", "yt", "zt", "xr", "yr", "zr")
        for dof, short_s in zip(constants.JOINT_DOF_NAMES, short_dofs):
            attr = num_fn.create(f"{dof}_enabled", f"b{short_s}", _om2.MFnNumericData.kBoolean, False)
            num_fn.keyable = True
            num_fn.storable = True
            cls.addAttribute(attr)
            setattr(cls, f"{dof}_enabled", attr)

    class XRayCollisionNode(_om2ui.MPxLocatorNode):
        node_id = _om2.MTypeId(constants.COLLISION_NODE_ID)
        node_name = constants.COLLISION_NODE_NAME

        @staticmethod
        def creator() -> Any:
            return XRayCollisionNode()

        @staticmethod
        def initialize() -> None:
            enum_fn = _om2.MFnEnumAttribute()
            XRayCollisionNode.shapeType = enum_fn.create("shapeType", "sht", 0)
            for idx, label in enumerate(constants.PHYSICS_SHAPES):
                enum_fn.addField(label, idx)
            enum_fn.keyable = True
            XRayCollisionNode.addAttribute(XRayCollisionNode.shapeType)

    class XRayBodyNode(_om2ui.MPxLocatorNode):
        node_id = _om2.MTypeId(constants.BODY_NODE_ID)
        node_name = constants.BODY_NODE_NAME

        @staticmethod
        def creator() -> Any:
            return XRayBodyNode()

        @staticmethod
        def initialize() -> None:
            num_fn = _om2.MFnNumericAttribute()
            XRayBodyNode.massCenter = num_fn.create(
                "MassCenter", "mc", _om2.MFnNumericData.k3Double, 0.0
            )
            num_fn.keyable = True
            num_fn.storable = True
            XRayBodyNode.addAttribute(XRayBodyNode.massCenter)

            XRayBodyNode.mass = num_fn.create("Mass", "mass", _om2.MFnNumericData.kDouble, 1.0)
            num_fn.setMin(0.001)
            num_fn.setMax(10000.0)
            num_fn.keyable = True
            num_fn.storable = True
            XRayBodyNode.addAttribute(XRayBodyNode.mass)

            typed_fn = _om2.MFnTypedAttribute()
            XRayBodyNode.animJoint = typed_fn.create("AnimationJoint", "aj", _om2.MFnData.kString)
            typed_fn.keyable = True
            typed_fn.storable = True
            XRayBodyNode.addAttribute(XRayBodyNode.animJoint)

            msg_fn = _om2.MFnMessageAttribute()
            XRayBodyNode.joint = msg_fn.create("joint", "j")
            msg_fn.keyable = True
            XRayBodyNode.addAttribute(XRayBodyNode.joint)

            _register_joint_attributes(XRayBodyNode)

    class XRayConstraintNode(_om2ui.MPxLocatorNode):
        node_id = _om2.MTypeId(constants.CONSTRAINT_NODE_ID)
        node_name = constants.CONSTRAINT_NODE_NAME

        @staticmethod
        def creator() -> Any:
            return XRayConstraintNode()

        @staticmethod
        def initialize() -> None:
            _register_joint_attributes(XRayConstraintNode)

    def _add_visual_export_flags(syntax: Any) -> None:
        syntax.addFlag(*constants.FLAG_OUT_FILENAME, _om2.MSyntax.kString)
        syntax.addFlag(*constants.FLAG_TOOTLE, _om2.MSyntax.kNoArg)
        syntax.addFlag(*constants.FLAG_TOOTLE_HQ, _om2.MSyntax.kNoArg)
        syntax.addFlag(*constants.FLAG_NO_OUT_TO_DB, _om2.MSyntax.kNoArg)
        syntax.addFlag(*constants.FLAG_NO_OPTIMIZE, _om2.MSyntax.kNoArg)
        syntax.addFlag(*constants.FLAG_OVERWRITE_SETTINGS, _om2.MSyntax.kNoArg)
        syntax.addFlag(*constants.FLAG_CONVERTED_OUT, _om2.MSyntax.kString)
        syntax.addFlag(*constants.FLAG_EXCHANGE_OUT, _om2.MSyntax.kString)
        syntax.enableEdit = False
        syntax.enableQuery = False

    def _parse_visual_export_options(arg_db: Any) -> dict[str, Any]:
        no_opt = _is_any_flag_set(arg_db, *constants.FLAG_NO_OPTIMIZE)
        # Match original UX: -t explicitly opts into optimization; optimize by default unless -no is passed.
        optimize_mesh = not no_opt
        return {
            "optimize_mesh": optimize_mesh,
            "high_quality_overdraw": _is_any_flag_set(arg_db, *constants.FLAG_TOOTLE_HQ) or optimize_mesh,
            "no_out_to_db": _is_any_flag_set(arg_db, *constants.FLAG_NO_OUT_TO_DB),
            "overwrite_settings": _is_any_flag_set(arg_db, *constants.FLAG_OVERWRITE_SETTINGS),
            "converted_out": _parse_flag_string(arg_db, *constants.FLAG_CONVERTED_OUT),
            "exchange_out": _parse_flag_string(arg_db, *constants.FLAG_EXCHANGE_OUT),
        }

    def _parse_flag_float(
        arg_db: Any, short_flag: str, long_flag: str, default: float
    ) -> float:
        for flag in (short_flag, long_flag):
            if arg_db.isFlagSet(flag):
                return float(arg_db.flagArgumentDouble(flag, 0))
        return default

    class SolidVisualExportCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_SOLID_VISUAL_EXPORT

        @staticmethod
        def creator() -> Any:
            return SolidVisualExportCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_ROOT, _om2.MSyntax.kString)
            _add_visual_export_flags(syntax)
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            out_file = _parse_flag_string(arg_db, *constants.FLAG_OUT_FILENAME)
            root_name = _parse_flag_string(arg_db, *constants.FLAG_ROOT)
            if not out_file:
                raise RuntimeError("Missing required flag -o / -outfilename")
            if not root_name:
                raise RuntimeError("Missing required flag -r / -root")

            opts = _parse_visual_export_options(arg_db)
            target = execute_solid_visual_export(
                out_filename=out_file,
                root_name=root_name,
                om2=_om2,
                **opts,
            )
            _om2.MGlobal.displayInfo(f"Export {target.as_posix()} successful!")
            self.setResult(target.as_posix())

    class XRayPathCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_XRAY_PATH

        @staticmethod
        def creator() -> Any:
            return XRayPathCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag("-m", "-models", _om2.MSyntax.kNoArg)
            syntax.addFlag("-a", "-animations", _om2.MSyntax.kNoArg)
            syntax.addFlag("-r", "-resources", _om2.MSyntax.kString)
            syntax.enableQuery = True
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            res_arg = _parse_flag_string(arg_db, "-r", "-resources")
            if res_arg is not None:
                res_root = cook_bridge.set_resources_path(res_arg)
                self.setResult(res_root.as_posix())
                return

            res_root = cook_bridge.get_resources_path()
            if res_root is None:
                _om2.MGlobal.displayWarning("XRAY_RESOURCES_PATH / STK2_ROOT_PATH is not configured")
                self.setResult("")
                return

            if _is_any_flag_set(arg_db, "-m", "-models"):
                self.setResult((res_root / "exported" / "models").as_posix())
            elif _is_any_flag_set(arg_db, "-a", "-animations"):
                self.setResult((res_root / "exported" / "animations").as_posix())
            else:
                self.setResult(res_root.as_posix())

    class SkeletonExportCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_SKELETON_EXPORT

        @staticmethod
        def creator() -> Any:
            return SkeletonExportCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_OUT_FILENAME, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_ROOT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_CONVERTED_OUT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_EXCHANGE_OUT, _om2.MSyntax.kString)
            syntax.enableEdit = False
            syntax.enableQuery = False
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            out_file = _parse_flag_string(arg_db, *constants.FLAG_OUT_FILENAME)
            if not out_file:
                raise RuntimeError("Missing required flag -o / -outfilename")

            target = execute_skeleton_export(
                out_filename=out_file,
                root_name=_parse_flag_string(arg_db, *constants.FLAG_ROOT),
                converted_out=_parse_flag_string(arg_db, *constants.FLAG_CONVERTED_OUT),
                exchange_out=_parse_flag_string(arg_db, *constants.FLAG_EXCHANGE_OUT),
                om2=_om2,
            )
            _om2.MGlobal.displayInfo(f"Export {target.as_posix()} successful!")
            self.setResult(target.as_posix())

    class SkinExportCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_SKIN_EXPORT

        @staticmethod
        def creator() -> Any:
            return SkinExportCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_SKIN_VISUAL, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_ROOT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_SKELETON, _om2.MSyntax.kString)
            _add_visual_export_flags(syntax)
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            out_file = _parse_flag_string(arg_db, *constants.FLAG_OUT_FILENAME)
            skin_root = _parse_flag_string(arg_db, *constants.FLAG_SKIN_VISUAL) or _parse_flag_string(
                arg_db, *constants.FLAG_ROOT
            )
            skeleton_name = _parse_flag_string(arg_db, *constants.FLAG_SKELETON)
            if not out_file:
                raise RuntimeError("Missing required flag -o / -outfilename")
            if not skin_root:
                raise RuntimeError("Missing required flag -skn / -skin_visual")
            if not skeleton_name:
                raise RuntimeError("Missing required flag -skl / -skeleton")

            opts = _parse_visual_export_options(arg_db)
            target = execute_skin_export(
                out_filename=out_file,
                skin_root_name=skin_root,
                skeleton_name=skeleton_name,
                om2=_om2,
                **opts,
            )
            _om2.MGlobal.displayInfo(f"Export {target.as_posix()} successful!")
            self.setResult(target.as_posix())

    class AnimExportCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_ANIM_EXPORT

        @staticmethod
        def creator() -> Any:
            return AnimExportCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_OUT_FILENAME, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_ROOT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_LOCAL_CENTER, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_ADDITIVE_ANIM, _om2.MSyntax.kNoArg)
            syntax.addFlag(*constants.FLAG_PRECISION_T, _om2.MSyntax.kDouble)
            syntax.addFlag(*constants.FLAG_PRECISION_R, _om2.MSyntax.kDouble)
            syntax.addFlag(*constants.FLAG_PRECISION_S, _om2.MSyntax.kDouble)
            syntax.addFlag(*constants.FLAG_CONVERTED_OUT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_EXCHANGE_OUT, _om2.MSyntax.kString)
            syntax.enableEdit = False
            syntax.enableQuery = False
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            out_file = _parse_flag_string(arg_db, *constants.FLAG_OUT_FILENAME)
            if not out_file:
                raise RuntimeError("Missing required flag -o / -outfilename")

            target = execute_anim_export(
                out_filename=out_file,
                root_name=_parse_flag_string(arg_db, *constants.FLAG_ROOT),
                local_center_node=_parse_flag_string(arg_db, *constants.FLAG_LOCAL_CENTER),
                is_additive=_is_any_flag_set(arg_db, *constants.FLAG_ADDITIVE_ANIM),
                precision_t=_parse_flag_float(arg_db, *constants.FLAG_PRECISION_T, 0.005),
                precision_r=_parse_flag_float(arg_db, *constants.FLAG_PRECISION_R, 0.005),
                precision_s=_parse_flag_float(arg_db, *constants.FLAG_PRECISION_S, 0.005),
                converted_out=_parse_flag_string(arg_db, *constants.FLAG_CONVERTED_OUT),
                exchange_out=_parse_flag_string(arg_db, *constants.FLAG_EXCHANGE_OUT),
                om2=_om2,
            )
            _om2.MGlobal.displayInfo(f"Export {target.as_posix()} successful!")
            self.setResult(target.as_posix())

    def _parse_multi_flag_strings(arg_db: Any, short_flag: str, long_flag: str) -> list[str]:
        values: list[str] = []
        for flag in (short_flag, long_flag):
            if not arg_db.isFlagSet(flag):
                continue
            count = arg_db.numberOfFlagUses(flag) if hasattr(arg_db, "numberOfFlagUses") else 1
            if count > 1 and hasattr(arg_db, "getFlagArgumentList"):
                for idx in range(count):
                    al = arg_db.getFlagArgumentList(flag, idx)
                    values.append(str(al.asString(0)))
                return values
            values.append(str(arg_db.flagArgumentString(flag, 0)))
            return values
        return values

    def _resolve_edit_target(arg_db: Any) -> str | None:
        raw_is_edit = getattr(arg_db, "isEdit", False)
        is_edit = bool(raw_is_edit()) if callable(raw_is_edit) else bool(raw_is_edit)
        if not is_edit:
            return None
        if hasattr(arg_db, "getObjectList"):
            try:
                obj_sel = arg_db.getObjectList()
                if obj_sel is not None and obj_sel.length() > 0:
                    return str(obj_sel.getSelectionStrings()[0])
            except Exception:
                pass
        if hasattr(arg_db, "getObjectStrings"):
            try:
                objs = arg_db.getObjectStrings()
                if objs:
                    return str(objs[0])
            except Exception:
                pass
        sel = _om2.MGlobal.getActiveSelectionList()
        if sel.length() > 0:
            return str(sel.getSelectionStrings()[0])
        return None

    class CreateBodyCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_CREATE_BODY

        @staticmethod
        def creator() -> Any:
            return CreateBodyCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_NAME, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_ANIM_JOINT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_LINK, _om2.MSyntax.kString)
            syntax.makeFlagMultiUse(constants.FLAG_LINK[0])
            syntax.enableEdit = True
            syntax.setObjectType(_om2.MSyntax.kSelectionList, 0, 1)
            syntax.useSelectionAsDefault(True)
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            res = physics_extractor.create_body(
                name=_parse_flag_string(arg_db, *constants.FLAG_NAME),
                anim_joint=_parse_flag_string(arg_db, *constants.FLAG_ANIM_JOINT),
                links=_parse_multi_flag_strings(arg_db, *constants.FLAG_LINK),
                edit_target=_resolve_edit_target(arg_db),
                om2=_om2,
            )
            self.setResult(res)

    class CreateCollisionCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_CREATE_COLLISION

        @staticmethod
        def creator() -> Any:
            return CreateCollisionCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_NAME, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_TYPE, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_PARENT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_AUTO, _om2.MSyntax.kString)
            syntax.enableEdit = False
            syntax.enableQuery = False
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            res = physics_extractor.create_collision(
                name=_parse_flag_string(arg_db, *constants.FLAG_NAME),
                shape_type=_parse_flag_string(arg_db, *constants.FLAG_TYPE) or "box",
                parent=_parse_flag_string(arg_db, *constants.FLAG_PARENT),
                auto_target=_parse_flag_string(arg_db, *constants.FLAG_AUTO),
                om2=_om2,
            )
            self.setResult(res)

    class CreateJointCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_CREATE_JOINT

        @staticmethod
        def creator() -> Any:
            return CreateJointCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_NAME, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_TYPE, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_LINK, _om2.MSyntax.kString)
            syntax.makeFlagMultiUse(constants.FLAG_LINK[0])
            syntax.enableEdit = True
            syntax.setObjectType(_om2.MSyntax.kSelectionList, 0, 1)
            syntax.useSelectionAsDefault(True)
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            res = physics_extractor.create_joint(
                name=_parse_flag_string(arg_db, *constants.FLAG_NAME),
                joint_type=_parse_flag_string(arg_db, *constants.FLAG_TYPE),
                links=_parse_multi_flag_strings(arg_db, *constants.FLAG_LINK),
                edit_target=_resolve_edit_target(arg_db),
                om2=_om2,
            )
            self.setResult(res)

    class BuildCollisionCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_BUILD_COLLISION

        @staticmethod
        def creator() -> Any:
            return BuildCollisionCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_RECURSE, _om2.MSyntax.kNoArg)
            syntax.addFlag(*constants.FLAG_START_FROM, _om2.MSyntax.kString)
            syntax.enableEdit = False
            syntax.enableQuery = False
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            start = _parse_flag_string(arg_db, *constants.FLAG_START_FROM)
            if not start:
                raise RuntimeError("Missing required flag -j / -start_from")
            created = physics_extractor.build_collision_hierarchy(
                start,
                recurse=_is_any_flag_set(arg_db, *constants.FLAG_RECURSE),
                om2=_om2,
            )
            self.setResult(created)

    class PhExporterCmd(_om2.MPxCommand):
        cmd_name = constants.CMD_PH_EXPORT

        @staticmethod
        def creator() -> Any:
            return PhExporterCmd()

        @staticmethod
        def newSyntax() -> Any:
            syntax = _om2.MSyntax()
            syntax.addFlag(*constants.FLAG_OUT_FILENAME, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_ROOT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_CONVERTED_OUT, _om2.MSyntax.kString)
            syntax.addFlag(*constants.FLAG_EXCHANGE_OUT, _om2.MSyntax.kString)
            syntax.enableEdit = False
            syntax.enableQuery = False
            return syntax

        def doIt(self, args: Any) -> None:  # noqa: N802
            arg_db = _om2.MArgDatabase(self.syntax(), args)
            out_file = _parse_flag_string(arg_db, *constants.FLAG_OUT_FILENAME)
            if not out_file:
                raise RuntimeError("Missing required flag -o / -outfilename")
            target = execute_ph_export(
                out_filename=out_file,
                root_name=_parse_flag_string(arg_db, *constants.FLAG_ROOT),
                converted_out=_parse_flag_string(arg_db, *constants.FLAG_CONVERTED_OUT),
                exchange_out=_parse_flag_string(arg_db, *constants.FLAG_EXCHANGE_OUT),
                om2=_om2,
            )
            _om2.MGlobal.displayInfo(f"Export {target.as_posix()} successful!")
            self.setResult(target.as_posix())

    if hasattr(_om2, "MPxFileTranslator"):

        class PhysicsExporterTranslator(_om2.MPxFileTranslator):
            @staticmethod
            def creator() -> Any:
                return PhysicsExporterTranslator()

            def haveWriteMethod(self) -> bool:  # noqa: N802
                return True

            def defaultExtension(self) -> str:  # noqa: N802
                return "physics"

            def writer(self, file_obj: Any, options: str, access_mode: Any) -> None:
                target = execute_ph_export(out_filename=file_obj.fullName(), om2=_om2)
                _om2.MGlobal.displayInfo(f"Export {target.as_posix()} successful!")

    _REGISTERED_NODES = (
        FakeDrawingLocatorNode,
        XRayCollisionNode,
        XRayBodyNode,
        XRayConstraintNode,
    )

    _REGISTERED_COMMANDS = (
        SolidVisualExportCmd,
        XRayPathCmd,
        SkeletonExportCmd,
        SkinExportCmd,
        AnimExportCmd,
        CreateBodyCmd,
        CreateCollisionCmd,
        CreateJointCmd,
        BuildCollisionCmd,
        PhExporterCmd,
    )

    def initializePlugin(mobject: Any) -> None:  # noqa: N802
        plugin = _om2.MFnPlugin(
            mobject, constants.PLUGIN_VENDOR, constants.PLUGIN_VERSION, "Any"
        )
        for node_cls in _REGISTERED_NODES:
            plugin.registerNode(
                node_cls.node_name,
                node_cls.node_id,
                node_cls.creator,
                node_cls.initialize,
                _om2.MPxNode.kLocatorNode,
            )
        for cmd_cls in _REGISTERED_COMMANDS:
            plugin.registerCommand(
                cmd_cls.cmd_name,
                cmd_cls.creator,
                cmd_cls.newSyntax,
            )
        if hasattr(_om2, "MPxFileTranslator"):
            plugin.registerFileTranslator(
                constants.TRANSLATOR_PHYSICS,
                None,
                PhysicsExporterTranslator.creator,
            )
        shelf_menu.install_menu()
        _om2.MGlobal.displayInfo("Initialized cross-platform X-Ray Maya Python plugin!")

    def uninitializePlugin(mobject: Any) -> None:  # noqa: N802
        shelf_menu.remove_menu()
        plugin = _om2.MFnPlugin(mobject)
        if hasattr(_om2, "MPxFileTranslator"):
            plugin.deregisterFileTranslator(constants.TRANSLATOR_PHYSICS)
        for cmd_cls in reversed(_REGISTERED_COMMANDS):
            plugin.deregisterCommand(cmd_cls.cmd_name)
        for node_cls in reversed(_REGISTERED_NODES):
            plugin.deregisterNode(node_cls.node_id)

except ImportError:
    # Outside Maya interpreter (e.g. T1 headless CI / unittest), pure helper functions stay usable
    pass
