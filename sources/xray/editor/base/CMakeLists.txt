# xray_editor_base (Native part)
project(xray_editor_base)

find_program(XRAY_DOTNET_EXECUTABLE dotnet REQUIRED)
file(REAL_PATH "${XRAY_DOTNET_EXECUTABLE}" _xray_dotnet_real_exe)
get_filename_component(_xray_dotnet_detected_root "${_xray_dotnet_real_exe}" DIRECTORY)
if(NOT EXISTS "${_xray_dotnet_detected_root}/host/fxr")
    foreach(_xray_dotnet_candidate IN ITEMS "$ENV{DOTNET_ROOT}" "/usr/lib64/dotnet" "/usr/share/dotnet")
        if(_xray_dotnet_candidate AND EXISTS "${_xray_dotnet_candidate}/host/fxr")
            set(_xray_dotnet_detected_root "${_xray_dotnet_candidate}")
            break()
        endif()
    endforeach()
endif()
if(DEFINED XRAY_DOTNET_ROOT AND XRAY_DOTNET_ROOT AND NOT EXISTS "${XRAY_DOTNET_ROOT}/host/fxr")
    unset(XRAY_DOTNET_ROOT CACHE)
endif()
set(XRAY_DOTNET_ROOT "${_xray_dotnet_detected_root}" CACHE PATH "System x64 .NET installation used by the editor")
set(XRAY_MANAGED_EDITOR_DIR "${CMAKE_BINARY_DIR}/editor-managed")
file(MAKE_DIRECTORY "${XRAY_MANAGED_EDITOR_DIR}")
configure_file(EditorHost.csproj.in "${XRAY_MANAGED_EDITOR_DIR}/EditorHost.csproj" @ONLY)
set(XRAY_SDK_UI_PACKAGE_SOURCE "" CACHE PATH "Optional offline NuGet feed for SDK UI")
set(editor_package_source "https://api.nuget.org/v3/index.json")
if(XRAY_SDK_UI_PACKAGE_SOURCE)
    set(editor_package_source "${XRAY_SDK_UI_PACKAGE_SOURCE}")
elseif(EXISTS "${CMAKE_SOURCE_DIR}/../build/sdk-modernization/nuget")
    set(editor_package_source "${CMAKE_SOURCE_DIR}/../build/sdk-modernization/nuget")
endif()
file(WRITE "${XRAY_MANAGED_EDITOR_DIR}/NuGet.Config" "<configuration><packageSources><clear /><add key=\"sdk\" value=\"${editor_package_source}\" /></packageSources></configuration>\n")
file(GLOB_RECURSE editor_core_inputs CONFIGURE_DEPENDS
    "${CMAKE_SOURCE_DIR}/xray/editor/core/*.cs")
file(GLOB_RECURSE editor_avalonia_inputs CONFIGURE_DEPENDS
    "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/*.cs"
    "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/*.axaml"
    "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/*.png")
add_custom_command(
    OUTPUT "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.base.dll"
        "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.base.runtimeconfig.json"
        "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.base.deps.json"
        "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.core.dll"
        "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.avalonia_controls.dll"
    COMMAND ${CMAKE_COMMAND} -E env
        "DOTNET_CLI_HOME=${XRAY_MANAGED_EDITOR_DIR}/cli-home"
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1" "DOTNET_CLI_TELEMETRY_OPTOUT=1"
        "DOTNET_NOLOGO=1" "DOTNET_GENERATE_ASPNET_CERTIFICATE=false" "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false"
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1" "MSBuildEnableWorkloadResolver=false"
        "NUGET_PACKAGES=${XRAY_MANAGED_EDITOR_DIR}/packages"
        "${XRAY_DOTNET_EXECUTABLE}" build "${XRAY_MANAGED_EDITOR_DIR}/EditorHost.csproj"
        --configuration "${CMAKE_BUILD_TYPE}" --output "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}"
        --artifacts-path "${XRAY_MANAGED_EDITOR_DIR}/artifacts"
        --configfile "${XRAY_MANAGED_EDITOR_DIR}/NuGet.Config" --nologo
    DEPENDS Bridge.cs PlatformWindowService.cs EditorWorld.cs EditorWorld.Sdk.cs SdkProperties.cs SdkNativeResources.cs SdkRenderOptions.cs SdkDccResources.cs SdkDccWindow.cs WindowIde.Dcc.cs SdkModelSources.cs SdkModelPhysics.cs SdkAnimationPreview.cs SdkAnimationViewerEditor.cs SdkAnimationCollectionEditor.cs SdkAnimationSetup.cs SdkAnimationGroups.cs SdkAnimationPlacement.cs WindowIde.Animation.cs SdkModelEditor.cs WindowIde.Models.cs SdkScene.cs SdkViewport.cs SdkGizmo.cs ProjectViewportGizmos.cs ViewportNavigation.cs ProjectViewportEditing.cs SdkDockLayout.cs
        AvaloniaPreviewWindow.cs AvaloniaIdeBridge.cs SdkOutputs.cs SdkSelection.cs SdkAdvancedGizmo.cs SdkResourceReload.cs ProjectViewportOutputs.cs Action.cs ActionContinuous.cs ActionShortcut.cs ActionsLayer.cs SdkRuntime.cs
        InputKeysHolder.cs InputEngine.cs
        SdkSound.cs WindowIde.Sound.cs SdkParticle.cs WindowIde.Particle.cs WindowIde.Dialog.cs SdkScripting.cs WindowIde.Scripting.cs SdkNavigation.cs WindowIde.Navigation.cs SdkMaterialSources.cs SdkMaterialEditor.cs WindowIde.Materials.cs SdkAssetSources.cs SdkTextureEditor.cs WindowIde.Assets.cs ObjectsLibrary.cs SdkResourcePipeline.cs SdkResourceBrowser.cs ProjectBrowserDialog.cs
        SdkTerrain.cs SdkWorldTools.cs ProjectItems.cs Project.cs ProjectEditing.cs ProjectClipboard.cs ProjectPropertyAdapters.cs
        SdkAvaloniaHost.cs ObjectInspectorTab.cs ProjectTab.cs ToolsTab.cs SdkIdePanels.cs WindowIde.cs WindowIde.Projects.cs WindowIde.Editing.cs
        images/images16x16.png EditorHost.packages.lock.json ${editor_core_inputs} ${editor_avalonia_inputs}
        "${CMAKE_SOURCE_DIR}/xray/editor/core/xray.editor.core.csproj"
        "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/xray.editor.avalonia_controls.csproj"
        "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/packages.lock.json"
        "${XRAY_MANAGED_EDITOR_DIR}/EditorHost.csproj" "${XRAY_MANAGED_EDITOR_DIR}/NuGet.Config"
    VERBATIM)
