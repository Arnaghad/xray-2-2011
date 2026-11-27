cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/debug
set(DEBUG_COMMON_SOURCES
    sources/debug.cpp
    sources/initialize.cpp
    sources/macros.cpp
    sources/pch.cpp
)

set(DEBUG_WIN_SOURCES
    sources/bugtrap_win.cpp
    sources/call_stack_win.cpp
    sources/debug_win.cpp
    sources/debug_win_shared.cpp
    sources/initialize_win.cpp
    sources/initialize_win_shared.cpp
    sources/unhandled_win.cpp
    sources/unhandled_win_shared.cpp
)

set(DEBUG_LINUX_SOURCES
    sources/bugtrap_stub.cpp
    sources/call_stack_posix.cpp
    sources/debug_posix.cpp
    sources/initialize_posix.cpp
    sources/unhandled_posix.cpp
)

set(DEBUG_SOURCES ${DEBUG_COMMON_SOURCES})
if(WIN32)
    list(APPEND DEBUG_SOURCES ${DEBUG_WIN_SOURCES})
elseif(UNIX)
    list(APPEND DEBUG_SOURCES ${DEBUG_LINUX_SOURCES})
endif()

# Create static library for xray/debug
add_library(xray_debug STATIC ${DEBUG_SOURCES})

# Include directories (root of sources)
target_include_directories(xray_debug PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Preprocessor definitions
target_compile_definitions(xray_debug PRIVATE XRAY_DEBUG_BUILDING)

if(WIN32)
    target_link_libraries(xray_debug PUBLIC dbghelp)
elseif(UNIX)
    include(CheckCXXSourceCompiles)
    set(_xray_saved_req_flags "${CMAKE_REQUIRED_FLAGS}")
    set(_xray_saved_req_libs "${CMAKE_REQUIRED_LIBRARIES}")
    set(CMAKE_REQUIRED_FLAGS "-std=c++26")
    set(CMAKE_REQUIRED_LIBRARIES "stdc++exp")
    check_cxx_source_compiles("
        #include <stacktrace>
        int main() { auto st = std::stacktrace::current(); return st.empty() ? 0 : 1; }
    " XRAY_HAS_STDCPPEXP_STACKTRACE)
    set(CMAKE_REQUIRED_FLAGS "${_xray_saved_req_flags}")
    set(CMAKE_REQUIRED_LIBRARIES "${_xray_saved_req_libs}")
    if(XRAY_HAS_STDCPPEXP_STACKTRACE)
        target_link_libraries(xray_debug PUBLIC stdc++exp)
    endif()
    target_link_libraries(xray_debug PUBLIC pthread ${CMAKE_DL_LIBS})
endif()
if(NOT MSVC)
    target_compile_options(xray_debug PRIVATE -Werror=return-local-addr)
endif()
