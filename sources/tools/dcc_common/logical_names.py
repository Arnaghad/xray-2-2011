"""Logical resource naming shared by DCC frontends."""

from pathlib import Path
from typing import Iterable


def make_logical_name(output_path: str | Path, category: str = "models") -> str:
    """Derive forward-slash X-Ray logical resource name from an output path."""
    norm = str(output_path).replace("\\", "/").strip("/")
    for suffix in (".model", ".skinned_model", ".skeleton", ".clip", ".physics", ".ph"):
        if norm.endswith(suffix):
            norm = norm[: -len(suffix)]
            break

    markers: Iterable[str] = (
        f"/resources/exported/{category}/",
        f"/resources/sources/{category}/",
        f"/resources/{category}/",
        f"/{category}/",
    )
    low = norm.lower()
    for marker in markers:
        idx = low.rfind(marker)
        if idx >= 0:
            rel = norm[idx + len(marker) :].strip("/")
            if rel:
                return rel

    stem = Path(norm).name or "unnamed"
    return stem

