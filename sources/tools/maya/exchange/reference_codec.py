"""Compatibility API for the DCC-independent legacy codec (v1 defaults retained)."""
import sys
from pathlib import Path
_tools = str(Path(__file__).resolve().parents[2])
if _tools not in sys.path:
    sys.path.insert(0, _tools)
from dcc_common.legacy_codec import *  # noqa: F401,F403,E402
