find_package(Python3 COMPONENTS Interpreter REQUIRED)

set(_xray_test_cxx_warnings "")
if(NOT MSVC)
    set(_xray_test_cxx_warnings
        -Wall -Wextra -Wstrict-aliasing=2 -Werror=strict-aliasing -Werror=return-local-addr -Wformat
    )
endif()

# --- Mandatory CPU tests (no GPU, Vulkan ICD, Blender, Maya, or external corpus required) ---

add_test(
    NAME xray_source_contract
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/test_source_contract.py"
        --repo-root "${CMAKE_SOURCE_DIR}/.."
)
set_tests_properties(xray_source_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_lua_sol_binding test_lua_sol_binding.cpp)
target_link_libraries(xray_lua_sol_binding PRIVATE sol2::sol2)
add_test(NAME xray_lua_sol_binding COMMAND xray_lua_sol_binding)
set_tests_properties(xray_lua_sol_binding PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_windows_x64_contract test_windows_x64_contract.cpp)
target_link_libraries(xray_windows_x64_contract PRIVATE xray_core)
add_test(NAME xray_windows_x64_contract COMMAND xray_windows_x64_contract)
set_tests_properties(xray_windows_x64_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_vfs_legacy_decode test_vfs_legacy_decode.cpp)
target_include_directories(xray_vfs_legacy_decode PRIVATE "${CMAKE_SOURCE_DIR}/xray/maya_cook/sources")
target_link_libraries(xray_vfs_legacy_decode PRIVATE xray_maya_cook)
add_test(NAME xray_vfs_legacy_decode COMMAND xray_vfs_legacy_decode)
set_tests_properties(xray_vfs_legacy_decode PROPERTIES LABELS "cpu" TIMEOUT 60)

add_test(
    NAME xray_vfs_legacy_python_decode
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/test_vfs_legacy_decode.py"
)
set_tests_properties(xray_vfs_legacy_python_decode PROPERTIES LABELS "cpu" TIMEOUT 60)

add_test(
    NAME xray_dcc_exchange_cook_roundtrip
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/test_dcc_exchange_cook_roundtrip.py"
        --cook-cli $<TARGET_FILE:xray_asset_cook_cli>
)
set_tests_properties(xray_dcc_exchange_cook_roundtrip PROPERTIES LABELS "cpu" TIMEOUT 60)

add_test(
    NAME xray_dcc_v1_v2_compatibility_contract
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/test_dcc_v1_v2_compatibility.py"
        --cook-cli $<TARGET_FILE:xray_asset_cook_cli>
        --repo-root "${CMAKE_SOURCE_DIR}/.."
)
set_tests_properties(xray_dcc_v1_v2_compatibility_contract PROPERTIES LABELS "cpu" TIMEOUT 90)

