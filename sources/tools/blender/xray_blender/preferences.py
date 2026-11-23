# SPDX-License-Identifier: GPL-3.0-or-later
"""Global tool paths; project authoring metadata stays inside the blend file."""

import os
from pathlib import Path
import bpy
from bpy.props import StringProperty
from ._dcc_common.cook_bridge import find_cook_cli

ADDON_ID = __package__


def get_preferences(context):
    addon = context.preferences.addons.get(ADDON_ID)
    return addon.preferences if addon else None


def _absolute(path):
    return Path(bpy.path.abspath(os.path.expandvars(os.path.expanduser(path)))).resolve()


def resources_path(context):
    prefs = get_preferences(context)
    if prefs and prefs.resources_path.strip():
        return _absolute(prefs.resources_path)
    value = os.environ.get("XRAY_RESOURCES_PATH")
    if value:
        return _absolute(value)
    value = os.environ.get("STK2_ROOT_PATH")
    return _absolute(value) / "resources" if value else None


def cook_cli_path(context):
    prefs = get_preferences(context)
    explicit = _absolute(prefs.cook_cli_path) if prefs and prefs.cook_cli_path.strip() else None
    try:
        return find_cook_cli(explicit)
    except FileNotFoundError as error:
        raise ValueError("Choose an existing asset cooker executable in Settings") from error


def check_paths(context):
    root = resources_path(context)
    if root is None or not root.is_dir():
        raise ValueError("Choose an existing X-Ray resources folder in Settings")
    return root, cook_cli_path(context)


class XRAY_AddonPreferences(bpy.types.AddonPreferences):
    bl_idname = ADDON_ID
    resources_path: StringProperty(name="Resources Folder", subtype="DIR_PATH", description="X-Ray resources root; falls back to XRAY_RESOURCES_PATH or STK2_ROOT_PATH")
    cook_cli_path: StringProperty(name="Asset Cooker", subtype="FILE_PATH", description="xray_asset_cook_cli executable; leave blank to use SDK/environment discovery")

    def draw(self, context):
        self.layout.prop(self, "resources_path")
        self.layout.prop(self, "cook_cli_path")
        self.layout.operator("xray.check_paths", icon="CHECKMARK")


CLASSES = (XRAY_AddonPreferences,)
