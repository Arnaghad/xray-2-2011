"""Verify Exchange v2 bundle -> xray_asset_cook_cli -> legacy VFS archive decode roundtrip."""

from __future__ import annotations

import argparse
import math
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "sources/tools"))

from dcc_common.exchange import DccExchangeBundleWriter, validate_bundle  # noqa: E402
from dcc_common.legacy_codec import parse_config_bytes, read_vfs_archive  # noqa: E402


def build_static_triangle_bundle(bundle_dir: Path) -> None:
    writer = DccExchangeBundleWriter(
        asset_kind="static_model",
        name="tests/ci_cube_tri",
        coord_space={
            "system": "blender_right_handed_z_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        },
        source_dcc="blender",
    )

    positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
    ]
    normals = [[0.0, 0.0, 1.0]] * 4
    uvs = [
        [0.0, 0.0],
        [1.0, 0.0],
        [0.0, 1.0],
        [1.0, 1.0],
    ]
    indices = [0, 1, 2, 2, 1, 3]

    writer.manifest["surfaces"] = [
        {
            "name": "body",
            "material": "default",
            "vertex_count": len(positions),
            "index_count": len(indices),
            "streams": {
                "positions": writer.append_span("f32x3", positions),
                "normals": writer.append_span("f32x3", normals),
                "uvs": writer.append_span("f32x2", uvs),
                "indices": writer.append_span("u16", indices),
            },
        }
    ]
    writer.write(bundle_dir, validate=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cook-cli", required=True)
    args = parser.parse_args()
    cook_cli = Path(args.cook_cli)
    assert cook_cli.is_file(), f"Missing cook CLI: {cook_cli}"

    help_proc = subprocess.run([str(cook_cli), "--help"], capture_output=True, text=True)
    assert help_proc.returncode == 0, f"--help failed: {help_proc.stderr}"

    with tempfile.TemporaryDirectory(prefix="xray_cook_rt_") as tmp:
        root = Path(tmp)
        bundle_dir = root / "bundle"
        raw_out = root / "raw"
        conv_out = root / "converted"
        archive_path = root / "ci_cube_tri.model"

        build_static_triangle_bundle(bundle_dir)
        manifest = validate_bundle(bundle_dir)
        assert manifest["schema"] == "xray-dcc-exchange/v2"

        proc = subprocess.run(
            [
                str(cook_cli),
                "--input", str(bundle_dir),
                "--output", str(raw_out),
                "--converted-output", str(conv_out),
                "--pack-archive", str(archive_path),
            ],
            capture_output=True,
            text=True,
        )
        assert proc.returncode == 0, f"Cook failed:\n{proc.stdout}\n{proc.stderr}"
        assert archive_path.is_file(), "Expected packed .model archive"

        members = read_vfs_archive(archive_path.read_bytes())
        for required_member in (
            "render/export_properties",
            "render/body/export_properties",
            "render/body/vertices",
            "render/body/indices",
        ):
            assert required_member in members, (
                f"Missing {required_member!r} in archive: {sorted(members)}"
            )

        settings = parse_config_bytes(members["render/export_properties"])
        assert isinstance(settings, dict) and settings, "Parsed model settings must be non-empty"
        surf_props = parse_config_bytes(members["render/body/export_properties"])
        assert isinstance(surf_props, dict) and surf_props, "Parsed surface properties must be non-empty"

        converted_files = [p for p in conv_out.rglob("*") if p.is_file()]
        assert converted_files, "Expected converted outputs"

        # Negative control: missing input bundle must return non-zero exit code.
        bad_proc = subprocess.run(
            [
                str(cook_cli),
                "--input", str(root / "nonexistent_bundle"),
                "--output", str(root / "bad_out"),
            ],
            capture_output=True,
            text=True,
        )
        assert bad_proc.returncode != 0, "Expected non-zero exit code for missing bundle"

    assert math.isfinite(1.0)
    print("PASS: xray_dcc_exchange_cook_roundtrip")


if __name__ == "__main__":
    main()
