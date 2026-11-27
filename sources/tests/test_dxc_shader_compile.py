"""Verify HLSL -> SPIR-V shader compilation through xray_shader_compiler + DXC."""

from __future__ import annotations

import argparse
import os
import struct
import subprocess
import tempfile
from pathlib import Path

SPIRV_MAGIC = 0x07230203


def compile_shader(
    compiler: Path,
    env: dict[str, str],
    hlsl_path: Path,
    spv_path: Path,
    stage: str,
    entry: str,
    defines: tuple[str, ...] = (),
    flags: tuple[str, ...] = (),
) -> bytes:
    cmd = [str(compiler), str(hlsl_path), str(spv_path), stage, f"--entry={entry}"]
    for define in defines:
        cmd.append(f"--define={define}")
    cmd.extend(flags)

    proc = subprocess.run(
        cmd,
        env=env,
        capture_output=True,
        text=True,
    )
    assert proc.returncode == 0, f"Compile failed for {hlsl_path} ({entry}):\n{proc.stdout}\n{proc.stderr}"
    data = spv_path.read_bytes()
    assert len(data) >= 20 and len(data) % 4 == 0, f"Invalid SPIR-V size for {spv_path}: {len(data)}"
    magic = struct.unpack_from("<I", data, 0)[0]
    assert magic == SPIRV_MAGIC, f"Bad SPIR-V magic in {spv_path}: {magic:#x}"
    return data