add_executable(xray_editor_host_abi_contract test_editor_host_abi.cpp)
add_test(NAME xray_editor_host_abi_contract COMMAND xray_editor_host_abi_contract)
set_tests_properties(xray_editor_host_abi_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_ogg_vorbis_contract test_ogg_vorbis_contract.cpp)
target_link_libraries(xray_ogg_vorbis_contract PRIVATE PkgConfig::OGG_VORBIS)
add_test(NAME xray_ogg_vorbis_contract COMMAND xray_ogg_vorbis_contract)
set_tests_properties(xray_ogg_vorbis_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

set(_legacy_ogg_sample "C:/Stalker2_2011/xr2-dsgn/resources/sources/sounds/single/stereo_test/rain_stereo.ogg")
if(EXISTS "${_legacy_ogg_sample}")
    add_test(
        NAME xray_ogg_vorbis_legacy_corpus
        COMMAND xray_ogg_vorbis_contract --legacy-ogg "${_legacy_ogg_sample}"
    )
    set_tests_properties(xray_ogg_vorbis_legacy_corpus PROPERTIES LABELS "audio" TIMEOUT 60)
endif()

add_executable(xray_dependency_single_source_contract test_dependency_single_source.cpp)
target_link_libraries(xray_dependency_single_source_contract PRIVATE Boost::headers asio::asio PkgConfig::OGG_VORBIS PkgConfig::BULLET)
target_compile_options(xray_dependency_single_source_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_dependency_single_source_contract COMMAND xray_dependency_single_source_contract)
set_tests_properties(xray_dependency_single_source_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_math_and_binary_config_contract test_math_and_binary_config_contract.cpp)
target_link_libraries(xray_math_and_binary_config_contract PRIVATE xray_core)
target_compile_options(xray_math_and_binary_config_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_math_and_binary_config_contract COMMAND xray_math_and_binary_config_contract)
set_tests_properties(xray_math_and_binary_config_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_callback_and_raii_contract test_callback_and_raii_contract.cpp)
target_link_libraries(xray_callback_and_raii_contract PRIVATE xray_core)
target_compile_options(xray_callback_and_raii_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_callback_and_raii_contract COMMAND xray_callback_and_raii_contract)
set_tests_properties(xray_callback_and_raii_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_input_sdl3_contract test_input_sdl3_contract.cpp)
target_include_directories(xray_input_sdl3_contract PRIVATE "${CMAKE_SOURCE_DIR}/xray/input/sources")
target_link_libraries(xray_input_sdl3_contract PRIVATE xray_input xray_core SDL3::SDL3)
target_compile_options(xray_input_sdl3_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_input_sdl3_contract COMMAND xray_input_sdl3_contract)
set_tests_properties(xray_input_sdl3_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_xaudio2_9_contract test_xaudio2_9_contract.cpp)
target_link_libraries(xray_xaudio2_9_contract PRIVATE xray_core xaudio2_9 ole32)
target_compile_options(xray_xaudio2_9_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_xaudio2_9_contract COMMAND xray_xaudio2_9_contract)
set_tests_properties(xray_xaudio2_9_contract PROPERTIES LABELS "audio" TIMEOUT 60)

add_executable(xray_diagnostics_minidump_contract test_diagnostics_minidump_contract.cpp)
target_link_libraries(xray_diagnostics_minidump_contract PRIVATE xray_debug xray_core)
target_compile_options(xray_diagnostics_minidump_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_diagnostics_minidump_contract COMMAND xray_diagnostics_minidump_contract)
set_tests_properties(xray_diagnostics_minidump_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_network_udp_loopback test_network_udp_loopback.cpp)
target_link_libraries(xray_network_udp_loopback PRIVATE asio::asio ws2_32)
target_compile_options(xray_network_udp_loopback PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_network_udp_loopback COMMAND xray_network_udp_loopback)
set_tests_properties(xray_network_udp_loopback PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_physics_world_parity_contract test_physics_world_parity_contract.cpp)
target_include_directories(xray_physics_world_parity_contract PRIVATE "${CMAKE_SOURCE_DIR}/xray/physics/sources")
target_link_libraries(xray_physics_world_parity_contract PRIVATE xray_physics xray_collision xray_core PkgConfig::BULLET)
target_compile_options(xray_physics_world_parity_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_physics_world_parity_contract COMMAND xray_physics_world_parity_contract)
set_tests_properties(xray_physics_world_parity_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_collision_bvh_parity_contract test_collision_bvh_parity_contract.cpp)
target_include_directories(xray_collision_bvh_parity_contract PRIVATE "${CMAKE_SOURCE_DIR}/xray/collision/sources")
target_link_libraries(xray_collision_bvh_parity_contract PRIVATE xray_collision xray_core PkgConfig::BULLET)
target_compile_options(xray_collision_bvh_parity_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_collision_bvh_parity_contract COMMAND xray_collision_bvh_parity_contract)
set_tests_properties(xray_collision_bvh_parity_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

add_executable(xray_physics_cook_fitter_contract test_physics_cook_fitter_contract.cpp)
target_include_directories(xray_physics_cook_fitter_contract PRIVATE "${CMAKE_SOURCE_DIR}/xray/maya_cook/sources")
target_link_libraries(xray_physics_cook_fitter_contract PRIVATE xray_maya_cook)
target_compile_options(xray_physics_cook_fitter_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_physics_cook_fitter_contract COMMAND xray_physics_cook_fitter_contract)
set_tests_properties(xray_physics_cook_fitter_contract PROPERTIES LABELS "cpu" TIMEOUT 60)

# --- Environment-gated tests (Vulkan loader, DXC compiler, packaged SDK assets) ---

add_executable(xray_vulkan_original_archive test_vulkan_original_archive.cpp)
target_include_directories(xray_vulkan_original_archive PRIVATE
    "${CMAKE_SOURCE_DIR}/xray/maya_cook/sources"
    "${CMAKE_SOURCE_DIR}/xray/render/vulkan/sources"
)
target_link_libraries(xray_vulkan_original_archive PRIVATE xray_maya_cook xray_render_vulkan)
add_test(NAME xray_vulkan_original_archive COMMAND xray_vulkan_original_archive)
set_tests_properties(xray_vulkan_original_archive PROPERTIES LABELS "vulkan" TIMEOUT 60)

add_executable(xray_vulkan_sdl3_surface_contract test_vulkan_sdl3_surface_contract.cpp)
target_link_libraries(xray_vulkan_sdl3_surface_contract PRIVATE xray_render_vulkan SDL3::SDL3)
target_compile_options(xray_vulkan_sdl3_surface_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_vulkan_sdl3_surface_contract COMMAND xray_vulkan_sdl3_surface_contract)
set_tests_properties(xray_vulkan_sdl3_surface_contract PROPERTIES LABELS "vulkan" TIMEOUT 60)
set(_vk_val_dir "${CMAKE_SOURCE_DIR}/../build/vulkan-validation/mingw64/bin")
if(EXISTS "${_vk_val_dir}/VkLayer_khronos_validation.json")
    set_tests_properties(xray_vulkan_sdl3_surface_contract PROPERTIES
        ENVIRONMENT_MODIFICATION "VK_LAYER_PATH=set:${_vk_val_dir};PATH=path_list_prepend:${_vk_val_dir}"
    )
endif()

add_executable(xray_vulkan_sync2_dynamic_rendering_contract test_vulkan_sync2_dynamic_rendering.cpp)
target_link_libraries(xray_vulkan_sync2_dynamic_rendering_contract PRIVATE xray_render_vulkan xray_core)
target_compile_options(xray_vulkan_sync2_dynamic_rendering_contract PRIVATE ${_xray_test_cxx_warnings})
add_test(NAME xray_vulkan_sync2_dynamic_rendering_contract COMMAND xray_vulkan_sync2_dynamic_rendering_contract)
set_tests_properties(xray_vulkan_sync2_dynamic_rendering_contract PROPERTIES LABELS "vulkan" TIMEOUT 60)
if(EXISTS "${_vk_val_dir}/VkLayer_khronos_validation.json")
    set_tests_properties(xray_vulkan_sync2_dynamic_rendering_contract PROPERTIES
        ENVIRONMENT_MODIFICATION "VK_LAYER_PATH=set:${_vk_val_dir};PATH=path_list_prepend:${_vk_val_dir};XRAY_VULKAN_REQUIRE_VALIDATION=set:1;DISABLE_RTSS_LAYER=set:1;DISABLE_VULKAN_OBS_CAPTURE=set:1"
    )
endif()

set(XRAY_DXC_DIR "${CMAKE_SOURCE_DIR}/../build/dxc/bin/x64" CACHE PATH "Directory containing dxcompiler.dll and dxil.dll")
if(EXISTS "${XRAY_DXC_DIR}/dxcompiler.dll")
    add_test(
        NAME xray_dxc_shader_compile
        COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/test_dxc_shader_compile.py"
            --compiler $<TARGET_FILE:xray_shader_compiler>
            --shader-dir "${CMAKE_SOURCE_DIR}/xray/render/vulkan/assets"
            --dxc-dir "${XRAY_DXC_DIR}"
    )
    set_tests_properties(xray_dxc_shader_compile PROPERTIES LABELS "dxc" TIMEOUT 60)
endif()

set(_sdk_assets_dir "${CMAKE_SOURCE_DIR}/../build/sdk-assets")
set(_sdk_val_dir "${CMAKE_SOURCE_DIR}/../build/vulkan-validation/mingw64/bin")
if(XRAY_BUILD_EDITOR_HOST AND EXISTS "${_sdk_assets_dir}/scene.json" AND EXISTS "${XRAY_DXC_DIR}/dxcompiler.dll" AND EXISTS "${_sdk_val_dir}/VkLayer_khronos_validation.dll")
    add_test(
        NAME xray_sdk_package_smoke
        COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/test_sdk_package_smoke.py"
            --repo-root "${CMAKE_SOURCE_DIR}/.."
            --build-dir "${CMAKE_BINARY_DIR}"
            --config "$<CONFIG>"
            --dxc-dir "${XRAY_DXC_DIR}"
            --validation-dir "${_sdk_val_dir}"
            --assets-root "${_sdk_assets_dir}"
    )
    set_tests_properties(xray_sdk_package_smoke PROPERTIES LABELS "sdk" TIMEOUT 120)
endif()
