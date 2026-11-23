"""DCC-independent discovery and subprocess bridge for the X-Ray asset cooker."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from .logical_names import make_logical_name


def _exe_names() -> tuple[str, ...]:
    if sys.platform.startswith("win"):
        return ("xray_asset_cook_cli.exe", "xray_asset_cook_cli", "xray_maya_cook_cli.exe", "xray_maya_cook_cli")
    return ("xray_asset_cook_cli", "xray_asset_cook_cli.exe", "xray_maya_cook_cli", "xray_maya_cook_cli.exe")


def _repo_root() -> Path:
    # sources/tools/dcc_common/cook_bridge.py -> repo root is 3 levels up
    return Path(__file__).resolve().parents[3]


def find_cook_cli(explicit_path: str | Path | None = None) -> Path:
    """Locate the portable xray_maya_cook_cli binary across Windows and Linux."""
    if explicit_path is not None:
        p = Path(explicit_path)
        if p.is_file():
            return p
        raise FileNotFoundError(f"Explicit xray_maya_cook_cli path not found: {p}")

    for env_name in ("XRAY_ASSET_COOK_CLI", "XRAY_MAYA_COOK_CLI"):
        env_val = os.environ.get(env_name)
        if not env_val:
            continue
        p = Path(env_val)
        if p.is_file():
            return p
        if os.name == "nt" and not p.suffix:
            p_exe = p.with_suffix(".exe")
            if p_exe.is_file():
                return p_exe

    names = _exe_names()
    here = Path(__file__).resolve()
    repo = _repo_root()

    search_dirs: list[Path] = [
        here.parent / "bin",
        here.parent.parent / "bin",
        here.parent.parent / "maya/plugin/bin",
        here.parent.parent / "maya/bin",
    ]
    for env_root in ("XRAY_SDK_ROOT", "STK2_ROOT_PATH"):
        val = os.environ.get(env_root)
        if val:
            search_dirs.extend([Path(val) / "bin", Path(val) / "binaries", Path(val)])

    search_dirs.extend(
        [
            repo / "build/maya-port/standalone-cook",
            repo / "build/sdk-native-20261108-release/binaries",
            repo / "build/sdk-native-20261108-debug/binaries",
        ]
    )

    build_dir = repo / "build"
    if build_dir.is_dir():
        for child in sorted(build_dir.iterdir()):
            if child.is_dir():
                search_dirs.append(child / "binaries")
                search_dirs.append(child / "Release")
                search_dirs.append(child / "Debug")
                search_dirs.append(child)

    for d in search_dirs:
        for name in names:
            cand = d / name
            if cand.is_file():
                return cand

    for name in names:
        which = shutil.which(name)
        if which:
            return Path(which)

    raise FileNotFoundError(
        "Could not locate xray_asset_cook_cli (or xray_maya_cook_cli) executable. "
        "Build sources/xray/maya_cook or set XRAY_ASSET_COOK_CLI (legacy: XRAY_MAYA_COOK_CLI)."
    )



def run_cook_cli(
    exchange_dir: str | Path,
    output_dir: str | Path,
    *,
    converted_dir: str | Path | None = None,
    archive_path: str | Path | None = None,
    optimize_mesh: bool = True,
    high_quality_overdraw: bool = True,
    preserve_tangents: bool = False,
    overwrite_settings: bool = False,
    max_verts: int | None = None,
    cli_path: str | Path | None = None,
) -> subprocess.CompletedProcess[str]:
    """Invoke xray_maya_cook_cli with cross-platform arguments and clear error reporting."""
    cli = find_cook_cli(cli_path)
    cmd = [
        str(cli),
        "--input",
        str(Path(exchange_dir)),
        "--output",
        str(Path(output_dir)),
    ]
    if converted_dir is not None:
        cmd.extend(["--converted-output", str(Path(converted_dir))])
    if archive_path is not None:
        cmd.extend(["--pack-archive", str(Path(archive_path))])
    if not optimize_mesh:
        cmd.append("--no-optimize")
    if not high_quality_overdraw:
        cmd.append("--fast-overdraw")
    if preserve_tangents:
        cmd.append("--preserve-tangents")
    if overwrite_settings:
        cmd.append("--overwrite-settings")
    if max_verts is not None:
        cmd.extend(["--max-verts", str(int(max_verts))])

    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        msg = (res.stderr or res.stdout or f"Exit code {res.returncode}").strip()
        raise RuntimeError(f"xray_maya_cook_cli failed: {msg}")
    return res


def run_fit_collision_cli(
    exchange_dir: str | Path,
    *,
    cli_path: str | Path | None = None,
) -> list[dict[str, Any]]:
    """Invoke xray_maya_cook_cli --fit-collision on a physics exchange bundle."""
    cli = find_cook_cli(cli_path)
    cmd = [
        str(cli),
        "--fit-collision",
        "--input",
        str(Path(exchange_dir)),
    ]
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        msg = (res.stderr or res.stdout or f"Exit code {res.returncode}").strip()
        raise RuntimeError(f"xray_maya_cook_cli --fit-collision failed: {msg}")
    payload = json.loads(res.stdout)
    return list(payload.get("results", []))
