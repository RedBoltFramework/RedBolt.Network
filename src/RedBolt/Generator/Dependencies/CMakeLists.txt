# Copyright (c) 2026 RedBolt and contributors.
# This work is licensed under the terms of the MIT license.
# For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

redbolt_require_path("${REDBOLT_SHARPCPP_ROOT}" "REDBOLT_SHARPCPP_ROOT")

if(NOT REDBOLT_DEPENDENCIES_GENERATOR_DLL)
    set(REDBOLT_DEPENDENCIES_GENERATOR_DLL
        "${PROJECT_SOURCE_DIR}/build/bin/$<CONFIG>/host/tools/RedBolt.Network.Generator.Dependencies.dll"
        CACHE FILEPATH "RedBolt.Network dependency generator assembly")
endif()

file(GLOB_RECURSE REDBOLT_SHARPCPP_GENERATOR_INPUTS CONFIGURE_DEPENDS
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Model/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Metadata/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Parsing/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Pipeline/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Rendering/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Support/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/include/SharpCpp/*.h"
    "${REDBOLT_SHARPCPP_ROOT}/include/SharpCpp/*.hpp")
file(GLOB_RECURSE REDBOLT_DEPENDENCIES_GENERATOR_INPUTS CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cs"
    "${CMAKE_CURRENT_SOURCE_DIR}/*.csproj")

set(REDBOLT_SHARPCPP_GENERATOR_INPUTS "${REDBOLT_SHARPCPP_GENERATOR_INPUTS}" CACHE INTERNAL "SharpCpp generator inputs")
set(REDBOLT_DEPENDENCIES_GENERATOR_INPUTS "${REDBOLT_DEPENDENCIES_GENERATOR_INPUTS}" CACHE INTERNAL "RedBolt.Network generator inputs")

set(REDBOLT_LIBDATACHANNEL_GENERATED_DIR "${REDBOLT_NATIVE_GENERATED_DIR}/libdatachannel" CACHE INTERNAL "libdatachannel binding output")
set(REDBOLT_LIBDATACHANNEL_BINDINGS_STEM "RedBolt.Bindings.LibDataChannel" CACHE INTERNAL "libdatachannel binding stem")
set(REDBOLT_LIBDATACHANNEL_BINDINGS_CS "${REDBOLT_LIBDATACHANNEL_GENERATED_DIR}/${REDBOLT_LIBDATACHANNEL_BINDINGS_STEM}.Bindings.g.cs" CACHE INTERNAL "libdatachannel generated C# binding")
set(REDBOLT_LIBDATACHANNEL_BINDINGS_CPP "${REDBOLT_LIBDATACHANNEL_GENERATED_DIR}/${REDBOLT_LIBDATACHANNEL_BINDINGS_STEM}.Bindings.g.cpp" CACHE INTERNAL "libdatachannel generated native binding")
set(REDBOLT_LIBDATACHANNEL_BINDINGS_JSON "${REDBOLT_LIBDATACHANNEL_GENERATED_DIR}/${REDBOLT_LIBDATACHANNEL_BINDINGS_STEM}.json" CACHE INTERNAL "libdatachannel binding metadata")
set(REDBOLT_LIBDATACHANNEL_PARSE_HEADER "${REDBOLT_LIBDATACHANNEL_GENERATED_DIR}/LibDataChannelAll.h" CACHE INTERNAL "libdatachannel parse header")
file(MAKE_DIRECTORY "${REDBOLT_LIBDATACHANNEL_GENERATED_DIR}")

file(CONFIGURE OUTPUT "${REDBOLT_LIBDATACHANNEL_PARSE_HEADER}" CONTENT [=[
#pragma once
#include <rtc/rtc.h>
]=] @ONLY)