add_custom_target(xray_editor_host ALL DEPENDS
    "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.base.dll"
    "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.base.runtimeconfig.json"
    "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/xray.editor.base.deps.json")

set(XRAY_AVALONIA_TEST_OUT "${XRAY_MANAGED_EDITOR_DIR}/avalonia-tests")
file(GLOB_RECURSE editor_avalonia_test_inputs CONFIGURE_DEPENDS
    "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls.tests/*.cs")
add_custom_command(
    OUTPUT "${XRAY_AVALONIA_TEST_OUT}/xray.editor.avalonia_controls.tests.dll"
        "${XRAY_AVALONIA_TEST_OUT}/xray.editor.avalonia_controls.tests.runtimeconfig.json"
        "${XRAY_AVALONIA_TEST_OUT}/xray.editor.avalonia_controls.tests.deps.json"
    COMMAND ${CMAKE_COMMAND} -E env
        "DOTNET_CLI_HOME=${XRAY_MANAGED_EDITOR_DIR}/cli-home"
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1" "DOTNET_CLI_TELEMETRY_OPTOUT=1"
        "DOTNET_NOLOGO=1" "DOTNET_GENERATE_ASPNET_CERTIFICATE=false" "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false"
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1" "MSBuildEnableWorkloadResolver=false"
        "NUGET_PACKAGES=${XRAY_MANAGED_EDITOR_DIR}/packages"
        "${XRAY_DOTNET_EXECUTABLE}" build "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls.tests/xray.editor.avalonia_controls.tests.csproj"
        --configuration "${CMAKE_BUILD_TYPE}" --output "${XRAY_AVALONIA_TEST_OUT}"
        --artifacts-path "${XRAY_MANAGED_EDITOR_DIR}/avalonia-tests-artifacts"
        --configfile "${XRAY_MANAGED_EDITOR_DIR}/NuGet.Config" --nologo
    DEPENDS ${editor_avalonia_inputs} ${editor_avalonia_test_inputs} ${editor_core_inputs}
        "${CMAKE_SOURCE_DIR}/xray/editor/core/xray.editor.core.csproj"
        "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/xray.editor.avalonia_controls.csproj"
        "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls/packages.lock.json"
        "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls.tests/xray.editor.avalonia_controls.tests.csproj"
        "${CMAKE_SOURCE_DIR}/xray/editor/avalonia_controls.tests/packages.lock.json"
        "${XRAY_MANAGED_EDITOR_DIR}/NuGet.Config"
    VERBATIM)
add_custom_target(xray_avalonia_headless_tests ALL DEPENDS
    "${XRAY_AVALONIA_TEST_OUT}/xray.editor.avalonia_controls.tests.dll"
    "${XRAY_AVALONIA_TEST_OUT}/xray.editor.avalonia_controls.tests.runtimeconfig.json")

set(SOURCES
    "sources/sdk_gizmo.cpp"
    "sources/sdk_transform.cpp"
    "sources/editor_base_entry_point.cpp"
    "sources/editor_base_library_linkage.cpp"
    "sources/editor_creator.cpp"
    "sources/pch.cpp"
    # Phase 3: transform control system (native)
    "sources/transform_control_base.cpp"
    "sources/transform_control_rotation.cpp"
    "sources/transform_control_rotation_draw.cpp"
    "sources/transform_control_scaling.cpp"
    "sources/transform_control_scaling_draw.cpp"
    "sources/transform_control_translation.cpp"
    "sources/transform_control_translation_draw.cpp"
    "sources/transform_control_translation_snap.cpp"
    "sources/collision_object_control.cpp"
)

set(HEADERS
    "sources/editor_base.h"
    "sources/editor_base_memory.h"
    "sources/editor_creator.h"
    "sources/pch.h"
    # Phase 3: native headers
    "sources/editor_control_base.h"
    "sources/transform_control_base.h"
    "sources/transform_control_object.h"
    "sources/transform_control_rotation.h"
    "sources/transform_control_scaling.h"
    "sources/transform_control_translation.h"
)

add_library(xray_editor_base SHARED ${SOURCES} ${HEADERS})

target_include_directories(xray_editor_base PUBLIC 
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/sources
    ${XRAY_ENGINE_DIR}/sources
)

target_link_libraries(xray_editor_base PRIVATE 
    xray_core
    xray_collision
    xray_animation
    xray_render_vulkan
    xray_physics
)

target_compile_definitions(xray_editor_base PRIVATE XRAY_EDITOR_BASE_BUILDING)

if(MSVC)
    set_target_properties(xray_editor_base PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS TRUE)
elseif(WIN32)
    target_link_options(xray_editor_base PRIVATE -Wl,--export-all-symbols)
endif()
