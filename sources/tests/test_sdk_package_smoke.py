"""Verify SDK packaging, PE import audit, and MSYS2-free packaged CLI execution."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    parser.add_argument("--build-dir", required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--dxc-dir", required=True)
    parser.add_argument("--validation-dir", required=True)
    parser.add_argument("--assets-root", required=True)
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()
    packager = repo_root / "sources/tools/sdk/package-sdk-runtime.py"
    cfg = args.config.lower()
    if cfg not in {"debug", "release"}:
        raise ValueError(f"Unsupported config: {args.config}")

    mingw_bin = Path(shutil.which("objdump") or "C:/msys64/mingw64/bin/objdump.exe").resolve().parent

    with tempfile.TemporaryDirectory(prefix="xray_sdk_pkg_smoke_") as tmp:
        pkg_dir = Path(tmp) / "pkg"
        proc = subprocess.run(
            [
                sys.executable,
                str(packager),
                "--config", cfg,
                "--build-dir", args.build_dir,
                "--output", str(pkg_dir),
                "--mingw-bin", str(mingw_bin),
                "--dxc-dir", args.dxc_dir,
                "--validation-dir", args.validation_dir,
                "--assets-root", args.assets_root,
                "--clean",
            ],
            capture_output=True,
            text=True,
        )
        assert proc.returncode == 0, f"Packaging failed:\n{proc.stdout}\n{proc.stderr}"

        manifest = json.loads((pkg_dir / "package-manifest.json").read_text(encoding="utf-8"))
        assert manifest.get("peImportAuditPassed") is True
        assert manifest.get("peAuditedBinaries", 0) > 0

        # Verify .NET 11 runtimeconfig.json and hostfxr presence.
        runtime_cfg_path = pkg_dir / "bin/xray.editor.base.runtimeconfig.json"
        assert runtime_cfg_path.is_file(), f"Missing runtimeconfig.json: {runtime_cfg_path}"
        runtime_cfg = json.loads(runtime_cfg_path.read_text(encoding="utf-8-sig"))
        opts = runtime_cfg.get("runtimeOptions", {})
        assert opts.get("tfm") == "net11.0", f"Unexpected runtimeconfig tfm: {opts!r}"
        assert opts.get("rollForward") == "LatestPatch", f"Unexpected rollForward: {opts!r}"
        fw_entries = opts.get("frameworks")
        if fw_entries is None:
            fw_single = opts.get("framework")
            fw_entries = [fw_single] if isinstance(fw_single, dict) else []
        frameworks = {fw.get("name"): fw.get("version") for fw in fw_entries if isinstance(fw, dict)}
        assert frameworks.get("Microsoft.NETCore.App") == "11.0.0", f"Unexpected NETCore.App: {frameworks!r}"
        assert "Microsoft.WindowsDesktop.App" not in frameworks, (
            f"Pure Avalonia net11.0 host must not require Microsoft.WindowsDesktop.App: {frameworks!r}"
        )

        dotnet_root = Path(os.environ.get("DOTNET_ROOT", r"C:\Program Files\dotnet"))
        fxr_hits = list((dotnet_root / "host/fxr").glob("11.*/hostfxr.dll")) if (dotnet_root / "host/fxr").is_dir() else []
        assert fxr_hits, f"Missing .NET 11 hostfxr.dll under {dotnet_root / 'host/fxr'}"

        # Verify packaged binaries execute with PATH stripped of MSYS2.
        sys_root = os.environ.get("SystemRoot", r"C:\Windows")
        clean_env = os.environ.copy()
        clean_env["PATH"] = f"{sys_root}\\System32;{sys_root}"
        cook_exe = pkg_dir / "bin/xray_asset_cook_cli.exe"
        cook_proc = subprocess.run(
            [str(cook_exe), "--help"],
            cwd=str(pkg_dir / "bin"),
            env=clean_env,
            capture_output=True,
            text=True,
        )
        assert cook_proc.returncode == 0, (
            f"Packaged xray_asset_cook_cli failed without MSYS2 in PATH (rc={cook_proc.returncode}):\n"
            f"{cook_proc.stdout}\n{cook_proc.stderr}"
        )

        # Launch packaged EditorHost + Vulkan viewport in background capture mode:
        # open legacy .xprj, add folder + Undo/Redo, save + reload, and shut down cleanly.
        report_path = Path(tmp) / "stage06-smoke-report.json"
        cap_dir = Path(tmp) / "capture"
        val_dir = pkg_dir / "bin/vulkan-validation"
        editor_env = clean_env.copy()
        editor_env["PATH"] = f"{val_dir};{sys_root}\\System32;{sys_root}"
        editor_env["VK_LAYER_PATH"] = str(val_dir)
        editor_env["XRAY_VULKAN_REQUIRE_VALIDATION"] = "1"
        editor_env["DISABLE_RTSS_LAYER"] = "1"
        editor_env["DISABLE_VULKAN_OBS_CAPTURE"] = "1"
        editor_env["XRAY_SDK_STAGE06_SMOKE"] = "1"
        editor_env["XRAY_SDK_STAGE06_SMOKE_REPORT"] = str(report_path)

        stalker_exe = pkg_dir / "bin/stalker2.exe"
        try:
            editor_proc = subprocess.run(
                [
                    str(stalker_exe),
                    "-editor",
                    "-windowed",
                    "-level=vulkan_m8_control",
                    f"-render_assets={pkg_dir / 'assets'}",
                    f"-user_data_path={pkg_dir / 'user-data'}",
                    "-max_resources_size=4096",
                    f"-render_capture={cap_dir}",
                ],
                cwd=str(pkg_dir / "bin"),
                env=editor_env,
                capture_output=True,
                text=True,
                timeout=60,
            )
        except subprocess.TimeoutExpired as exc:
            report_text = report_path.read_text(encoding="utf-8", errors="replace") if report_path.is_file() else "<no report>"
            raise AssertionError(
                f"Packaged EditorHost smoke timed out after {exc.timeout}s.\n"
                f"report={report_text}\nstdout:\n{exc.stdout}\nstderr:\n{exc.stderr}"
            ) from exc
        assert editor_proc.returncode == 0, (
            f"Packaged EditorHost smoke failed (rc={editor_proc.returncode}):\n"
            f"{editor_proc.stdout}\n{editor_proc.stderr}"
        )
        assert report_path.is_file(), (
            f"Missing EditorHost smoke report: {report_path}\nstdout:\n{editor_proc.stdout}\nstderr:\n{editor_proc.stderr}"
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        assert report.get("ok") is True, f"EditorHost smoke reported error: {report!r}"
        assert report.get("openedProject") == "default_scene", f"Unexpected project: {report!r}"
        assert report.get("legacyProjectsVerified") == ["alex", "dima_light_test"], f"Unexpected legacy check: {report!r}"
        assert report.get("legacySaveReloadOk") is True, f"Legacy save/reload check failed: {report!r}"
        assert report.get("avaloniaUi6dEditorsOk") is True, f"Avalonia dialog/logic/navigation integration failed: {report!r}"
        assert report.get("avaloniaUi7WorkspacePanelsOk") is True, f"Avalonia inspector/curve workspace integration failed: {report!r}"
        assert report.get("avaloniaUi7ProjectTreeOk") is True, f"Avalonia project tree integration failed: {report!r}"
        assert report.get("avaloniaUi7ToolsAndSceneGraphOk") is True, f"Avalonia tools/scene graph integration failed: {report!r}"
        assert report.get("avaloniaUi7AuxiliaryToolsOk") is True, f"Avalonia auxiliary tools integration failed: {report!r}"
        assert report.get("avaloniaUi7SoundAndParticleEditorsOk") is True, f"Avalonia sound and particle editor integration failed: {report!r}"
        assert report.get("avaloniaUi7AnimationSetupAndGroupsOk") is True, f"Avalonia animation setup and groups integration failed: {report!r}"
        assert report.get("avaloniaUi7StandaloneEditorsAndNoWpfOk") is True, f"Avalonia standalone editors / no-WPF integration failed: {report!r}"
        assert report.get("avaloniaUi7IdeDialogsOk") is True, f"Avalonia IDE dialogs integration failed: {report!r}"
        assert report.get("avaloniaUi7ServicesAndCameraOk") is True, f"Avalonia services / CameraViewPanel check failed: {report!r}"
        assert report.get("avaloniaUi7PureAvaloniaShellOk") is True, f"Pure Avalonia shell check failed: {report!r}"
        for forbidden_bin in (
            "wpf_controls.dll",
            "Xceed.Wpf.Toolkit.dll",
            "WeifenLuo.WinFormsUI.Docking.dll",
            "WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll",
            "Avalonia.Win32.Interoperability.dll",
        ):
            assert not (pkg_dir / "bin" / forbidden_bin).exists(), f"{forbidden_bin} must not be packaged"
        for required_avalonia_bin in (
            "xray.editor.base.dll",
            "xray.editor.core.dll",
            "xray.editor.avalonia_controls.dll",
            "Avalonia.Base.dll",
            "Dock.Avalonia.dll",
            "SkiaSharp.dll",
            "runtimes/win-x64/native/libSkiaSharp.dll",
            "runtimes/win-x64/native/libHarfBuzzSharp.dll",
        ):
            assert (pkg_dir / "bin" / required_avalonia_bin).is_file(), f"Missing packaged Avalonia runtime file: {required_avalonia_bin}"
        assert report.get("initialRootChildren", 0) > 0, f"Empty loaded project tree: {report!r}"
        assert report.get("undoOk") is True and report.get("redoOk") is True, f"Undo/Redo check failed: {report!r}"
        assert report.get("reloadedHasFolder") is True, f"Save/Reload check failed: {report!r}"
        assert report.get("renderedFrames", 0) >= 6, f"Insufficient rendered frames: {report!r}"
        assert str(report.get("runtimeVersion", "")).startswith("11."), f"Unexpected CLR runtime: {report!r}"
        saved_xprj = Path(report["savedProjectPath"])
        assert saved_xprj.is_file(), f"Saved project file missing: {saved_xprj}"

    print("PASS: xray_sdk_package_smoke")


if __name__ == "__main__":
    main()
