"""Maya top-level menu and GUI helpers for cross-platform X-Ray export."""

from __future__ import annotations

MENU_NAME = "XRayEngine2Menu"


def _prompt_export_static() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, long=True) or []
    if not sel:
        cmds.warning("Select a root node (e.g. |prop_name containing |vis) to export.")
        return

    result = cmds.fileDialog2(
        fileMode=0,
        caption="Export X-Ray Static Model (.model)",
        fileFilter="X-Ray Model (*.model);;All Files (*.*)",
    )
    if not result:
        return
    out_path = result[0]
    if out_path.lower().endswith(".model"):
        out_path = out_path[:-6]

    root = sel[0]
    cmds.xray_solid_visual_export(outfilename=out_path, root=root, tootle=True, hq_l=True)


def _prompt_export_skeleton() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, long=True) or []
    result = cmds.fileDialog2(
        fileMode=0,
        caption="Export X-Ray Skeleton (.skeleton)",
        fileFilter="X-Ray Skeleton (*.skeleton);;All Files (*.*)",
    )
    if not result:
        return
    out_path = result[0][:-9] if result[0].lower().endswith(".skeleton") else result[0]
    kwargs: dict[str, str] = {"outfilename": out_path}
    if sel:
        kwargs["root"] = sel[0]
    cmds.xray_skeleton_export(**kwargs)


def _prompt_export_skin() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, long=True) or []
    if not sel:
        cmds.warning("Select a skinned mesh/root group to export.")
        return
    result = cmds.fileDialog2(
        fileMode=0,
        caption="Export X-Ray Skinned Model (.skinned_model)",
        fileFilter="X-Ray Skinned Model (*.skinned_model);;All Files (*.*)",
    )
    if not result:
        return
    skel_prompt = cmds.promptDialog(
        title="Skeleton Path",
        message="Enter skeleton asset path (e.g. resources/animations/skeletons/neutral_01):",
        button=["OK", "Cancel"],
        defaultButton="OK",
        cancelButton="Cancel",
    )
    if skel_prompt != "OK":
        return
    skel_name = cmds.promptDialog(query=True, text=True).strip()
    if not skel_name:
        cmds.warning("Skeleton name is required for skinned model export.")
        return
    out_path = result[0][:-14] if result[0].lower().endswith(".skinned_model") else result[0]
    cmds.xray_skin_export(outfilename=out_path, skin_visual=sel[0], skeleton=skel_name, tootle=True, hq_l=True)


def _prompt_export_animation() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, long=True) or []
    result = cmds.fileDialog2(
        fileMode=0,
        caption="Export X-Ray Animation Clip (.clip)",
        fileFilter="X-Ray Clip (*.clip);;All Files (*.*)",
    )
    if not result:
        return
    out_path = result[0][:-5] if result[0].lower().endswith(".clip") else result[0]
    kwargs: dict[str, str] = {"outfilename": out_path}
    if sel:
        kwargs["root"] = sel[0]
    cmds.xray_anim_export(**kwargs)


def _prompt_create_asset_groups() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, shortNames=True) or ["xray_asset"]
    base = sel[0].split("|")[-1]
    root_xform = cmds.createNode("transform", name=f"{base}_root")
    cmds.createNode("xray_fake_drawing_locator", name=f"{base}_rootShape", parent=root_xform)
    cmds.group(empty=True, name="vis", parent=root_xform)
    cmds.group(empty=True, name="col", parent=root_xform)
    cmds.group(empty=True, name="locators", parent=root_xform)


def _prompt_export_physics() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, long=True) or []
    result = cmds.fileDialog2(
        fileMode=0,
        caption="Export X-Ray Physics (.physics / .ph)",
        fileFilter="X-Ray Physics (*.physics);;Ragdoll Physics (*.ph);;All Files (*.*)",
    )
    if not result:
        return
    kwargs: dict[str, str] = {"outfilename": result[0]}
    if sel:
        kwargs["root"] = sel[0]
    cmds.xray_ph_exporter(**kwargs)


def _prompt_build_collision() -> None:
    import maya.cmds as cmds  # type: ignore

    sel = cmds.ls(selection=True, long=True) or []
    if not sel:
        cmds.warning("Select a root skeleton joint first.")
        return
    cmds.xray_build_collision(recurse=True, start_from=sel[0])


def _prompt_set_resources_path() -> None:
    import cook_bridge
    import maya.cmds as cmds  # type: ignore

    chosen = cmds.fileDialog2(fileMode=3, caption="Select X-Ray resources directory")
    if chosen:
        cook_bridge.set_resources_path(chosen[0])


def install_menu() -> None:
    try:
        import maya.cmds as cmds  # type: ignore
    except ImportError:
        return

    if cmds.about(batch=True):
        return
    remove_menu()
    menu = cmds.menu(MENU_NAME, label="X-Ray 2.0", parent="MayaWindow", tearOff=True)
    cmds.menuItem(label="Create Static Model Hierarchy (vis/col/locators)", command=lambda *_: _prompt_create_asset_groups(), parent=menu)
    cmds.menuItem(label="Export Selected Static Model...", command=lambda *_: _prompt_export_static(), parent=menu)
    cmds.menuItem(divider=True, parent=menu)
    cmds.menuItem(label="Export Skeleton (.skeleton)...", command=lambda *_: _prompt_export_skeleton(), parent=menu)
    cmds.menuItem(label="Export Skinned Model (.skinned_model)...", command=lambda *_: _prompt_export_skin(), parent=menu)
    cmds.menuItem(label="Export Animation Clip (.clip)...", command=lambda *_: _prompt_export_animation(), parent=menu)
    cmds.menuItem(divider=True, parent=menu)
    cmds.menuItem(label="Create Physics Body", command=lambda *_: cmds.xray_body(), parent=menu)
    cmds.menuItem(label="Create Collision Shape", command=lambda *_: cmds.xray_collision(type="box"), parent=menu)
    cmds.menuItem(label="Create Physics Constraint", command=lambda *_: cmds.xray_constraint(type="ball"), parent=menu)
    cmds.menuItem(label="Build Ragdoll Collision From Selected Joint", command=lambda *_: _prompt_build_collision(), parent=menu)
    cmds.menuItem(label="Export Physics (.physics/.ph)...", command=lambda *_: _prompt_export_physics(), parent=menu)
    cmds.menuItem(divider=True, parent=menu)
    cmds.menuItem(label="Set Resources Path...", command=lambda *_: _prompt_set_resources_path(), parent=menu)


def remove_menu() -> None:
    try:
        import maya.cmds as cmds  # type: ignore
    except ImportError:
        return

    if cmds.menu(MENU_NAME, exists=True):
        cmds.deleteUI(MENU_NAME, menu=True)
