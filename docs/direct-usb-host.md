# Direct USB QuickTime host (separate GPL component)

SMAET REC remains MIT-licensed. The USB QuickTime implementation is kept as a separate executable/process so GPL source and its obligations do not silently become part of the MIT application.

## Upstream source

`third_party/iphone-usb-host` is a Git submodule pointing to RayrenSX/iPhoneMirror, pinned to a specific upstream commit. That project is GPL-3.0. Keep its source, notices, build system and resulting executable separately identifiable. Do not copy its source into `src/SmaetRec.*` or relabel it MIT.

The application-side IPC adapter is original SMAET REC code under the repository MIT license.

## IPC contract v1

The helper is expected at `resources/usb/iphone-usb-host.exe` and is launched with `--smaet-ipc --pipe <pipe-name> --device <UDID> --video h264-annexb`.

Each record consists of a 4-byte unsigned little-endian total length, then a 4-byte unsigned little-endian JSON-header length, then that many UTF-8 JSON bytes, then the optional binary payload. Status: `{"type":"status","message":"..."}`. Error: `{"type":"error","message":"..."}`. End: `{"type":"stopped"}`.

Video record JSON fields: `type=video`, `payloadBytes`, `timestampMicros`, and `keyFrame`. After the JSON header, exactly payloadBytes of H.264 Annex-B access-unit data follow.

The host must emit video only after completed QuickTime negotiation and receipt of a valid FEED video sample. Opening a USB handle is not streaming. Timestamps must be monotonic. Keyframe flags must reflect actual IDR frames. SPS/PPS must be supplied at stream start and after format changes. No simulated frames are permitted.

## Native host status

The upstream repository is linked as a separate GPL submodule; it is not yet an SMAET-specific host executable. A dedicated adapter/launcher must be built against the pinned upstream APIs and emit the IPC contract above before USB capture can be considered functional. The app intentionally fails clearly when the helper is missing; it never falls back to AirPlay, Wi-Fi, or fake video.

## Driver safety

USB driver/filter setup must be opt-in, per-device, signature/hash verified and reversible. Do not disable driver-signature enforcement or Memory Integrity, and do not globally replace Apple's USB stack.