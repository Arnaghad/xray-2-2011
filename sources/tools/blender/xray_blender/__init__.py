# SPDX-License-Identifier: GPL-3.0-or-later
"""X-Ray Blender extension. Build the ZIP to include the shared DCC package."""

import bpy
from . import properties, preferences, operators, ui

CLASSES = (*properties.CLASSES, *preferences.CLASSES, *operators.CLASSES, *ui.CLASSES)
_registered_classes = []
_menu_hooks = []


def register():
    if _registered_classes:
        return
    try:
        for cls in CLASSES:
            bpy.utils.register_class(cls)
            _registered_classes.append(cls)
        properties.attach()
        for owner, callback in ((bpy.types.VIEW3D_MT_editor_menus, ui.draw_header_menu),
                                (bpy.types.VIEW3D_MT_add, ui.draw_add_menu),
                                (bpy.types.TOPBAR_MT_file_import, ui.draw_import_menu)):
            owner.append(callback)
            _menu_hooks.append((owner, callback))
    except Exception:
        unregister()
        raise


def unregister():
    while _menu_hooks:
        owner, callback = _menu_hooks.pop()
        owner.remove(callback)
    properties.detach()
    while _registered_classes:
        bpy.utils.unregister_class(_registered_classes.pop())
