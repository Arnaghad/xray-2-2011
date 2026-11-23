"""Canonical node identifiers and command flags matching sources/xray/maya/sources/."""

from __future__ import annotations

import sys
from pathlib import Path

# Shared conventions; Maya node IDs and command flags stay in this adapter.
_tools_dir = str(Path(__file__).resolve().parents[2])
if _tools_dir not in sys.path:
    sys.path.insert(0, _tools_dir)
from dcc_common.constants import (  # noqa: E402,F401
    OBJECT_MOVER_BONE_NAME, PHYSICS_SHAPES, JOINT_TYPES, JOINT_DOF_NAMES,
)

PLUGIN_NAME = "xray_maya_plugin"
PLUGIN_VENDOR = "X-Ray 2.0 Cross-Platform Pipeline"
PLUGIN_VERSION = "2.1.0"

# Exact parity with sources/xray/maya/sources/node_identifiers.h
COLLISION_NODE_ID = 0x00600
COLLISION_NODE_NAME = "xray_collision"

BODY_NODE_ID = 0x00700
BODY_NODE_NAME = "xray_body"

BODY_MANIP_NODE_ID = 0x00701
BODY_MANIP_NODE_NAME = "xray_bodyManip"

CONSTRAINT_NODE_ID = 0x00800
CONSTRAINT_NODE_NAME = "xray_constraint"

FAKE_DRAWING_LOCATOR_ID = 0x00801
FAKE_DRAWING_LOCATOR_NAME = "xray_fake_drawing_locator"

MATERIAL_NODE_ID = 0x00802
MATERIAL_NODE_NAME = "xray_material"

NODE_IDENTIFIERS: dict[str, int] = {
    COLLISION_NODE_NAME: COLLISION_NODE_ID,
    BODY_NODE_NAME: BODY_NODE_ID,
    BODY_MANIP_NODE_NAME: BODY_MANIP_NODE_ID,
    CONSTRAINT_NODE_NAME: CONSTRAINT_NODE_ID,
    FAKE_DRAWING_LOCATOR_NAME: FAKE_DRAWING_LOCATOR_ID,
    MATERIAL_NODE_NAME: MATERIAL_NODE_ID,
}

# Exact command names registered by sources/xray/maya/sources/maya_entry_point.cpp
CMD_SOLID_VISUAL_EXPORT = "xray_solid_visual_export"
CMD_SKIN_EXPORT = "xray_skin_export"
CMD_SKELETON_EXPORT = "xray_skeleton_export"
CMD_ANIM_EXPORT = "xray_anim_export"
CMD_XRAY_PATH = "xray_path"
CMD_CREATE_BODY = "xray_body"
CMD_CREATE_COLLISION = "xray_collision"
CMD_CREATE_JOINT = "xray_constraint"
CMD_BUILD_COLLISION = "xray_build_collision"
CMD_PH_EXPORT = "xray_ph_exporter"
TRANSLATOR_PHYSICS = "xray_ph_exporter"

PLUGIN_COMMANDS: tuple[str, ...] = (
    CMD_SOLID_VISUAL_EXPORT,
    CMD_SKIN_EXPORT,
    CMD_SKELETON_EXPORT,
    CMD_ANIM_EXPORT,
    CMD_XRAY_PATH,
    CMD_CREATE_BODY,
    CMD_CREATE_COLLISION,
    CMD_CREATE_JOINT,
    CMD_BUILD_COLLISION,
    CMD_PH_EXPORT,
)

# Exact short/long flags matching sources/xray/maya/sources/export_defines.h
FLAG_OUT_FILENAME = ("-o", "-outfilename")
FLAG_ROOT = ("-r", "-root")
FLAG_SKIN_VISUAL = ("-skn", "-skin_visual")
FLAG_SKELETON = ("-skl", "-skeleton")
FLAG_TOOTLE = ("-t", "-tootle")
FLAG_TOOTLE_HQ = ("-hq", "-hq_l")
FLAG_NO_OUT_TO_DB = ("-ndb", "-ndb_l")
FLAG_LOCAL_CENTER = ("-l", "-localCoordinatCenterNode")
FLAG_ADDITIVE_ANIM = ("-a", "-additiveToAnimation")
FLAG_PRECISION_T = ("-pt", "-precision_t")
FLAG_PRECISION_R = ("-pr", "-precision_r")
FLAG_PRECISION_S = ("-ps", "-precision_s")

# Extension flags (backward-compatible additions)
FLAG_NO_OPTIMIZE = ("-no", "-noOptimize")
FLAG_OVERWRITE_SETTINGS = ("-ow", "-overwriteSettings")
FLAG_CONVERTED_OUT = ("-c", "-converted")
FLAG_EXCHANGE_OUT = ("-ex", "-exchange")

# Physics flags and node/type constants
FLAG_NAME = ("-n", "-name")
FLAG_TYPE = ("-t", "-type")
FLAG_ANIM_JOINT = ("-aj", "-anim_joint")
FLAG_LINK = ("-l", "-link")
FLAG_PARENT = ("-p", "-parent")
FLAG_AUTO = ("-a", "-auto")
FLAG_RECURSE = ("-r", "-recurse")
FLAG_START_FROM = ("-j", "-start_from")

