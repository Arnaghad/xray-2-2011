"""Stage 06 DCC compatibility tests: Exchange v1/v2, editable curves, 128-bone skinning, and legacy readback."""

from __future__ import annotations

import argparse
import json
import math
import subprocess
import sys
import tempfile
from pathlib import Path


def run_cli(cook_cli: Path, args: list[str], *, expect_ok: bool = True) -> subprocess.CompletedProcess[str]:
    proc = subprocess.run([str(cook_cli), *args], capture_output=True, text=True)
    if expect_ok and proc.returncode != 0:
        raise AssertionError(
            f"{cook_cli.name} {' '.join(args)} failed (rc={proc.returncode}):\n{proc.stdout}\n{proc.stderr}"
        )
    if not expect_ok and proc.returncode == 0:
        raise AssertionError(
            f"{cook_cli.name} {' '.join(args)} unexpectedly succeeded:\n{proc.stdout}\n{proc.stderr}"
        )
    return proc


def verify_v1_static_and_physics(cook_cli: Path, root: Path, exchange_mod, codec_mod) -> None:
    ExchangeBundleWriter = exchange_mod.ExchangeBundleWriter
    validate_bundle = exchange_mod.validate_bundle

    # 1a. v1 static_model cook + archive readback
    v1_static_dir = root / "v1_static_bundle"
    w = ExchangeBundleWriter(
        asset_kind="static_model",
        name="tests/v1_box",
        coord_space={
            "system": "maya_right_handed_y_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw_maya",
        },
    )
    positions = [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [1.0, 1.0, 0.0]]
    normals = [[0.0, 0.0, 1.0]] * 4
    uvs = [[0.0, 0.0], [1.0, 0.0], [0.0, 1.0], [1.0, 1.0]]
    indices = [0, 1, 2, 2, 1, 3]
    w.manifest["surfaces"] = [{
        "name": "body",
        "material": "default",
        "vertex_count": 4,
        "index_count": 6,
        "streams": {
            "positions": w.append_span("f32x3", positions),
            "normals": w.append_span("f32x3", normals),
            "uvs": w.append_span("f32x2", uvs),
            "indices": w.append_span("u16", indices),
        },
    }]
    w.write(v1_static_dir, validate=True)
    assert validate_bundle(v1_static_dir)["schema"] == "xray-maya-exchange/v1"

    v1_model_archive = root / "v1_box.model"
    run_cli(cook_cli, [
        "--input", str(v1_static_dir),
        "--output", str(root / "v1_static_raw"),
        "--converted-output", str(root / "v1_static_conv"),
        "--pack-archive", str(v1_model_archive),
    ])
    members = codec_mod.read_vfs_archive(v1_model_archive.read_bytes())
    assert "render/export_properties" in members
    assert "render/body/vertices" in members
    assert "render/body/indices" in members

    # 1b. v1 physics fit_requests (--fit-collision returns "maya" + "xray")
    v1_fit_dir = root / "v1_fit_bundle"
    wf = ExchangeBundleWriter(
        asset_kind="physics",
        name="tests/v1_fit",
        coord_space={
            "system": "maya_right_handed_y_up",
            "unit_scale_to_meters": 0.01,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw_maya",
        },
    )
    pts = [[-50.0, -25.0, -10.0], [50.0, -25.0, -10.0], [50.0, 25.0, 10.0], [-50.0, 25.0, 10.0],
           [-50.0, -25.0, 10.0], [50.0, -25.0, 10.0], [50.0, 25.0, -10.0], [-50.0, 25.0, -10.0]]
    wf.manifest["fit_requests"] = [{
        "name": "box_req",
        "shape": "box",
        "points": wf.append_span("f32x3", pts),
    }]
    wf.write(v1_fit_dir, validate=True)
    fit_out = root / "v1_fit.json"
    run_cli(cook_cli, ["--input", str(v1_fit_dir), "--fit-collision", "--output", str(fit_out)])
    fit_data = json.loads(fit_out.read_text(encoding="utf-8"))
    results = fit_data if isinstance(fit_data, list) else fit_data.get("results", [])
    assert results and "maya" in results[0] and "xray" in results[0], f"Unexpected fit JSON: {fit_data!r}"


