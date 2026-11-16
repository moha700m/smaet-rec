# Direct USB QuickTime host (separate GPL component)

SMAET REC remains MIT-licensed. The USB QuickTime implementation is kept as a separate executable/process so GPL source and its obligations do not silently become part of the MIT application.

## Upstream source

`third_party/iphone-usb-host` is a Git submodule pointing to RayrenSX/iPhoneMirror, pinned to a specific upstream commit. That project is GPL-3.0. Keep its source, notices, build system and resulting executable separately identifiable. Do not copy its source into `src/SmaetRec.*` or relabel it MIT.

The application-side IPC adapter is original SMAET REC code under the repository MIT license.

## SMAET adapter IPC contract v2 (decoded frames)

The helper is expected at `resources/usb/iphone-usb-host.exe` and is launched with `--smaet-ipc --pipe <pipe-name> --device-serial <USB-serial> --video bgra8`. The adapter resolves the selected physical USB serial to the authoritative usbmux UDID using the upstream Core API; the Windows PnP instance-id suffix is not treated as a UDID.

Each record consists of a 4-byte unsigned little-endian total length, then a 4-byte unsigned little-endian JSON-header length, then that many UTF-8 JSON bytes, then the optional binary payload. Status: `{"type":"status","message":"..."}`. Error: `{"type":"error","message":"..."}`. End: `{"type":"stopped"}`.

Video record JSON fields: `type=video`, `pixelFormat=bgra8`, `width`, `height`, `stride`, `payloadBytes`, and `timestamp100ns`. After the JSON header, exactly payloadBytes of tightly packed top-down BGRA8 pixels follow (`stride = width * 4`). The upstream Core API supplies decoded BGRA/NV12 frames, not H.264 access units.

The host must emit video only after the upstream capture session reaches Streaming and a decoded frame is available. Opening a USB handle is not streaming. Preserve the upstream 100-ns frame timestamp. Validate dimensions, stride, pixel format and payload bounds before forwarding. No simulated frames are permitted.

## Native host status

The upstream repository is linked as a separate GPL submodule; it is not yet an SMAET-specific host executable. A dedicated GPL adapter/launcher must be built against the pinned upstream C ABI and emit IPC v2 before USB capture can be considered functional. The app-side reader now expects decoded BGRA frames and submits them directly to MemoryFrameSource; it no longer treats upstream decoded pixels as H.264. The app intentionally fails clearly when the helper is missing; it never falls back to AirPlay, Wi-Fi, or fake video.

## Driver safety

USB driver/filter setup must be opt-in, per-device, signature/hash verified and reversible. Do not disable driver-signature enforcement or Memory Integrity, and do not globally replace Apple's USB stack.