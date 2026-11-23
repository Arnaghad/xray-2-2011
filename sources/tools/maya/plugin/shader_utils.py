"""Material, shader, and texture metadata extraction utilities for Maya API 2.0."""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any


def sanitize_surface_name(sg_name: str, index_suffix: int = 0) -> str:
    """Match original surface::save_name() length cap and forbidden character escaping."""
    base = sg_name
    if len(base) > 61:
        base = base[:61]
    if index_suffix > 0:
        base = f"{base}_{index_suffix}"
    base = re.sub(r"[:/|\\]", "_", base)
    return base or "default_surface"


def normalize_texture_path(file_texture_path: str | None) -> str:
    """Convert an arbitrary Windows/Linux texture path into X-Ray relative texture id."""
    if not file_texture_path:
        return "notset"
    norm = str(file_texture_path).replace("\\", "/").strip()
    if not norm:
        return "notset"
    low = norm.lower()
    idx = low.find("textures/")
    if idx >= 0:
        rel = norm[idx + len("textures/") :]
    else:
        rel = Path(norm).name
    rel = rel.lower().strip("/")
    dot = rel.rfind(".")
    if dot > 0:
        rel = rel[:dot]
    return rel or "notset"


def _try_get_plug_string(dep_fn: Any, plug_names: tuple[str, ...]) -> str:
    for pname in plug_names:
        try:
            if not dep_fn.hasAttribute(pname):
                continue
            plug = dep_fn.findPlug(pname, False)
            if not plug.isNull:
                val = str(plug.asString()).strip()
                if val:
                    return val
        except Exception:
            continue
    return ""


def _try_get_plug_bool(dep_fn: Any, plug_names: tuple[str, ...]) -> bool | None:
    for pname in plug_names:
        try:
            if not dep_fn.hasAttribute(pname):
                continue
            plug = dep_fn.findPlug(pname, False)
            if not plug.isNull:
                return bool(plug.asBool())
        except Exception:
            continue
    return None


def _find_upstream_file_texture(shader_obj: Any, om2: Any) -> str:
    dep = om2.MFnDependencyNode(shader_obj)

    direct = _try_get_plug_string(dep, ("xrayTextureName", "textureName", "fileTextureName"))
    if direct:
        return direct

    for plug_name in ("color", "baseColor", "outColor", "diffuse", "diffuseColor"):
        try:
            if not dep.hasAttribute(plug_name):
                continue
            plug = dep.findPlug(plug_name, False)
            if plug.isNull:
                continue
        except Exception:
            continue

        # Direct single-hop check (works on both real OpenMaya and lightweight mocks)
        try:
            sources = plug.connectedTo(True, False)
            for src_plug in sources:
                src_node = src_plug.node()
                src_dep = om2.MFnDependencyNode(src_node)
                ft = _try_get_plug_string(src_dep, ("fileTextureName",))
                if ft:
                    return ft
        except Exception:
            pass

        # Full upstream DG walk inside real Maya
        if hasattr(om2, "MItDependencyGraph"):
            try:
                it = om2.MItDependencyGraph(
                    plug,
                    om2.MFn.kFileTexture,
                    om2.MItDependencyGraph.kUpstream,
                    om2.MItDependencyGraph.kBreadthFirst,
                    om2.MItDependencyGraph.kNodeLevel,
                )
                while not it.isDone():
                    tex_dep = om2.MFnDependencyNode(it.currentNode())
                    ft = _try_get_plug_string(tex_dep, ("fileTextureName",))
                    if ft:
                        return ft
                    it.next()
            except Exception:
                pass

    return ""


def extract_shader_metadata(sg_obj: Any, om2: Any) -> dict[str, Any]:
    """Extract shadingGroup, texture_name, material_name, and double_sided attributes."""
    sg_dep = om2.MFnDependencyNode(sg_obj)
    sg_name = str(sg_dep.name())
    surface_name = sanitize_surface_name(sg_name)
    shader_name = surface_name
    texture_name = "notset"
    material_name = ""
    double_sided = False

    try:
        if sg_dep.hasAttribute("surfaceShader"):
            sh_plug = sg_dep.findPlug("surfaceShader", False)
            conns = sh_plug.connectedTo(True, False)
            if conns:
                shader_node = conns[0].node()
                sh_dep = om2.MFnDependencyNode(shader_node)
                raw_tex = _find_upstream_file_texture(shader_node, om2)
                if raw_tex:
                    texture_name = normalize_texture_path(raw_tex)
                material_name = _try_get_plug_string(
                    sh_dep, ("xrayMaterialName", "materialName", "xray_material_name")
                )
                ds = _try_get_plug_bool(sh_dep, ("xrayDoubleSided", "doubleSided"))
                if ds is not None:
                    double_sided = ds
    except Exception:
        pass

    if not material_name and texture_name != "notset":
        material_name = texture_name

    return {
        "sg_name": sg_name,
        "surface_name": surface_name,
        "shader_name": shader_name,
        "texture_name": texture_name,
        "material_name": material_name,
        "double_sided": double_sided,
    }