def locate_dxc_artifacts(dxc_dir: Path) -> tuple[Path | None, Path | None]:
    if os.name == "nt":
        lib = dxc_dir / "dxcompiler.dll"
        cli = dxc_dir / "dxc.exe"
    else:
        lib = dxc_dir / "lib" / "libdxcompiler.so"
        if not lib.is_file():
            lib = dxc_dir / "libdxcompiler.so"
        cli = dxc_dir / "bin" / "dxc"
        if not cli.is_file():
            cli = dxc_dir / "dxc"
    return (lib.resolve() if lib.is_file() else None, cli.resolve() if cli.is_file() else None)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--compiler", required=True)
    parser.add_argument("--shader-dir", required=True)
    parser.add_argument("--dxc-dir", required=True)
    args = parser.parse_args()

    compiler = Path(args.compiler)
    shader_dir = Path(args.shader_dir)
    dxc_dir = Path(args.dxc_dir)
    dxc_lib, dxc_cli = locate_dxc_artifacts(dxc_dir)
    assert compiler.is_file(), f"Missing shader compiler: {compiler}"
    assert dxc_lib is not None, f"Missing DXC shared library in {dxc_dir}"

    base_env = os.environ.copy()
    if os.name == "nt":
        base_env["PATH"] = f"{dxc_dir};{base_env.get('PATH', '')}"
    else:
        lib_dir = dxc_lib.parent
        base_env["LD_LIBRARY_PATH"] = f"{lib_dir}:{base_env.get('LD_LIBRARY_PATH', '')}"
        if dxc_cli is not None:
            base_env["PATH"] = f"{dxc_cli.parent}:{base_env.get('PATH', '')}"

    lib_env = dict(base_env)
    lib_env["XRAY_DXC_BACKEND"] = "library"
    lib_env["XRAY_DXC_PATH"] = str(dxc_lib)
    lib_env["XRAY_SDK_DISABLE_COOKED_CACHE"] = "1"

    # Every .hlsl shader under sources/xray/render/vulkan/assets/ and basic/
    shader_cases: list[tuple[str, str, str, tuple[str, ...], tuple[str, ...]]] = [
        ("skinning.comp.hlsl", "compute", "skin_cs", (), ()),
        ("bloom.hlsl", "pixel", "extract_ps", (), ()),
        ("bloom.hlsl", "pixel", "blur_ps", (), ()),
        ("bloom.hlsl", "pixel", "composite_ps", (), ()),
        ("debug_primitives.hlsl", "vertex", "debug_vs", (), ("-fvk-invert-y",)),
        ("debug_primitives.hlsl", "pixel", "debug_ps", (), ()),
        ("decals.hlsl", "pixel", "decal_ps", ("USE_BINDLESS=0",), ()),
        ("deferred_lighting.hlsl", "pixel", "lighting_ps", ("SAMPLE_COUNT=1", "USE_SCENE_SHADOW=1"), ()),
        ("deferred_lighting.hlsl", "pixel", "environment_ps", ("SAMPLE_COUNT=1", "USE_SSAO=1"), ()),
        ("deferred_lighting.hlsl", "pixel", "combine_ps", ("SAMPLE_COUNT=1",), ()),
        ("distortion.hlsl", "pixel", "distortion_ps", (), ()),
        ("distortion_composite.hlsl", "pixel", "composite_ps", (), ()),
        ("editor_diagnostic.hlsl", "vertex", "diagnostic_vs", (), ("-fvk-invert-y",)),
        ("editor_diagnostic.hlsl", "pixel", "diagnostic_ps", (), ()),
        ("editor_selection.hlsl", "vertex", "selection_vs", (), ("-fvk-invert-y",)),
        ("editor_selection.hlsl", "pixel", "selection_ps", (), ()),
        ("fxaa.hlsl", "pixel", "fxaa_ps", (), ()),
        ("fxaa.hlsl", "pixel", "tonemap_encode_ps", (), ()),
        ("gbuffer_preview.hlsl", "vertex", "preview_vs", (), ()),
        ("gbuffer_preview.hlsl", "pixel", "preview_ps", (), ()),
        ("gbuffer_preview.hlsl", "pixel", "stencil_ps", (), ()),
        ("particle_billboards.hlsl", "vertex", "particle_vs", (), ()),
        ("particle_billboards.hlsl", "pixel", "particle_ps", (), ()),
        ("skybox.hlsl", "vertex", "sky_vs", (), ()),
        ("skybox.hlsl", "pixel", "sky_ps", (), ()),
        ("ssao.hlsl", "pixel", "ssao_ps", (), ()),
        ("ssao.hlsl", "pixel", "ssao_filter_ps", (), ()),
        ("static_forward.hlsl", "vertex", "forward_vs", (), ()),
        ("static_forward.hlsl", "pixel", "forward_ps", (), ()),
        ("static_gbuffer.hlsl", "vertex", "gbuffer_vs", (), ("-fvk-invert-y",)),
        ("static_gbuffer.hlsl", "pixel", "gbuffer_ps", (), ()),
        ("static_material.hlsl", "vertex", "material_vs", (), ()),
        ("static_material.hlsl", "pixel", "material_ps", (), ()),
        ("static_opaque.hlsl", "vertex", "static_vs", (), ()),
        ("static_opaque.hlsl", "pixel", "static_ps", (), ()),
        ("static_opaque.hlsl", "pixel", "mask_ps", (), ()),
        ("static_shadow.hlsl", "pixel", "shadow_ps", (), ()),
        ("taa.hlsl", "pixel", "taa_ps", ("SAMPLE_COUNT=1",), ()),
        ("terrain_deferred.hlsl", "vertex", "terrain_deferred_vs", ("TERRAIN_MATERIAL=0",), ("-fvk-invert-y",)),
        ("terrain_deferred.hlsl", "pixel", "terrain_gbuffer_ps", ("TERRAIN_MATERIAL=0",), ()),
        ("terrain_deferred.hlsl", "vertex", "terrain_material_vs", ("TERRAIN_MATERIAL=1",), ("-fvk-invert-y",)),
        ("terrain_deferred.hlsl", "pixel", "terrain_material_ps", ("TERRAIN_MATERIAL=1",), ()),
        ("terrain_unlit.hlsl", "vertex", "terrain_vs", (), ("-fvk-invert-y",)),
        ("terrain_unlit.hlsl", "pixel", "terrain_ps", (), ()),
        ("ui.hlsl", "vertex", "ui_vs", (), ("-fvk-invert-y",)),
        ("ui.hlsl", "pixel", "ui_ps", ("UI_MODE=0",), ()),
        ("basic/geometry.hlsl", "vertex", "triangle_vs", (), ()),
        ("basic/geometry.hlsl", "vertex", "mesh_vs", (), ()),
        ("basic/geometry.hlsl", "pixel", "color_ps", (), ()),
        ("basic/materials.hlsl", "vertex", "material_vs", (), ()),
        ("basic/materials.hlsl", "pixel", "material_ps", (), ()),
    ]

    discovered_hlsl = {
        p.relative_to(shader_dir).as_posix()
        for p in shader_dir.rglob("*.hlsl")
    }
    covered_hlsl = {case[0] for case in shader_cases}
    missing = sorted(discovered_hlsl - covered_hlsl)
    assert not missing, f"Uncovered .hlsl files in {shader_dir}: {missing}"

    compiled_blobs: dict[tuple[str, str], bytes] = {}
    with tempfile.TemporaryDirectory(prefix="xray_dxc_test_") as tmp:
        out = Path(tmp)
        for idx, (rel, stage, entry, defines, flags) in enumerate(shader_cases):
            spv_file = out / f"{idx}_{Path(rel).stem}_{entry}.spv"
            data = compile_shader(
                compiler,
                lib_env,
                shader_dir / rel,
                spv_file,
                stage,
                entry,
                defines=defines,
                flags=flags,
            )
            compiled_blobs[(rel, entry)] = data

        # Parity check between dxc_library_backend and dxc_process_backend
        if dxc_cli is not None:
            proc_env = dict(base_env)
            proc_env["XRAY_DXC_BACKEND"] = "process"
            proc_env["XRAY_DXC_CLI_PATH"] = str(dxc_cli)
            proc_env["XRAY_SDK_DISABLE_COOKED_CACHE"] = "1"
            for rel, stage, entry, flags in (
                ("skinning.comp.hlsl", "compute", "skin_cs", ()),
                ("static_gbuffer.hlsl", "vertex", "gbuffer_vs", ("-fvk-invert-y",)),
                ("fxaa.hlsl", "pixel", "fxaa_ps", ()),
                ("basic/geometry.hlsl", "pixel", "color_ps", ()),
            ):
                cli_spv = compile_shader(
                    compiler,
                    proc_env,
                    shader_dir / rel,
                    out / f"cli_{Path(rel).stem}_{entry}.spv",
                    stage,
                    entry,
                    flags=flags,
                )
                assert cli_spv == compiled_blobs[(rel, entry)], (
                    f"SPIR-V mismatch between library and CLI backend for {rel} ({entry})"
                )

        # Disk cache hit test without live DXC
        cache_dir = out / "sdk_user_data"
        cache_write_env = dict(base_env)
        cache_write_env["XRAY_DXC_BACKEND"] = "library"
        cache_write_env["XRAY_DXC_PATH"] = str(dxc_lib)
        cache_write_env["XRAY_SDK_USER_DATA"] = str(cache_dir)
        cache_write_env.pop("XRAY_SDK_DISABLE_COOKED_CACHE", None)

        uncached = compile_shader(
            compiler,
            cache_write_env,
            shader_dir / "fxaa.hlsl",
            out / "fxaa_cached_write.spv",
            "pixel",
            "fxaa_ps",
        )

        cache_read_env = dict(base_env)
        cache_read_env["XRAY_DXC_BACKEND"] = "library"
        cache_read_env["XRAY_DXC_PATH"] = "/nonexistent/missing_dxcompiler"
        cache_read_env["XRAY_SDK_USER_DATA"] = str(cache_dir)
        cache_read_env.pop("XRAY_SDK_DISABLE_COOKED_CACHE", None)

        from_cache = compile_shader(
            compiler,
            cache_read_env,
            shader_dir / "fxaa.hlsl",
            out / "fxaa_cached_read.spv",
            "pixel",
            "fxaa_ps",
        )
        assert from_cache == uncached, "Disk-cached SPIR-V mismatch"

    print(f"PASS: xray_dxc_shader_compile ({len(discovered_hlsl)} hlsl files, {len(shader_cases)} entry points)")


if __name__ == "__main__":
    main()
