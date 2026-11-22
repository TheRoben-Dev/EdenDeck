cmake_minimum_required(VERSION 3.20)
project(edendeck_layer VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

option(EDENDECK_LAYER_TESTS "Build unit tests" ON)

find_package(Vulkan REQUIRED)

add_library(VkLayer_EDENDECK_fsr SHARED src/layer.cpp)
target_include_directories(VkLayer_EDENDECK_fsr PRIVATE src)
# Header-only use of Vulkan: the layer must never link libvulkan itself.
target_link_libraries(VkLayer_EDENDECK_fsr PRIVATE Vulkan::Headers)
target_compile_options(VkLayer_EDENDECK_fsr PRIVATE -Wall -Wextra -Wno-unused-parameter)
target_link_options(VkLayer_EDENDECK_fsr PRIVATE -Wl,--no-undefined -Wl,-z,defs)

# Manifest next to the built library (relative path, so the build dir works with VK_ADD_LAYER_PATH)
set(LAYER_LIBRARY_PATH "./libVkLayer_EDENDECK_fsr.so")
configure_file(manifest/VkLayer_EDENDECK_fsr.json.in
               ${CMAKE_CURRENT_BINARY_DIR}/VkLayer_EDENDECK_fsr.json @ONLY)
set(LAYER_LIBRARY_PATH "../libVkLayer_EDENDECK_fsr.so")
configure_file(manifest/VkLayer_EDENDECK_fsr_implicit.json.in
               ${CMAKE_CURRENT_BINARY_DIR}/implicit/VkLayer_EDENDECK_fsr.json @ONLY)

include(GNUInstallDirs)
install(TARGETS VkLayer_EDENDECK_fsr LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR})
# Installed manifest uses the absolute library path.
set(LAYER_LIBRARY_PATH "${CMAKE_INSTALL_FULL_LIBDIR}/libVkLayer_EDENDECK_fsr.so")
configure_file(manifest/VkLayer_EDENDECK_fsr.json.in
               ${CMAKE_CURRENT_BINARY_DIR}/install/VkLayer_EDENDECK_fsr.json @ONLY)
install(FILES ${CMAKE_CURRENT_BINARY_DIR}/install/VkLayer_EDENDECK_fsr.json
        DESTINATION ${CMAKE_INSTALL_DATADIR}/vulkan/explicit_layer.d)
# Implicit variant is only active when ENABLE_EDENDECK_LAYER=1 is set (and never when
# DISABLE_EDENDECK_LAYER=1). Not installed by default; packaging decides (see docs).

if(EDENDECK_LAYER_TESTS)
    enable_testing()
    add_executable(frame_stats_test tests/frame_stats_test.cpp)
    target_include_directories(frame_stats_test PRIVATE src)
    add_test(NAME frame_stats_test COMMAND frame_stats_test)
endif()
