# Direct USB capture host

SMAET REC's MIT WPF application starts a separate GPL-3.0-only native host.
The GPL engine and adapter are not loaded into the MIT process; media crosses
only a private Windows named pipe. The host source is under `usb-host/` and
the pinned upstream source is the `third_party/iphone-usb-host` submodule.

## Runtime and device selection

The application starts `resources/usb/iphone-usb-host.exe` with:

```text
--smaet-ipc --pipe <pipe-name> --device-serial <Windows-USB-serial> --video bgra8
```

The adapter enumerates devices through `im_refresh_devices_ex`, matches the
selected serial to exactly one attached device, and passes the authoritative
usbmux UDID to `im_session_create`. Ambiguous or missing matches fail closed.
It forwards decoded BGRA8 frames from the upstream C ABI.

## IPC v2 framing

Each record is:

1. 4-byte unsigned little-endian record length (bytes after this prefix)
2. 4-byte unsigned little-endian UTF-8 JSON header length
3. UTF-8 JSON header
4. optional binary payload

Records:

- Status: `{"type":"status","message":"..."}`
- Error: `{"type":"error","message":"..."}`
- Video: `{"type":"video","pixelFormat":"bgra8","width":1920,"height":1080,"stride":7680,"payloadBytes":8294400,"timestamp100ns":123456}` followed by tightly packed BGRA8 bytes
- Stop: `{"type":"stopped"}`

The payload size equals `stride * height`; stride equals `width * 4`.
Maximum record size is 64 MiB. The host validates dimensions and repacks
padded native rows before sending.

## Build and package

On Windows 10/11 x64 with Visual Studio 2022 and CMake 3.25+:

```powershell
git submodule update --init --recursive
cmake -S usb-host -B build/usb-host -A x64
cmake --build build/usb-host --config Release --target iphone-usb-host
```

CI builds the native engine and adapter, stages the runtime in
`src/SmaetRec.App/resources/usb`, runs managed tests, and publishes the
self-contained application. The release package also carries the GPL license,
notices, and corresponding source archive.

## Requirements

- Windows 10/11 x64.
- Apple Devices or desktop iTunes with Apple Mobile Device Support installed.
- Data-capable USB cable; iPhone unlocked; accept “Trust This Computer”.
- Upstream-compatible Apple USB capture interface and libusb0 filter driver
  where required. The upstream engine's configuration helper is packaged
  beside the host. Driver installation remains a separate privileged action;
  do not use Zadig to replace the Apple parent device driver or disable
  Windows driver-signature enforcement.

No fake frames or AirPlay fallback are used by the USB path. A real-device
acceptance test is required before claiming hardware compatibility.
