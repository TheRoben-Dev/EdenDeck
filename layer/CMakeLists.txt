cmake_minimum_required(VERSION 3.20)
project(scanline_layer VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

option(SCANLINE_LAYER_TESTS "Build unit tests" ON)

find_package(Vulkan REQUIRED)

add_library(VkLayer_SCANLINE_overlay SHARED src/layer.cpp)
target_include_directories(VkLayer_SCANLINE_overlay PRIVATE src)
# Header-only use of Vulkan: the layer must never link libvulkan itself.
target_link_libraries(VkLayer_SCANLINE_overlay PRIVATE Vulkan::Headers)
target_compile_options(VkLayer_SCANLINE_overlay PRIVATE -Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers)
target_link_options(VkLayer_SCANLINE_overlay PRIVATE -Wl,--no-undefined -Wl,-z,defs)

# Manifest next to the built library (relative path, so the build dir works with VK_ADD_LAYER_PATH)
set(LAYER_LIBRARY_PATH "./libVkLayer_SCANLINE_overlay.so")
configure_file(manifest/VkLayer_SCANLINE_overlay.json.in
               ${CMAKE_CURRENT_BINARY_DIR}/VkLayer_SCANLINE_overlay.json @ONLY)
set(LAYER_LIBRARY_PATH "../libVkLayer_SCANLINE_overlay.so")
configure_file(manifest/VkLayer_SCANLINE_overlay_implicit.json.in
               ${CMAKE_CURRENT_BINARY_DIR}/implicit/VkLayer_SCANLINE_overlay.json @ONLY)

include(GNUInstallDirs)
install(TARGETS VkLayer_SCANLINE_overlay LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR})
# Installed manifest uses the absolute library path.
set(LAYER_LIBRARY_PATH "${CMAKE_INSTALL_FULL_LIBDIR}/libVkLayer_SCANLINE_overlay.so")
configure_file(manifest/VkLayer_SCANLINE_overlay.json.in
               ${CMAKE_CURRENT_BINARY_DIR}/install/VkLayer_SCANLINE_overlay.json @ONLY)
install(FILES ${CMAKE_CURRENT_BINARY_DIR}/install/VkLayer_SCANLINE_overlay.json
        DESTINATION ${CMAKE_INSTALL_DATADIR}/vulkan/explicit_layer.d)
# Implicit variant is only active when ENABLE_SCANLINE_LAYER=1 is set (and never when
# DISABLE_SCANLINE_LAYER=1). Not installed by default; packaging decides (see docs).

if(SCANLINE_LAYER_TESTS)
    enable_testing()
    add_executable(frame_stats_test tests/frame_stats_test.cpp)
    target_include_directories(frame_stats_test PRIVATE src)
    add_test(NAME frame_stats_test COMMAND frame_stats_test)
    add_executable(crt_settings_test tests/crt_settings_test.cpp)
    target_include_directories(crt_settings_test PRIVATE src)
    add_test(NAME crt_settings_test COMMAND crt_settings_test)
endif()
