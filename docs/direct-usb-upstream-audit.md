# Direct USB upstream source audit

Audit date: 2026-11-16
Upstream: https://github.com/RayrenSX/iPhoneMirror
Pinned submodule commit: 85e56f183286c065e8bd674c87a0e62ed41755ed
License: GPL-3.0-only (upstream README and LICENSE)

## Findings

The pinned upstream commit is the current upstream `main` commit at audit time. The earlier assumed source paths were wrong: the actual implementation is organized under `src/Core/src`, not a top-level `QuickTime/` directory.

Relevant implementation files:

- `src/Core/src/Device/AppleUsbDiscovery.cpp`: Apple USB PnP and driver-stack discovery.
- `src/Core/src/Transport/UsbMuxClient.cpp` and `.h`: usbmux ListDevices, serial/UDID identity, pairing record checks and device tunnel.
- `src/Core/src/Transport/LibUsb0Transport.cpp` and `.h`: explicit per-device libusb0/QuickTime USB transport and configuration lifecycle.
- `src/Core/src/Protocol/QuickTimePacket.cpp` and `.h`: QuickTime packet framing; recognizes async `FEED` video and `EAT!` audio samples.
- `src/Core/src/Protocol/QuickTimeSession.cpp` and `.h`: QuickTime session state machine and negotiation.
- `src/Core/src/Capture/CaptureSession.cpp` and `.h`: USB capture orchestration, backend activation, session teardown, H.264 decode and frame publication.
- `src/Core/include/iPhoneMirror/CoreApi.h`: stable C ABI intended for external consumers.
- `src/Core/CMakeLists.txt`: builds `iPhoneMirror.Core.dll` and native protocol tests.
- `docs/ARCHITECTURE.md` and `docs/DEVELOPMENT.md`: runtime/process boundaries and supported Windows build/test workflow.

## Important API facts

The upstream C ABI is versioned (currently `ApiVersion = 18`) and already exposes the operations needed by a thin external host:

1. `im_initialize()` / `im_shutdown()`.
2. `im_refresh_devices[_ex](...)` returns real Apple device records, including the usbmux UDID, device ID, pairing/Lockdown state and connection type.
3. `im_session_create(udid, CaptureOptions, &handle)` creates an independent wired USB capture session.
4. `im_session_get_status(handle, ...)` exposes state, frame count, dimensions, failure stage and error details.
5. `im_session_get_latest_video_timestamp(handle, ...)` and `im_session_copy_latest_video_frame(handle, ...)` expose decoded BGRA8 frames (and the API also has a tightly packed NV12 copy path).
6. `im_session_stop(handle)` / `im_session_destroy(handle)` perform explicit session shutdown.

The API's `VideoFrameInfo` carries width, height, stride, pixel format and a 100-ns timestamp. Therefore a correct SMAET host can forward decoded frames over IPC without reverse-engineering QuickTime again or inventing an Annex-B packet source.

## Correct integration design

- Keep the GPL implementation in its own process and preserve the upstream GPL-3.0-only license, source and notices.
- Build a small GPL-licensed adapter executable against the upstream C ABI. The adapter should enumerate via `im_refresh_devices`, accept a real usbmux UDID, create one capture session, poll status/frame timestamp, copy BGRA/NV12 frames and forward them through a versioned bounded named-pipe protocol.
- Keep the MIT SMAET app as the IPC client and consumer. Do not link the GPL DLL into the MIT process.
- The current SMAET IPC v1 contract is not implemented by upstream and must be implemented by the adapter. It must not claim `H264 Annex-B`: upstream C ABI publishes decoded pixel frames, not compressed H.264 access units.
- Do not use a Windows PnP instance-id suffix as the authoritative UDID. Resolve the exact UDID from `im_refresh_devices` and match the selected physical USB device to the usbmux record.
- Do not modify Apple driver state silently. The upstream capture path has explicit safety checks and configuration restoration logic; preserve those behaviors.

## Build and validation constraints

Upstream documents Windows 10/11 x64, MSVC, Windows SDK and CMake 3.25+ as build requirements. The native CTest suite includes QuickTime packet/session tests, USB transport tests and recovery-policy tests. The development guide's full build also covers managed tests and packaging.

This audit was performed against source and build documentation only. It does not claim that the upstream native DLL was built in this environment, that an adapter executable exists, or that a physical iPhone capture has been verified.
