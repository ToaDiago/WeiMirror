cmake_minimum_required(VERSION 3.25)

project(iPhoneMirror VERSION 1.8.3 LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "iPhoneMirror currently targets Windows 10/11 only")
endif()

option(IPHONEMIRROR_BUILD_TESTS "Build native protocol tests" ON)
option(IPHONEMIRROR_BUILD_DANGEROUS_USB_TOOLS
    "Build tools that issue real Apple USB configuration and bulk requests" OFF)

if(IPHONEMIRROR_BUILD_TESTS)
    enable_testing()
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_subdirectory(src/Core)

option(WEIMIRROR_AUDIO_TESTS "Build device-free audio regression checks" OFF)
if(WEIMIRROR_AUDIO_TESTS)
    add_executable(WeiMirror.AudioQA
        tests/AudioQA.cpp
        src/Core/src/Audio/WasapiRenderer.cpp
        src/Core/src/Logging.cpp)
    target_include_directories(WeiMirror.AudioQA PRIVATE src/Core/src)
    target_compile_definitions(WeiMirror.AudioQA PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(WeiMirror.AudioQA PRIVATE /W4 /permissive- /utf-8 /EHsc)
    target_link_libraries(WeiMirror.AudioQA PRIVATE ole32 avrt bcrypt)
endif()
