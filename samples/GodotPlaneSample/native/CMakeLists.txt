cmake_minimum_required(VERSION 3.17)

project(nkg_leanclr_bridge LANGUAGES CXX)

set(GODOT_CPP_ROOT "" CACHE PATH "Path to a godot-cpp checkout.")
set(LEANCLR_ROOT "" CACHE PATH "Path to the LeanCLR repository.")
set(NKG_GODOT_PROJECT_DIR "${CMAKE_CURRENT_LIST_DIR}/.." CACHE PATH "Path to the Godot sample project.")
set(NKG_GODOT_ADAPTER_NATIVE_DIR "${CMAKE_CURRENT_LIST_DIR}/../../../src/NKGGameFramework.Adapter.Godot/native" CACHE PATH "Path to NKG Godot adapter native sources.")

if(NOT EXISTS "${GODOT_CPP_ROOT}/CMakeLists.txt")
    message(FATAL_ERROR "GODOT_CPP_ROOT must point to a godot-cpp checkout.")
endif()

if(NOT EXISTS "${LEANCLR_ROOT}/src/runtime/CMakeLists.txt")
    message(FATAL_ERROR "LEANCLR_ROOT must point to the LeanCLR repository.")
endif()

set(GODOTCPP_TARGET "template_debug" CACHE STRING "" FORCE)
set(GODOTCPP_API_VERSION "4.7" CACHE STRING "" FORCE)
set(GODOTCPP_DISABLE_EXCEPTIONS ON CACHE BOOL "" FORCE)
set(GODOTCPP_ENABLE_TESTING OFF CACHE BOOL "" FORCE)
set(GODOTCPP_USE_HOT_RELOAD OFF CACHE BOOL "" FORCE)

add_subdirectory("${GODOT_CPP_ROOT}" "${CMAKE_BINARY_DIR}/godot-cpp")
add_subdirectory("${LEANCLR_ROOT}/src/runtime" "${CMAKE_BINARY_DIR}/leanclr-runtime")

add_library(nkg_leanclr_bridge SHARED
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_debug_http_server.cpp"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_godot_debug_transport.cpp"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_godot_host.cpp"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_godot_host_command_reader.cpp"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_godot_object_registry.cpp"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_godot_resource_registry.cpp"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src/nkg_leanclr_runtime_bridge.cpp"
    src/nkg_leanclr_plane_bridge.cpp
    src/nkg_leanclr_plane_host.cpp
    src/register_types.cpp
)

target_compile_features(nkg_leanclr_bridge PRIVATE cxx_std_17)

target_link_libraries(nkg_leanclr_bridge PRIVATE
    godot::cpp
    leanclr
)

target_include_directories(nkg_leanclr_bridge PRIVATE
    "${LEANCLR_ROOT}/src/runtime"
    "${NKG_GODOT_ADAPTER_NATIVE_DIR}/src"
)

set_target_properties(nkg_leanclr_bridge PROPERTIES
    OUTPUT_NAME "nkg_leanclr_bridge.windows.template_debug.x86_64"
    RUNTIME_OUTPUT_DIRECTORY "${NKG_GODOT_PROJECT_DIR}/bin"
    LIBRARY_OUTPUT_DIRECTORY "${NKG_GODOT_PROJECT_DIR}/bin"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib"
)

if(MSVC)
    target_compile_options(nkg_leanclr_bridge PRIVATE /utf-8)
endif()

if(WIN32)
    target_link_libraries(nkg_leanclr_bridge PRIVATE ws2_32)
endif()
