cmake_minimum_required(VERSION 3.28)

project(SpriteLoomNative LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(ASTCENC_SOURCES
    third_party/astcenc/Source/astcenc_averages_and_directions.cpp
    third_party/astcenc/Source/astcenc_block_sizes.cpp
    third_party/astcenc/Source/astcenc_color_quantize.cpp
    third_party/astcenc/Source/astcenc_color_unquantize.cpp
    third_party/astcenc/Source/astcenc_compress_symbolic.cpp
    third_party/astcenc/Source/astcenc_compute_variance.cpp
    third_party/astcenc/Source/astcenc_decompress_symbolic.cpp
    third_party/astcenc/Source/astcenc_entry.cpp
    third_party/astcenc/Source/astcenc_find_best_partitioning.cpp
    third_party/astcenc/Source/astcenc_ideal_endpoints_and_weights.cpp
    third_party/astcenc/Source/astcenc_image.cpp
    third_party/astcenc/Source/astcenc_integer_sequence.cpp
    third_party/astcenc/Source/astcenc_lz_optimize.cpp
    third_party/astcenc/Source/astcenc_mathlib.cpp
    third_party/astcenc/Source/astcenc_mathlib_softfloat.cpp
    third_party/astcenc/Source/astcenc_partition_tables.cpp
    third_party/astcenc/Source/astcenc_percentile_tables.cpp
    third_party/astcenc/Source/astcenc_pick_best_endpoint_format.cpp
    third_party/astcenc/Source/astcenc_quantization.cpp
    third_party/astcenc/Source/astcenc_symbolic_physical.cpp
    third_party/astcenc/Source/astcenc_weight_align.cpp
    third_party/astcenc/Source/astcenc_weight_quant_xfer_tables.cpp)

set(ETCPAK_SOURCES
    third_party/etcpak/ProcessDxtc.cpp
    third_party/etcpak/ProcessRGB.cpp
    third_party/etcpak/bc7enc.cpp
    third_party/etcpak/Tables.cpp
    third_party/etcpak/Dither.cpp)

set(BASISU_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BASISU_BUILD_PYTHON OFF CACHE BOOL "" FORCE)
set(BASISU_OPENCL OFF CACHE BOOL "" FORCE)
set(BASISU_ZSTD ON CACHE BOOL "" FORCE)
set(BASISU_SSE ON CACHE BOOL "" FORCE)
add_subdirectory(third_party/basisu EXCLUDE_FROM_ALL)

add_library(sloom_native SHARED
    src/sloom_native.cpp
    ${ASTCENC_SOURCES}
    ${ETCPAK_SOURCES})

target_include_directories(sloom_native PRIVATE
    include
    third_party/astcenc/Source
    third_party/etcpak
    third_party/basisu
    third_party/basisu/encoder
    third_party/basisu/transcoder)

target_compile_definitions(sloom_native PRIVATE ASTCENC_SSE=2 ASTCENC_POPCNT=1)

if (WIN32)
    target_compile_options(sloom_native PRIVATE -msse2)
    target_link_options(sloom_native PRIVATE -static-libgcc -static-libstdc++)
endif ()

target_link_libraries(sloom_native PRIVATE basisu_encoder)

set_target_properties(sloom_native PROPERTIES PREFIX "")
