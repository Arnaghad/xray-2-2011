"""Verify repository source, preset, toolchain manifest, and .NET contracts."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


def check_no_bundled_lua(sources_dir: Path) -> None:
    forbidden = {"lua.h", "lauxlib.h", "lualib.h", "luaconf.h"}
    for sub in ("lua", "xray"):
        root = sources_dir / sub
        if not root.is_dir():
            continue
        for path in root.rglob("*.h"):
            if path.name.lower() in forbidden:
                raise AssertionError(f"Forbidden bundled Lua header found: {path}")


def check_root_cmake(cmake_path: Path) -> None:
    text = cmake_path.read_text(encoding="utf-8")
    for required in (
        'CMAKE_SYSTEM_NAME STREQUAL "Windows"',
        "CMAKE_SIZEOF_VOID_P EQUAL 8",
        "USE_VULKAN_RENDERER",
        "CMAKE_CXX_STANDARD 26",
        "include(CTest)",
    ):
        if required not in text:
            raise AssertionError(f"Missing required contract '{required}' in {cmake_path}")


def check_dotnet_alignment(repo_root: Path) -> None:
    global_json = json.loads((repo_root / "global.json").read_text(encoding="utf-8"))
    sdk = global_json["sdk"]
    sdk_version = sdk["version"]
    if sdk_version != "11.0.100" or sdk.get("rollForward") != "latestPatch" or sdk.get("allowPrerelease") is not False:
        raise AssertionError(f"Unexpected global.json SDK pinning: {sdk!r}")
    major = sdk_version.split(".", 1)[0]
    expected_tfm = f"net{major}.0"
    expected_lock_tfm = f"net{major}.0"

    if (repo_root / "sources/xray/wpf_controls").exists():
        raise AssertionError("Legacy sources/xray/wpf_controls directory must be removed")

    editor_csproj = repo_root / "sources/xray/editor/base/EditorHost.csproj.in"
    editor_text = editor_csproj.read_text(encoding="utf-8")
    for prop, expected_val in (
        ("TargetFramework", expected_tfm),
        ("RuntimeFrameworkVersion", f"{major}.0.0"),
        ("RestorePackagesWithLockFile", "true"),
        ("RestoreLockedMode", "true"),
        ("NuGetLockFilePath", "@CMAKE_CURRENT_SOURCE_DIR@/EditorHost.packages.lock.json"),
    ):
        m = re.search(rf"<{prop}>([^<]+)</{prop}>", editor_text)
        if not m or m.group(1) != expected_val:
            raise AssertionError(f"{editor_csproj} <{prop}>={m.group(1) if m else None!r}, expected {expected_val!r}")
    for forbidden in ("<UseWindowsForms>", "<UseWPF>", "DockPanelSuite", "Avalonia.Win32.Interoperability", "wpf_controls"):
        if forbidden in editor_text:
            raise AssertionError(f"{editor_csproj} must not reference {forbidden!r}")

    for csproj_rel, req_props in (
        ("sources/xray/editor/core/xray.editor.core.csproj", (("TargetFramework", expected_tfm),)),
        (
            "sources/xray/editor/avalonia_controls/xray.editor.avalonia_controls.csproj",
            (("TargetFramework", expected_tfm), ("RestorePackagesWithLockFile", "true"), ("RestoreLockedMode", "true")),
        ),
        (
            "sources/xray/editor/avalonia_controls.tests/xray.editor.avalonia_controls.tests.csproj",
            (("TargetFramework", expected_tfm), ("RestorePackagesWithLockFile", "true"), ("RestoreLockedMode", "true")),
        ),
    ):
        csproj_path = repo_root / csproj_rel
        csproj_text = csproj_path.read_text(encoding="utf-8")
        for prop, expected_val in req_props:
            m = re.search(rf"<{prop}>([^<]+)</{prop}>", csproj_text)
            if not m or m.group(1) != expected_val:
                raise AssertionError(f"{csproj_path} <{prop}>={m.group(1) if m else None!r}, expected {expected_val!r}")

    for lock_path in (
        repo_root / "sources/xray/editor/base/EditorHost.packages.lock.json",
        repo_root / "sources/xray/editor/avalonia_controls/packages.lock.json",
        repo_root / "sources/xray/editor/avalonia_controls.tests/packages.lock.json",
    ):
        if not lock_path.is_file():
            raise AssertionError(f"Missing NuGet lock file: {lock_path}")
        lock_text = lock_path.read_text(encoding="utf-8")
        lock_data = json.loads(lock_text)
        if lock_data.get("version") != 1:
            raise AssertionError(f"{lock_path} has unexpected lock version {lock_data.get('version')!r}")
        deps = lock_data.get("dependencies", {})
        if expected_lock_tfm not in deps or not deps[expected_lock_tfm]:
            raise AssertionError(f"{lock_path} missing dependencies for {expected_lock_tfm!r}")
        for forbidden in ("DockPanelSuite", "WeifenLuo", "Avalonia.Win32.Interoperability"):
            if forbidden in lock_text:
                raise AssertionError(f"{lock_path} must not contain legacy package {forbidden!r}")


def check_presets(presets_path: Path) -> None:
    data = json.loads(presets_path.read_text(encoding="utf-8"))
    configs = {p["name"]: p for p in data.get("configurePresets", [])}
    for name in ("windows-x64-debug", "windows-x64-release"):
        if name not in configs:
            raise AssertionError(f"Missing configure preset '{name}'")
        preset = configs[name]
        if not preset.get("binaryDir"):
            raise AssertionError(f"Preset '{name}' missing binaryDir")
        out_dir = preset.get("cacheVariables", {}).get("XRAY_BINARY_OUTPUT_DIRECTORY")
        if not out_dir:
            raise AssertionError(f"Preset '{name}' missing XRAY_BINARY_OUTPUT_DIRECTORY")

    if configs["windows-x64-debug"]["binaryDir"] == configs["windows-x64-release"]["binaryDir"]:
        raise AssertionError("Debug and Release presets must use distinct binaryDir")
    if (
        configs["windows-x64-debug"]["cacheVariables"]["XRAY_BINARY_OUTPUT_DIRECTORY"]
        == configs["windows-x64-release"]["cacheVariables"]["XRAY_BINARY_OUTPUT_DIRECTORY"]
    ):
        raise AssertionError("Debug and Release presets must use distinct XRAY_BINARY_OUTPUT_DIRECTORY")

    for tp in data.get("testPresets", []):
        if tp.get("execution", {}).get("noTestsAction") != "error":
            raise AssertionError(f"Test preset '{tp.get('name')}' must set execution.noTestsAction='error'")


def check_toolchain_manifest(manifest_path: Path) -> None:
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    for key in ("platform", "generator", "cxxStandard", "msys2Mingw64Packages", "dotnet", "dxc"):
        if key not in data:
            raise AssertionError(f"Missing '{key}' in {manifest_path}")
    if data["platform"] != "windows-x64" or data["cxxStandard"] != "26":
        raise AssertionError("Unexpected platform/cxxStandard in toolchain manifest")
    if data["dotnet"].get("tfm") != "net11.0":
        raise AssertionError(f"Unexpected dotnet.tfm in toolchain manifest: {data['dotnet']!r}")


def check_stage04_modernized_boundaries(sources_dir: Path) -> None:
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    if "find_package(SDL3 CONFIG REQUIRED)" not in root_cmake or "SDL2" in root_cmake:
        raise AssertionError("sources/CMakeLists.txt must use find_package(SDL3 CONFIG REQUIRED) and not SDL2")

    input_cmake = (sources_dir / "xray/input/CMakeLists.txt").read_text(encoding="utf-8")
    if "SDL3::SDL3" not in input_cmake or "xinput" in input_cmake.lower() or "dinput" in input_cmake.lower():
        raise AssertionError("xray/input/CMakeLists.txt must link SDL3::SDL3 without XInput/DirectInput")

    bugtrap_win = (sources_dir / "xray/debug/sources/bugtrap_win.cpp").read_text(encoding="utf-8")
    for forbidden in ("BugTrap/BugTrap.h", "BugTrap-x64.dll", "BugTrapN-x64.dll"):
        if forbidden in bugtrap_win:
            raise AssertionError(f"Legacy BugTrap reference '{forbidden}' found in bugtrap_win.cpp")

    sound_cmake = (sources_dir / "xray/sound/CMakeLists.txt").read_text(encoding="utf-8")
    if "xaudio2_9" not in sound_cmake or "xaudio2_8" in sound_cmake:
        raise AssertionError("xray/sound/CMakeLists.txt must link xaudio2_9 (not xaudio2_8)")

    stalker_cmake = (sources_dir / "xray/stalker2/pc/CMakeLists.txt").read_text(encoding="utf-8")
    if "xinput" in stalker_cmake.lower():
        raise AssertionError("xray/stalker2/pc/CMakeLists.txt must not link XInput directly")

    io_srv = (sources_dir / "xray/network/sources/io_service.cpp").read_text(encoding="utf-8")
    if "test_port" in io_srv or "1001" in io_srv:
        raise AssertionError("io_service.cpp must not hardcode UDP test_port 1001 at startup")

    pkg_py = (sources_dir / "tools/sdk/package-sdk-runtime.py").read_text(encoding="utf-8")
    if "SDL3.dll" not in pkg_py:
        raise AssertionError("package-sdk-runtime.py must package SDL3.dll")

    manifest = (sources_dir / "tools/ci/toolchain-manifest.json").read_text(encoding="utf-8")
    if "mingw-w64-x86_64-sdl3" not in manifest:
        raise AssertionError("toolchain-manifest.json must list mingw-w64-x86_64-sdl3")


def check_stage05_modernized_boundaries(sources_dir: Path) -> None:
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    for opt in (
        "XRAY_GAME_PHYSICS_USE_BULLET",
        "XRAY_COLLISION_USE_BULLET_BVH",
        "XRAY_COOK_FITTER_USE_NATIVE",
    ):
        if opt not in root_cmake:
            raise AssertionError(f"sources/CMakeLists.txt missing Stage 05 option {opt}")

    game_init = (sources_dir / "xray/game/sources/game_initialize.cpp").read_text(encoding="utf-8")
    if "XRAY_GAME_PHYSICS_USE_BULLET" not in game_init:
        raise AssertionError("game_initialize.cpp must gate Bullet/ODE world via XRAY_GAME_PHYSICS_USE_BULLET")

    phys_cooker = (sources_dir / "xray/maya_cook/sources/physics_cooker.cpp").read_text(encoding="utf-8")
    if "Wm4" in phys_cooker:
        raise AssertionError("physics_cooker.cpp must stay isolated from WildMagic4 (Wm4)")


def check_stage06_modernized_boundaries(repo_root: Path) -> None:
    sources_dir = repo_root / "sources"
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    for required in (
        "option(XRAY_BUILD_EDITOR_HOST",
        "if(WIN32 AND XRAY_BUILD_EDITOR_HOST)",
    ):
        if required not in root_cmake:
            raise AssertionError(f"sources/CMakeLists.txt missing Stage 06 EditorHost decoupling '{required}'")

    stalker_cmake = (sources_dir / "xray/stalker2/pc/CMakeLists.txt").read_text(encoding="utf-8")
    if "if(TARGET xray_editor_world)" not in stalker_cmake:
        raise AssertionError("xray/stalker2/pc/CMakeLists.txt must guard xray_editor_world dependency with if(TARGET ...)")

    lock_path = sources_dir / "tools/blender/legacy-corpus-lock.json"
    if not lock_path.is_file():
        raise AssertionError(f"Missing legacy corpus lock file: {lock_path}")
    lock_data = json.loads(lock_path.read_text(encoding="utf-8"))
    if lock_data.get("schema") != "xray-legacy-corpus-lock/v1":
        raise AssertionError(f"Unexpected corpus lock schema: {lock_data.get('schema')!r}")
    for key in ("inventory_sha256", "content_sha256"):
        digest = lock_data.get(key, "")
        if not isinstance(digest, str) or len(digest) != 64 or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise AssertionError(f"{lock_path} invalid {key}: {digest!r}")
    if lock_data.get("summary", {}).get("assets", 0) <= 0:
        raise AssertionError(f"{lock_path} summary.assets must be > 0")

    manifest_path = sources_dir / "xray/stalker2/pc/stalker2.manifest"
    manifest_text = manifest_path.read_text(encoding="utf-8")
    if "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" not in manifest_text:
        raise AssertionError(f"{manifest_path} must declare Win10/11 supportedOS for Avalonia NativeControlHost")


def check_stage07_modernized_boundaries(sources_dir: Path) -> None:
    vk_dir = sources_dir / "xray/render/vulkan/sources"
    corpus = "\n".join(p.read_text(encoding="utf-8", errors="replace") for p in sorted(vk_dir.glob("*.cpp")))
    for required in (
        "vkCmdPipelineBarrier2",
        "vkQueueSubmit2",
        "vkCmdWriteTimestamp2",
        "vkCmdBeginRendering",
        "vkCmdEndRendering",
        "VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO",
    ):
        if required not in corpus:
            raise AssertionError(f"Vulkan backend missing Stage 07 API '{required}'")

    for forbidden in (
        "vkCmdPipelineBarrier(",
        "vkQueueSubmit(",
        "vkCmdWriteTimestamp(",
        "vkCmdBeginRenderPass(",
        "vkCmdEndRenderPass(",
        "vkCreateRenderPass(",
        "vkCreateFramebuffer(",
    ):
        if forbidden in corpus:
            raise AssertionError(f"Vulkan backend still calls legacy API '{forbidden}'")


def check_stage08_modernized_boundaries(repo_root: Path) -> None:
    sources_dir = repo_root / "sources"
    archival_removed = (
        "asio.zip",
        "BugTrap",
        "build_log.txt",
        "build_log_final.txt",
        "UpgradeLog.htm",
        "vcpkg.json",
        "xray/render/dx9",
        "xray/render/common",
        "xray/maya",
        "xray/maya_animation",
        "xray/graphics_benchmark",
        "xray/rms_generator",
        "xray/rtp_learn",
        "xray/shader_generator",
        "xray/sound_system_usage_example",
        "xray/vfs_pack",
        "xray/finger_printer",
    )
    for rel in archival_removed:
        if (sources_dir / rel).exists():
            raise AssertionError(f"Archival path '{rel}' should have been removed from active build tree")

    dbg_init = (sources_dir / "cs/core/sources/debug_initialize.cpp").read_text(encoding="utf-8")
    if "#\tdefine CS_USE_BUG_TRAP\t\t\t\t1" in dbg_init or "#define CS_USE_BUG_TRAP 1" in dbg_init:
        raise AssertionError("cs/core/sources/debug_initialize.cpp must keep CS_USE_BUG_TRAP disabled (0)")

    formalized_cmakes = (
        "xray/render/vulkan/CMakeLists.txt",
        "xray/collision/CMakeLists.txt",
        "xray/physics/CMakeLists.txt",
        "xray/core/CMakeLists.txt",
        "xray/animation/CMakeLists.txt",
        "xray/debug/CMakeLists.txt",
        "xray/input/CMakeLists.txt",
        "xray/logging/CMakeLists.txt",
        "xray/particle/CMakeLists.txt",
        "xray/sound/CMakeLists.txt",
        "xray/rtp/CMakeLists.txt",
        "xray/fs/CMakeLists.txt",
        "cs/core/CMakeLists.txt",
        "cs/script/CMakeLists.txt",
        "opcode/CMakeLists.txt",
    )
    for rel in formalized_cmakes:
        text = (sources_dir / rel).read_text(encoding="utf-8")
        if "file(GLOB" in text or "list(FILTER" in text or "list(REMOVE_ITEM" in text:
            raise AssertionError(f"CMakeLists '{rel}' must use explicit source lists without GLOB/FILTER/REMOVE_ITEM")

    for doc_rel in (
        "THIRD_PARTY_DEPENDENCIES.md",
        "versions.txt",
        "tools/ci/build_source_map.py",
        "tools/ci/dependencies-manifest.json",
    ):
        p = sources_dir / doc_rel
        if not p.is_file() or p.stat().st_size == 0:
            raise AssertionError(f"Missing required Stage 08 documentation/tool file: {doc_rel}")

    manifest = json.loads((sources_dir / "tools/ci/dependencies-manifest.json").read_text(encoding="utf-8"))
    if len(manifest.get("vendoredDependencies", [])) < 8:
        raise AssertionError("dependencies-manifest.json missing vendoredDependencies entries")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    args = parser.parse_args()
    repo_root = Path(args.repo_root).resolve()

    check_no_bundled_lua(repo_root / "sources")
    check_root_cmake(repo_root / "sources/CMakeLists.txt")
    check_dotnet_alignment(repo_root)
    check_presets(repo_root / "sources/CMakePresets.json")
    check_toolchain_manifest(repo_root / "sources/tools/ci/toolchain-manifest.json")
    check_stage04_modernized_boundaries(repo_root / "sources")
    check_stage05_modernized_boundaries(repo_root / "sources")
    check_stage06_modernized_boundaries(repo_root)
    check_stage07_modernized_boundaries(repo_root / "sources")
    check_stage08_modernized_boundaries(repo_root)
    print("PASS: xray_source_contract")


if __name__ == "__main__":
    main()
