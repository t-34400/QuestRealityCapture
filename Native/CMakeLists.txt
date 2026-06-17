cmake_minimum_required(VERSION 3.22.1)

project(qrc_camera_native LANGUAGES C CXX)

add_library(qrc_camera_native SHARED
    src/qrc_camera_native.cpp
)

target_include_directories(qrc_camera_native PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_features(qrc_camera_native PRIVATE cxx_std_17)

target_compile_options(qrc_camera_native PRIVATE
    -Wall
    -Wextra
    -Werror=return-type
)

target_link_libraries(qrc_camera_native PRIVATE
    camera2ndk
    mediandk
    android
    log
    dl
)
