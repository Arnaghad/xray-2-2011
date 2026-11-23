"""Maya resource preferences plus the shared DCC cooker bridge."""

from __future__ import annotations

import os
import sys
from pathlib import Path

_tools_dir = str(Path(__file__).resolve().parents[2])
if _tools_dir not in sys.path:
    sys.path.insert(0, _tools_dir)

from dcc_common.cook_bridge import (  # noqa: E402,F401
    find_cook_cli, run_cook_cli, run_fit_collision_cli,
)
from dcc_common.logical_names import make_logical_name  # noqa: E402,F401


def get_resources_path() -> Path | None:
    """Resolve active X-Ray resources root from Maya optionVar or environment."""
    for var in ("XRAY_RESOURCES_PATH",):
        val = os.environ.get(var)
        if val:
            return Path(val)
    stk2 = os.environ.get("STK2_ROOT_PATH")
    if stk2:
        return Path(stk2) / "resources"

    try:
        import maya.cmds as cmds  # type: ignore

        if cmds.optionVar(exists="xrayResourcesPath"):
            val = cmds.optionVar(query="xrayResourcesPath")
            if val:
                return Path(val)
    except ImportError:
        pass
    return None


def set_resources_path(path: str | Path) -> Path:
    resolved = Path(path).resolve()
    os.environ["XRAY_RESOURCES_PATH"] = resolved.as_posix()
    try:
        import maya.cmds as cmds  # type: ignore

        cmds.optionVar(stringValue=("xrayResourcesPath", resolved.as_posix()))
    except ImportError:
        pass
    return resolved


