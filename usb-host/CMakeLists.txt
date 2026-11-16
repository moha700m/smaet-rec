cmake_minimum_required(VERSION 3.25)
project(SmaetRecUsbHost LANGUAGES CXX)

if(NOT WIN32 OR NOT MSVC)
  message(FATAL_ERROR "The SMAET USB host is supported on Windows x64 with MSVC.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(IPHONEMIRROR_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(IPHONEMIRROR_BUILD_DANGEROUS_USB_TOOLS OFF CACHE BOOL "" FORCE)

set(IPHONEMIRROR_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/../third_party/iphone-usb-host")
if(NOT EXISTS "${IPHONEMIRROR_SOURCE}/CMakeLists.txt")
  message(FATAL_ERROR "iPhoneMirror submodule missing. Run: git submodule update --init --recursive")
endif()

add_subdirectory("${IPHONEMIRROR_SOURCE}" "${CMAKE_BINARY_DIR}/iphone-mirror")

add_executable(iphone-usb-host adapter/main.cpp)
target_include_directories(iphone-usb-host PRIVATE
  "${IPHONEMIRROR_SOURCE}/src/Core/include")
target_compile_definitions(iphone-usb-host PRIVATE
  UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(iphone-usb-host PRIVATE /W4 /permissive- /utf-8 /EHsc)
target_link_libraries(iphone-usb-host PRIVATE iPhoneMirror.Core)
add_dependencies(iphone-usb-host iPhoneMirror.UsbConfigurationSwitch)

set_target_properties(iphone-usb-host PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/package/usb"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/package/usb"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/package/usb"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/package/usb"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/package/usb")

add_custom_command(TARGET iphone-usb-host POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "$<TARGET_FILE:iPhoneMirror.Core>" "$<TARGET_FILE_DIR:iphone-usb-host>"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "$<TARGET_FILE:iPhoneMirror.UsbConfigurationSwitch>" "$<TARGET_FILE_DIR:iphone-usb-host>"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${IPHONEMIRROR_SOURCE}/third_party/libusb/bin/x64/libusb-1.0.dll"
    "$<TARGET_FILE_DIR:iphone-usb-host>"
  COMMENT "Assembling the isolated GPL USB runtime")
