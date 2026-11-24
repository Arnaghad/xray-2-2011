"""Stage 07 Vulkan benchmark and pixel-comparison harness (BEFORE vs AFTER)."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import time
from pathlib import Path
from typing import Any


VALIDATION_LINE_RE = re.compile(r"M8 validation errors=(\d+) warnings=(\d+)")


def percentile(sorted_vals: list[float], p: float) -> float:
    if not sorted_vals:
        return 0.0
    if len(sorted_vals) == 1:
        return sorted_vals[0]
    rank = (len(sorted_vals) - 1) * p
    lo = int(math.floor(rank))
    hi = int(math.ceil(rank))
    frac = rank - lo
    return sorted_vals[lo] * (1.0 - frac) + sorted_vals[hi] * frac


def read_bmp_bgra(path: Path) -> tuple[int, int, bytes]:
    data = path.read_bytes()
    if len(data) < 54 or data[:2] != b"BM":
        raise AssertionError(f"Not a valid BMP file: {path}")
    pixel_offset = struct.unpack_from("<I", data, 10)[0]
    width, height_signed, planes, bpp, compression = struct.unpack_from("<iiHHI", data, 18)
    if planes != 1 or bpp != 32 or compression != 0:
        raise AssertionError(f"Unsupported BMP format in {path}: planes={planes}, bpp={bpp}, comp={compression}")
    height = abs(height_signed)
    expected_bytes = width * height * 4
    pixels = data[pixel_offset : pixel_offset + expected_bytes]
    if len(pixels) != expected_bytes:
        raise AssertionError(f"Truncated BMP pixel payload in {path}: {len(pixels)} != {expected_bytes}")
    return width, height, pixels


def compare_bmp_pair(before_path: Path, after_path: Path, max_channel_delta: int, max_diff_ratio: float) -> dict[str, Any]:
    w1, h1, p1 = read_bmp_bgra(before_path)
    w2, h2, p2 = read_bmp_bgra(after_path)
    if (w1, h1) != (w2, h2):
        raise AssertionError(f"BMP dimensions changed for {before_path.name}: ({w1}x{h1}) vs ({w2}x{h2})")

    total_pixels = w1 * h1
    max_delta = 0
    diff_pixels = 0
    mv1 = memoryview(p1)
    mv2 = memoryview(p2)
    if p1 != p2:
        for off in range(0, len(p1), 4):
            d0 = abs(mv1[off] - mv2[off])
            d1 = abs(mv1[off + 1] - mv2[off + 1])
            d2 = abs(mv1[off + 2] - mv2[off + 2])
            d = d0 if d0 >= d1 and d0 >= d2 else (d1 if d1 >= d2 else d2)
            if d > max_delta:
                max_delta = d
            if d > 0:
                diff_pixels += 1

    diff_ratio = float(diff_pixels) / float(total_pixels) if total_pixels > 0 else 0.0
    if max_delta > max_channel_delta or diff_ratio > max_diff_ratio:
        raise AssertionError(
            f"Visual regression in {after_path.name}: max_delta={max_delta} (limit {max_channel_delta}), "
            f"diff_ratio={diff_ratio:.6f} (limit {max_diff_ratio:.6f})"
        )
    return {
        "width": w1,
        "height": h1,
        "maxDelta": max_delta,
        "diffPixels": diff_pixels,
        "diffRatio": diff_ratio,
        "beforeSha256": hashlib.sha256(p1).hexdigest(),
        "afterSha256": hashlib.sha256(p2).hexdigest(),
    }


def make_vulkan_env(build_dir: Path, repo_root: Path, validation_dir: Path) -> dict[str, str]:
    env = os.environ.copy()
    sys_root = env.get("SystemRoot", r"C:\Windows")
    dxc_dir = repo_root / "build/dxc/bin/x64"
    bins_dir = build_dir / "binaries"
    path_parts = [
        str(bins_dir),
        str(validation_dir),
        r"C:\msys64\mingw64\bin",
        f"{sys_root}\\System32",
        sys_root,
    ]
    if dxc_dir.is_dir():
        path_parts.insert(1, str(dxc_dir))
        env["XRAY_DXC_PATH"] = str(dxc_dir / "dxcompiler.dll")
    env["PATH"] = ";".join(path_parts)
    env["VK_LAYER_PATH"] = str(validation_dir)
    env["XRAY_VULKAN_REQUIRE_VALIDATION"] = "1"
    env["XRAY_VULKAN_PERFORMANCE"] = "1"
    env["DISABLE_RTSS_LAYER"] = "1"
    env["DISABLE_VULKAN_OBS_CAPTURE"] = "1"
    return env


def parse_validation_counts(stderr_text: str) -> tuple[int, int]:
    matches = VALIDATION_LINE_RE.findall(stderr_text)
    if not matches:
        raise AssertionError(f"Missing 'M8 validation errors=X warnings=Y' line in stderr:\n{stderr_text}")
    errors = sum(int(e) for e, _ in matches)
    warnings = sum(int(w) for _, w in matches)
    return errors, warnings


def run_benchmark_scenes(
    build_dir: Path,
    out_dir: Path,
    repo_root: Path,
    assets_root: Path,
    validation_dir: Path,
) -> dict[str, Any]:
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    stalker2 = build_dir / "binaries/stalker2.exe"
    if not stalker2.is_file():
        raise AssertionError(f"Missing stalker2.exe: {stalker2}")

    env = make_vulkan_env(build_dir, repo_root, validation_dir)
    game_dir = out_dir / "game"
    outputs_dir = out_dir / "outputs"

    # Scene A: Control scene with M9 warmup(2s)+stress(4s) timestamps + heap budget + final BMP
    t0 = time.monotonic()
    proc_a = subprocess.run(
        [
            str(stalker2),
            "-windowed",
            "-level=vulkan_m8_control",
            f"-render_assets={assets_root}",
            "-max_resources_size=4096",
            f"-render_capture={game_dir}",
            "-render_performance=2,4",
        ],
        cwd=str(repo_root),
        env=env,
        capture_output=True,
        text=True,
        timeout=120,
    )
    wall_a = time.monotonic() - t0
    if proc_a.returncode != 0:
        raise AssertionError(f"Scene A failed (rc={proc_a.returncode}):\n{proc_a.stdout}\n{proc_a.stderr}")
    err_a, warn_a = parse_validation_counts(proc_a.stderr)

    start_meta = json.loads((game_dir / "performance-start.json").read_text(encoding="utf-8"))
    complete_meta = json.loads((game_dir / "performance-complete.json").read_text(encoding="utf-8"))
    if not complete_meta.get("completed"):
        raise AssertionError(f"Scene A did not complete cleanly: {complete_meta}")

    cpu_ms: list[float] = []
    gpu_ms: list[float] = []
    device_peak = 0
    max_heap_usage = 0
    max_cached_passes = 0
    max_pass_pair_creations = 0
    stress_samples = 0

    for line in (game_dir / "performance.jsonl").read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        device_peak = max(device_peak, int(row.get("device_peak_bytes", 0)))
        max_cached_passes = max(max_cached_passes, int(row.get("cached_pass_objects", 0)))
        max_pass_pair_creations = max(max_pass_pair_creations, int(row.get("pass_pair_creations", 0)))
        for h in row.get("heaps", []):
            if h.get("local"):
                max_heap_usage = max(max_heap_usage, int(h.get("usage", 0)))
        if row.get("phase") == "stress" and row.get("outputs"):
            out0 = row["outputs"][0]
            cpu_ms.append(float(out0.get("cpu_ns", 0.0)) / 1_000_000.0)
            if out0.get("gpu_samples", 0) > 0:
                gpu_ms.append(float(out0.get("gpu_ns", 0.0)) / 1_000_000.0)
            stress_samples += 1

    if stress_samples == 0 or not cpu_ms or not gpu_ms:
        raise AssertionError(f"Scene A collected insufficient stress samples: stress={stress_samples}, gpu={len(gpu_ms)}")
    cpu_sorted = sorted(cpu_ms)
    gpu_sorted = sorted(gpu_ms)

    # Scene B: Editor outputs workflow (multi-viewport, resize/minimize, pending close)
    env_b = env.copy()
    env_b.pop("XRAY_VULKAN_PERFORMANCE", None)
    env_b["XRAY_M8_EDITOR_OUTPUT_WORKFLOW"] = "1"
    t1 = time.monotonic()
    proc_b = subprocess.run(
        [
            str(stalker2),
            "-editor",
            "-windowed",
            "-level=vulkan_m8_control",
            f"-render_assets={assets_root}",
            "-max_resources_size=4096",
            f"-render_capture={outputs_dir}",
        ],
        cwd=str(repo_root),
        env=env_b,
        capture_output=True,
        text=True,
        timeout=120,
    )
    wall_b = time.monotonic() - t1
    if proc_b.returncode != 0:
        raise AssertionError(f"Scene B failed (rc={proc_b.returncode}):\n{proc_b.stdout}\n{proc_b.stderr}")
    err_b, warn_b = parse_validation_counts(proc_b.stderr)

    bmp_files = {}
    for sub_name, sub_dir in (("game", game_dir), ("outputs", outputs_dir)):
        for bmp in sorted(sub_dir.glob("*.bmp")):
            w, h, pixels = read_bmp_bgra(bmp)
            rel = f"{sub_name}/{bmp.name}"
            bmp_files[rel] = {
                "width": w,
                "height": h,
                "sha256": hashlib.sha256(pixels).hexdigest(),
            }

    if not bmp_files:
        raise AssertionError("No BMP captures produced by Scene A / Scene B")

    return {
        "schemaVersion": 1,
        "buildDir": str(build_dir),
        "gpuName": start_meta.get("gpu_name", ""),
        "validationEnabled": bool(start_meta.get("validation_enabled")),
        "validationErrors": int(start_meta.get("validation_errors", 0)) + err_a + err_b,
        "validationWarnings": int(start_meta.get("validation_warnings", 0)) + warn_a + warn_b,
        "stressSamples": stress_samples,
        "cpuP50Ms": round(percentile(cpu_sorted, 0.50), 4),
        "cpuP95Ms": round(percentile(cpu_sorted, 0.95), 4),
        "gpuP50Ms": round(percentile(gpu_sorted, 0.50), 4),
        "gpuP95Ms": round(percentile(gpu_sorted, 0.95), 4),
        "coldPipelineCreations": int(start_meta.get("cold_pipeline_creations", 0)),
        "coldPipelineCreationMs": round(float(start_meta.get("cold_pipeline_creation_ns", 0)) / 1_000_000.0, 4),
        "devicePeakBytes": device_peak,
        "maxLocalHeapUsageBytes": max_heap_usage,
        "maxCachedPassObjects": max_cached_passes,
        "maxPassPairCreations": max_pass_pair_creations,
        "sceneAWallSec": round(wall_a, 3),
        "sceneBWallSec": round(wall_b, 3),
        "bmpFiles": bmp_files,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", required=True, choices=("baseline", "verify"))
    parser.add_argument("--build-dir", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--baseline", default="")
    parser.add_argument("--assets-root", default="build/sdk-assets")
    parser.add_argument("--validation-dir", default="build/vulkan-validation/mingw64/bin")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[3]
    build_dir = (repo_root / args.build_dir).resolve() if not Path(args.build_dir).is_absolute() else Path(args.build_dir).resolve()
    out_dir = (repo_root / args.output).resolve() if not Path(args.output).is_absolute() else Path(args.output).resolve()
    assets_root = (repo_root / args.assets_root).resolve() if not Path(args.assets_root).is_absolute() else Path(args.assets_root).resolve()
    validation_dir = (repo_root / args.validation_dir).resolve() if not Path(args.validation_dir).is_absolute() else Path(args.validation_dir).resolve()

    summary = run_benchmark_scenes(build_dir, out_dir, repo_root, assets_root, validation_dir)
    if summary["validationErrors"] != 0 or summary["validationWarnings"] != 0:
        raise AssertionError(
            f"Vulkan validation reported issues: errors={summary['validationErrors']}, warnings={summary['validationWarnings']}"
        )

    (out_dir / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    if args.mode == "baseline":
        thresholds = {
            "maxValidationErrors": 0,
            "maxValidationWarnings": 0,
            "maxPixelChannelDelta": 2,
            "maxDifferingPixelRatio": 0.001,
            "maxCpuP95Ms": round(max(summary["cpuP95Ms"] * 1.15, summary["cpuP95Ms"] + 0.50), 4),
            "maxGpuP95Ms": round(max(summary["gpuP95Ms"] * 1.15, summary["gpuP95Ms"] + 0.50), 4),
            "maxDevicePeakBytes": int(summary["devicePeakBytes"] * 1.05),
            "expectedAfterPassPairCreations": 0,
        }
        (out_dir / "thresholds.json").write_text(json.dumps(thresholds, indent=2) + "\n", encoding="utf-8")
        print(
            f"PASS: Stage 07 BEFORE baseline captured at {out_dir} "
            f"(cpuP95={summary['cpuP95Ms']}ms gpuP95={summary['gpuP95Ms']}ms "
            f"devicePeak={summary['devicePeakBytes']}B bmps={len(summary['bmpFiles'])})"
        )
        return

    if not args.baseline:
        raise AssertionError("--baseline is required for --mode verify")
    base_dir = (repo_root / args.baseline).resolve() if not Path(args.baseline).is_absolute() else Path(args.baseline).resolve()
    thresholds = json.loads((base_dir / "thresholds.json").read_text(encoding="utf-8"))
    before_summary = json.loads((base_dir / "summary.json").read_text(encoding="utf-8"))

    if summary["maxPassPairCreations"] != thresholds["expectedAfterPassPairCreations"]:
        raise AssertionError(
            f"Expected pass_pair_creations == {thresholds['expectedAfterPassPairCreations']}, got {summary['maxPassPairCreations']}"
        )
    if summary["cpuP95Ms"] > thresholds["maxCpuP95Ms"]:
        raise AssertionError(f"CPU p95 regression: {summary['cpuP95Ms']}ms > {thresholds['maxCpuP95Ms']}ms")
    if summary["gpuP95Ms"] > thresholds["maxGpuP95Ms"]:
        raise AssertionError(f"GPU p95 regression: {summary['gpuP95Ms']}ms > {thresholds['maxGpuP95Ms']}ms")
    if summary["devicePeakBytes"] > thresholds["maxDevicePeakBytes"]:
        raise AssertionError(f"Device peak memory regression: {summary['devicePeakBytes']}B > {thresholds['maxDevicePeakBytes']}B")

    before_bmps = set(before_summary["bmpFiles"].keys())
    after_bmps = set(summary["bmpFiles"].keys())
    if before_bmps != after_bmps:
        raise AssertionError(f"Captured BMP set differs: before_only={before_bmps - after_bmps}, after_only={after_bmps - before_bmps}")

    pixel_diffs = {}
    for rel in sorted(before_bmps):
        pixel_diffs[rel] = compare_bmp_pair(
            base_dir / rel,
            out_dir / rel,
            thresholds["maxPixelChannelDelta"],
            thresholds["maxDifferingPixelRatio"],
        )

    comparison = {
        "before": before_summary,
        "after": summary,
        "thresholds": thresholds,
        "pixelComparisons": pixel_diffs,
        "passed": True,
    }
    (out_dir / "comparison.json").write_text(json.dumps(comparison, indent=2) + "\n", encoding="utf-8")
    print(
        f"PASS: Stage 07 AFTER verified vs BEFORE "
        f"(cpuP95: {before_summary['cpuP95Ms']}ms -> {summary['cpuP95Ms']}ms, "
        f"gpuP95: {before_summary['gpuP95Ms']}ms -> {summary['gpuP95Ms']}ms, "
        f"devicePeak: {before_summary['devicePeakBytes']}B -> {summary['devicePeakBytes']}B, "
        f"passPairs: {before_summary['maxPassPairCreations']} -> {summary['maxPassPairCreations']}, "
        f"bmpsMatched={len(pixel_diffs)})"
    )


if __name__ == "__main__":
    main()
