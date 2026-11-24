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
) -> None:
    proc = subprocess.run(
        [str(compiler), str(hlsl_path), str(spv_path), stage, f"--entry={entry}"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert proc.returncode == 0, f"Compile failed for {hlsl_path} ({entry}):\n{proc.stdout}\n{proc.stderr}"
    data = spv_path.read_bytes()
    assert len(data) >= 20 and len(data) % 4 == 0, f"Invalid SPIR-V size for {spv_path}: {len(data)}"
    magic = struct.unpack_from("<I", data, 0)[0]
    assert magic == SPIRV_MAGIC, f"Bad SPIR-V magic in {spv_path}: {magic:#x}"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--compiler", required=True)
    parser.add_argument("--shader-dir", required=True)
    parser.add_argument("--dxc-dir", required=True)
    args = parser.parse_args()

    compiler = Path(args.compiler)
    shader_dir = Path(args.shader_dir)
    dxc_dir = Path(args.dxc_dir)
    dxc_dll = (dxc_dir / "dxcompiler.dll").resolve()
    assert compiler.is_file(), f"Missing shader compiler: {compiler}"
    assert dxc_dll.is_file(), f"Missing dxcompiler.dll in {dxc_dir}"

    env = os.environ.copy()
    env["PATH"] = f"{dxc_dir};{env.get('PATH', '')}"
    env["XRAY_DXC_PATH"] = str(dxc_dll)

    with tempfile.TemporaryDirectory(prefix="xray_dxc_test_") as tmp:
        out = Path(tmp)
        compile_shader(
            compiler,
            env,
            shader_dir / "skinning.comp.hlsl",
            out / "skinning.spv",
            "compute",
            "skin_cs",
        )
        compile_shader(
            compiler,
            env,
            shader_dir / "static_gbuffer.hlsl",
            out / "static_gbuffer_vs.spv",
            "vertex",
            "gbuffer_vs",
        )

    print("PASS: xray_dxc_shader_compile")


if __name__ == "__main__":
    main()
