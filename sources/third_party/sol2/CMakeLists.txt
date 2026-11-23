add_library(xray_sol2 INTERFACE)
add_library(sol2::sol2 ALIAS xray_sol2)
target_include_directories(xray_sol2 SYSTEM INTERFACE ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_compile_definitions(xray_sol2 INTERFACE XRAY_SOL_SYSTEM_LUA=1)
# Upstream enables safety checks by default in Debug only. Script argument/type
# errors are part of the engine's runtime API contract in optimized builds too.
target_compile_definitions(xray_sol2 INTERFACE "$<$<NOT:$<CONFIG:Debug>>:SOL_ALL_SAFETIES_ON=1>")
target_link_libraries(xray_sol2 INTERFACE lua)
