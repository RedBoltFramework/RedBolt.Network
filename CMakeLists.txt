# Copyright (c) 2026 RedBolt and contributors.
# This work is licensed under the terms of the MIT license.
# For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

cmake_minimum_required(VERSION 3.28)

project(RedBoltNetwork VERSION 0.1.0 LANGUAGES C CXX)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "Build dependencies statically" FORCE)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_SCAN_FOR_MODULES OFF)

if(ANDROID)
    add_compile_options(-fsigned-char)
    add_link_options("-Wl,-z,max-page-size=16384")
endif()
if(MINGW)
    set(CMAKE_CXX_LINK_LIBRARY_USING_WHOLE_ARCHIVE
        "LINKER:--whole-archive"
        "<LINK_ITEM>"
        "LINKER:--no-whole-archive")
    set(CMAKE_CXX_LINK_LIBRARY_USING_WHOLE_ARCHIVE_SUPPORTED TRUE)
endif()

set(REDBOLT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../RedBolt" CACHE PATH "RedBolt source root")
set(REDBOLT_SHARPCPP_ROOT "${REDBOLT_ROOT}/extern/SharpCpp" CACHE PATH "SharpCpp source root")
set(REDBOLT_DEPENDENCY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/extern" CACHE PATH "RedBolt.Network dependency cache")
set(REDBOLT_GENERATED_ROOT "${CMAKE_BINARY_DIR}/generated")
set(REDBOLT_NATIVE_GENERATED_DIR "${REDBOLT_GENERATED_ROOT}/redbolt-network-native")
set(REDBOLT_RUNTIME_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/build/bin/$<CONFIG>/linux-x64/extern/runtimes/linux-x64/native" CACHE PATH "Native runtime output")
set(REDBOLT_PDB_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/build/bin/$<CONFIG>/linux-x64/extern/pdb" CACHE PATH "Native PDB output")
set(REDBOLT_LIBDATACHANNEL_NATIVE_LIBRARY_NAME "RedBolt.Native.LibDataChannel" CACHE INTERNAL "libdatachannel native library name")

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
function(redbolt_require_path path description)
    if(NOT EXISTS "${path}")
        message(FATAL_ERROR "${description} does not exist: ${path}")
    endif()
endfunction()

list(PREPEND CMAKE_MODULE_PATH "${REDBOLT_ROOT}/cmake/Modules")
set(CPM_DONT_UPDATE_MODULE_PATH ON CACHE BOOL "Use RedBolt CMake modules" FORCE)
set(CPM_SOURCE_CACHE "${REDBOLT_DEPENDENCY_ROOT}" CACHE PATH "CPM source cache" FORCE)
unset(CPM_DIRECTORY CACHE)
unset(CPM_VERSION CACHE)
include("${REDBOLT_ROOT}/cmake/CPM.cmake")
set(FETCHCONTENT_BASE_DIR "${CMAKE_BINARY_DIR}/deps")

set(SHARPCPP_BUILD_E2E OFF CACHE BOOL "Build SharpCpp E2E" FORCE)
set(SHARPCPP_BUILD_RBFX OFF CACHE BOOL "Build SharpCpp rbfx bindings" FORCE)
add_subdirectory("${REDBOLT_SHARPCPP_ROOT}" "${CMAKE_BINARY_DIR}/deps/SharpCpp" EXCLUDE_FROM_ALL)
include(extern/OpenSSL.cmake)
include(extern/LibDataChannel.cmake)

if(ANDROID)
    if(ANDROID_ABI STREQUAL "x86_64")
        set(REDBOLT_BINDING_TARGET_TRIPLE "x86_64-linux-android24")
    elseif(ANDROID_ABI STREQUAL "arm64-v8a")
        set(REDBOLT_BINDING_TARGET_TRIPLE "aarch64-linux-android24")
    else()
        message(FATAL_ERROR "Unsupported Android ABI: ${ANDROID_ABI}")
    endif()
    get_filename_component(_redbolt_android_compiler_bin "${CMAKE_CXX_COMPILER}" DIRECTORY)
    get_filename_component(_redbolt_android_toolchain_root "${_redbolt_android_compiler_bin}" DIRECTORY)
    set(REDBOLT_BINDING_SYSROOT "${_redbolt_android_toolchain_root}/sysroot")
endif()
set(REDBOLT_BINDING_CXX_COMPILER "${CMAKE_CXX_COMPILER}")
set(REDBOLT_BINDING_DEFINES RTC_STATIC=1 RTC_ENABLE_MEDIA=0 RTC_ENABLE_WEBSOCKET=1)
if(CMAKE_CONFIGURATION_TYPES)
    list(APPEND REDBOLT_BINDING_DEFINES "$<$<NOT:$<CONFIG:Debug>>:NDEBUG>")
elseif(CMAKE_BUILD_TYPE MATCHES "^(Release|Distribution|ReleaseASAN|ReleaseUBSAN|ReleaseTSAN|ReleaseCoverage)$")
    list(APPEND REDBOLT_BINDING_DEFINES NDEBUG)
endif()

include("${REDBOLT_ROOT}/cmake/RedBoltSharpCpp.cmake")
add_subdirectory(src/RedBolt/Generator/Dependencies)
add_subdirectory(src/RedBolt/Native/LibDataChannel)

add_library(redbolt-network-native-libdatachannel SHARED "${REDBOLT_LIBDATACHANNEL_BINDINGS_CPP}")
add_dependencies(redbolt-network-native-libdatachannel redbolt-generate-libdatachannel-bindings)
target_link_libraries(redbolt-network-native-libdatachannel PRIVATE
    SharpCpp::NativeSupport
    "$<LINK_LIBRARY:WHOLE_ARCHIVE,${REDBOLT_LIBDATACHANNEL_LIBRARY}>")
target_include_directories(redbolt-network-native-libdatachannel PRIVATE "${REDBOLT_LIBDATACHANNEL_INCLUDE_DIR}")
target_compile_definitions(redbolt-network-native-libdatachannel PRIVATE
    REDBOLT_NATIVE_BUILD RTC_STATIC=1 RTC_ENABLE_MEDIA=0 RTC_ENABLE_WEBSOCKET=1)
set_target_properties(redbolt-network-native-libdatachannel PROPERTIES
    OUTPUT_NAME "${REDBOLT_LIBDATACHANNEL_NATIVE_LIBRARY_NAME}"
    RUNTIME_OUTPUT_DIRECTORY "${REDBOLT_RUNTIME_OUTPUT_DIR}"
    LIBRARY_OUTPUT_DIRECTORY "${REDBOLT_RUNTIME_OUTPUT_DIR}"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib/$<CONFIG>"
    WINDOWS_EXPORT_ALL_SYMBOLS ON
    C_VISIBILITY_PRESET default)
if(CMAKE_CONFIGURATION_TYPES)
    foreach(_redbolt_configuration IN LISTS CMAKE_CONFIGURATION_TYPES)
        string(TOUPPER "${_redbolt_configuration}" _redbolt_configuration_upper)
        set_target_properties(redbolt-network-native-libdatachannel PROPERTIES
            RUNTIME_OUTPUT_DIRECTORY_${_redbolt_configuration_upper} "${REDBOLT_RUNTIME_OUTPUT_DIR}"
            LIBRARY_OUTPUT_DIRECTORY_${_redbolt_configuration_upper} "${REDBOLT_RUNTIME_OUTPUT_DIR}")
    endforeach()
endif()
if(WIN32)
    set_target_properties(redbolt-network-native-libdatachannel PROPERTIES PREFIX "")
endif()

file(GENERATE
    OUTPUT "${REDBOLT_GENERATED_ROOT}/native-modules/$<CONFIG>/LIBDATACHANNEL.json"
    CONTENT "{\n  \"module\": \"LIBDATACHANNEL\",\n  \"managedSelector\": \"RedBolt.Bindings.LibDataChannel\",\n  \"nativeLibrary\": \"RedBolt.Native.LibDataChannel\",\n  \"requires\": [],\n  \"requirementClosure\": [],\n  \"artifacts\": [\"$<TARGET_FILE_NAME:redbolt-network-native-libdatachannel>\"]\n}\n")

add_custom_target(redbolt-network-native-all DEPENDS redbolt-network-native-libdatachannel)
add_custom_target(redbolt-network-android-native DEPENDS redbolt-network-native-libdatachannel)