def build_128_bone_tree() -> dict:
    # 1 root + 7 arms with chains keeps depth <= 20 (< 64 max depth) and total bones == 128.
    children = []
    idx = 1
    chains = 7
    while idx < 128:
        chain_len = min(18 if idx == 1 else 19, 128 - idx)
        # Keep chaining until we allocate remaining bones across branches
        if len(children) < chains - 1:
            take = min(18, 128 - idx)
        else:
            take = 128 - idx
        head = {"name": f"bone_{idx:03d}", "children": []}
        cur = head
        idx += 1
        for _ in range(take - 1):
            nxt = {"name": f"bone_{idx:03d}", "children": []}
            cur["children"].append(nxt)
            cur = nxt
            idx += 1
        children.append(head)
    return {"name": "bone_000", "children": children}


def verify_v2_128_bone_skeleton_and_skin(cook_cli: Path, root: Path, exchange_mod, codec_mod) -> None:
    DccExchangeBundleWriter = exchange_mod.DccExchangeBundleWriter
    ExchangeValidationError = exchange_mod.ExchangeValidationError
    validate_bundle = exchange_mod.validate_bundle

    # 2a. 128-bone skeleton
    skel_dir = root / "v2_skel_128"
    ws = DccExchangeBundleWriter(
        asset_kind="skeleton",
        name="tests/skel_128",
        coord_space={
            "system": "blender_right_handed_z_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        },
        source_dcc="blender",
    )
    ws.manifest["root"] = build_128_bone_tree()
    ws.write(skel_dir, validate=True)
    assert validate_bundle(skel_dir)["schema"] == "xray-dcc-exchange/v2"

    skel_raw = root / "v2_skel_raw"
    skel_conv = root / "v2_skel_conv"
    skel_proc = run_cli(cook_cli, [
        "--input", str(skel_dir),
        "--output", str(skel_raw),
        "--converted-output", str(skel_conv),
    ])
    assert "bones=128" in skel_proc.stdout, f"Expected bones=128 in stdout: {skel_proc.stdout}"
    cooked_skel_files = list(skel_raw.rglob("*.skeleton"))
    assert cooked_skel_files, f"Expected .skeleton file in {skel_raw}"
    skel_text = cooked_skel_files[0].read_text(encoding="utf-8")
    assert "bone_000" in skel_text and "bone_127" in skel_text
    assert list(skel_conv.rglob("*.skeleton")), "Expected binary converted .skeleton"

    # 2b. 128-bone skinned model referencing every bone 0..127 with 4 weights/vert
    skin_dir = root / "v2_skin_128"
    wk = DccExchangeBundleWriter(
        asset_kind="skinned_model",
        name="tests/skin_128",
        coord_space={
            "system": "blender_right_handed_z_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        },
        source_dcc="blender",
    )
    wk.manifest["skeleton_name"] = "tests/skel_128"
    wk.manifest["bones"] = [
        {
            "name": f"bone_{i:03d}",
            "bind_translation": [0.05 * i, 0.0, 0.0],
            "bind_rotation": [0.0, 0.0, 0.0],
            "bind_scale": [1.0, 1.0, 1.0],
        }
        for i in range(128)
    ]

    # Build 128 triangles (384 vertices) so every bone index 0..127 appears in normalized 4-weight vertices
    positions = []
    normals = []
    uvs = []
    bone_indices = []
    bone_weights = []
    indices = []
    for b in range(128):
        x0 = float(b) * 0.1
        positions.extend([[x0, 0.0, 0.0], [x0 + 0.05, 0.0, 0.0], [x0, 0.05, 0.0]])
        normals.extend([[0.0, 0.0, 1.0]] * 3)
        uvs.extend([[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]])
        b1 = (b + 1) % 128
        b2 = (b + 2) % 128
        b3 = (b + 3) % 128
        for _ in range(3):
            bone_indices.append([b, b1, b2, b3])
            bone_weights.append([0.55, 0.25, 0.15, 0.05])
        base = b * 3
        indices.extend([base, base + 1, base + 2])

    wk.manifest["surfaces"] = [{
        "name": "skin",
        "material": "default",
        "vertex_count": len(positions),
        "index_count": len(indices),
        "streams": {
            "positions": wk.append_span("f32x3", positions),
            "normals": wk.append_span("f32x3", normals),
            "uvs": wk.append_span("f32x2", uvs),
            "bone_indices": wk.append_span("u16x4", bone_indices),
            "bone_weights": wk.append_span("f32x4", bone_weights),
            "indices": wk.append_span("u16", indices),
        },
    }]
    wk.write(skin_dir, validate=True)

    skin_archive = root / "skin_128.skinned_model"
    skin_proc = run_cli(cook_cli, [
        "--input", str(skin_dir),
        "--output", str(root / "v2_skin_raw"),
        "--converted-output", str(root / "v2_skin_conv"),
        "--pack-archive", str(skin_archive),
    ])
    assert "bones=128" in skin_proc.stdout, f"Expected bones=128 in stdout: {skin_proc.stdout}"
    members = codec_mod.read_vfs_archive(skin_archive.read_bytes())
    for required in ("render/export_properties", "render/bind_pose", "render/skin/vertices", "render/skin/indices", "skeleton"):
        assert required in members, f"Missing {required!r} in skinned archive: {sorted(members)}"

    # Negative control: 129 bones must fail validation AND CLI cook
    bad_skin_dir = root / "v2_skin_129_bad"
    wb = DccExchangeBundleWriter(
        asset_kind="skinned_model",
        name="tests/skin_129_bad",
        coord_space={
            "system": "blender_right_handed_z_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        },
        source_dcc="blender",
    )
    wb.manifest["skeleton_name"] = "tests/skel_128"
    wb.manifest["bones"] = wk.manifest["bones"] + [{
        "name": "bone_128",
        "bind_translation": [0.0, 0.0, 0.0],
        "bind_rotation": [0.0, 0.0, 0.0],
        "bind_scale": [1.0, 1.0, 1.0],
    }]
    wb.manifest["surfaces"] = [{
        "name": "skin",
        "material": "default",
        "vertex_count": 3,
        "index_count": 3,
        "streams": {
            "positions": wb.append_span("f32x3", positions[:3]),
            "normals": wb.append_span("f32x3", normals[:3]),
            "uvs": wb.append_span("f32x2", uvs[:3]),
            "bone_indices": wb.append_span("u16x4", bone_indices[:3]),
            "bone_weights": wb.append_span("f32x4", bone_weights[:3]),
            "indices": wb.append_span("u16", [0, 1, 2]),
        },
    }]
    wb.write(bad_skin_dir, validate=False)
    try:
        validate_bundle(bad_skin_dir)
        raise AssertionError("129-bone skinned_model passed Python validation unexpectedly")
    except ExchangeValidationError:
        pass
    run_cli(cook_cli, ["--input", str(bad_skin_dir), "--output", str(root / "v2_skin_129_out")], expect_ok=False)


def verify_v2_animation_and_curves(cook_cli: Path, root: Path, exchange_mod) -> None:
    DccExchangeBundleWriter = exchange_mod.DccExchangeBundleWriter
    ExchangeValidationError = exchange_mod.ExchangeValidationError
    validate_bundle = exchange_mod.validate_bundle

    anim_dir = root / "v2_anim_curves"
    wa = DccExchangeBundleWriter(
        asset_kind="animation",
        name="tests/anim_curves",
        coord_space={
            "system": "blender_right_handed_z_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        },
        source_dcc="blender",
    )
    frame_count = 16
    wa.manifest["fps"] = 30.0
    wa.manifest["frame_start"] = 10.0
    wa.manifest["frame_count"] = frame_count
    wa.manifest["root_already_relative"] = False

    t_vals = [float(i) / (frame_count - 1) for i in range(frame_count)]
    zeros = [0.0] * frame_count
    ones = [1.0] * frame_count
    rx_vals = [0.25 * math.sin(2.0 * math.pi * t) for t in t_vals]
    wa.manifest["tracks"] = [
        {
            "bone_name": "bone_000",
            "channels": {
                "tx": wa.append_span("f32", t_vals),
                "ty": wa.append_span("f32", zeros),
                "tz": wa.append_span("f32", zeros),
                "rx": wa.append_span("f32", zeros),
                "ry": wa.append_span("f32", zeros),
                "rz": wa.append_span("f32", zeros),
                "sx": wa.append_span("f32", ones),
                "sy": wa.append_span("f32", ones),
                "sz": wa.append_span("f32", ones),
            },
        },
        {
            "bone_name": "bone_001",
            "channels": {
                "tx": wa.append_span("f32", zeros),
                "ty": wa.append_span("f32", t_vals),
                "tz": wa.append_span("f32", zeros),
                "rx": wa.append_span("f32", zeros),
                "ry": wa.append_span("f32", zeros),
                "rz": wa.append_span("f32", zeros),
                "sx": wa.append_span("f32", ones),
                "sy": wa.append_span("f32", ones),
                "sz": wa.append_span("f32", ones),
            },
        },
    ]
    wa.manifest["curves"] = {
        "time_unit": "frames",
        "tracks": [
            {
                "bone_name": "bone_000",
                "rotation_order": "XYZ",
                "channels": {
                    "rx": {
                        "pre_infinity": "CONSTANT",
                        "post_infinity": "CYCLE",
                        "keys": [
                            {
                                "time": 10.0,
                                "value": 0.0,
                                "interpolation": "BEZIER",
                                "handle_left": [9.0, -0.05],
                                "handle_right": [11.0, 0.05],
                            },
                            {
                                "time": 18.0,
                                "value": 0.25,
                                "interpolation": "LINEAR",
                            },
                            {
                                "time": 25.0,
                                "value": 0.0,
                                "interpolation": "CONSTANT",
                            },
                        ],
                    }
                },
            }
        ],
    }
    wa.write(anim_dir, validate=True)
    assert validate_bundle(anim_dir)["schema"] == "xray-dcc-exchange/v2"

    anim_raw = root / "v2_anim_raw"
    anim_conv = root / "v2_anim_conv"
    run_cli(cook_cli, [
        "--input", str(anim_dir),
        "--output", str(anim_raw),
        "--converted-output", str(anim_conv),
    ])
    clip_files = list(anim_raw.rglob("*.clip"))
    assert clip_files, f"Expected cooked .clip in {anim_raw}"
    assert list(anim_conv.rglob("*.clip")), f"Expected binary converted .clip in {anim_conv}"

    # Emit expected engine-space .clip_samples (Blender Z-up pos (x,y,z) -> engine (x,z,y))
    # next to the cooked .clip and verify B-spline readback via xray_asset_cook_cli --clip-samples.
    samples_path = clip_files[0].with_suffix(".clip_samples")
    lines = [f"2 {frame_count} 1.0 0", "bone_000"]
    for t in t_vals:
        lines.append(f"{t:.6f} 0.0 0.0 0.0 0.0 0.0 1.0 1.0 1.0")
    lines.append("bone_001")
    for t in t_vals:
        lines.append(f"0.0 0.0 {t:.6f} 0.0 0.0 0.0 1.0 1.0 1.0")
    samples_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    run_cli(cook_cli, ["--clip-samples", str(samples_path)])

    # Negative control: invalid BEZIER handle side rejected by Python and C++
    bad_anim_dir = root / "v2_anim_bad_curve"
    wa.manifest["curves"]["tracks"][0]["channels"]["rx"]["keys"][0]["handle_left"] = [10.5, 0.0]
    wa.write(bad_anim_dir, validate=False)
    try:
        validate_bundle(bad_anim_dir)
        raise AssertionError("Invalid curve handle side passed Python validation")
    except ExchangeValidationError:
        pass
    run_cli(cook_cli, ["--input", str(bad_anim_dir), "--output", str(root / "v2_anim_bad_out")], expect_ok=False)


def verify_legacy_readback(repo_root: Path) -> None:
    lock_path = repo_root / "sources/tools/blender/legacy-corpus-lock.json"
    lock_data = json.loads(lock_path.read_text(encoding="utf-8"))
    assert lock_data.get("schema") == "xray-legacy-corpus-lock/v1"
    assert lock_data.get("summary", {}).get("assets") == 3836

    for rel in (
        "build/sdk-assets/sources/projects/alex/project.xprj",
        "build/sdk-assets/sources/projects/dima_light_test/project.xprj",
    ):
        p = repo_root / rel
        if p.is_file():
            text = p.read_text(encoding="utf-8", errors="ignore")
            assert "folders" in text and "objects" in text, f"Legacy .xprj readback invalid: {p}"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cook-cli", required=True)
    parser.add_argument("--repo-root", required=True)
    args = parser.parse_args()

    cook_cli = Path(args.cook_cli).resolve()
    repo_root = Path(args.repo_root).resolve()
    sys.path.insert(0, str(repo_root / "sources/tools"))

    from dcc_common import exchange as exchange_mod  # noqa: E402
    from dcc_common import legacy_codec as codec_mod  # noqa: E402

    with tempfile.TemporaryDirectory(prefix="xray_dcc_v1_v2_") as tmp:
        root = Path(tmp)
        verify_v1_static_and_physics(cook_cli, root, exchange_mod, codec_mod)
        verify_v2_128_bone_skeleton_and_skin(cook_cli, root, exchange_mod, codec_mod)
        verify_v2_animation_and_curves(cook_cli, root, exchange_mod)
    verify_legacy_readback(repo_root)
    print("PASS: xray_dcc_v1_v2_compatibility_contract")


if __name__ == "__main__":
    main()
