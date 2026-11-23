"""Compatibility imports for the original Maya exchange API."""

import sys
from pathlib import Path

_tools_dir = str(Path(__file__).resolve().parents[2])
if _tools_dir not in sys.path:
    sys.path.insert(0, _tools_dir)

from dcc_common.exchange import *  # noqa: F401,F403,E402
