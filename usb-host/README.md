# SMAET REC Direct USB Host

This directory contains the GPL-3.0-only out-of-process adapter for the pinned
[iPhoneMirror](https://github.com/RayrenSX/iPhoneMirror) capture engine.

The adapter resolves the Windows-selected Apple USB serial to the authoritative
usbmux UDID using `im_refresh_devices_ex`, creates a native capture session,
and forwards tightly packed decoded BGRA frames over the private named-pipe
protocol documented in `docs/direct-usb-host.md`.

## Build (Windows x64, Visual Studio 2022)

From a recursive checkout:

```powershell
git submodule update --init --recursive
cmake -S usb-host -B build/usb-host -A x64
cmake --build build/usb-host --config Release --target iphone-usb-host
```

The resulting runtime directory is `build/usb-host/package/usb`. It contains
the host executable, iPhoneMirror.Core.dll and libusb-1.0.dll. The application
launches the host from `usb/iphone-usb-host.exe`.

The MIT WPF process never loads the GPL DLL. Keep the adapter and its runtime
as a separate process and preserve this directory's GPL licensing notices.
A Windows CI workflow builds the native host and publishes a complete
self-contained SMAET REC application artifact.

## Device prerequisites

Install Apple Devices or desktop iTunes (Apple Mobile Device Support), connect
the iPhone with a data-capable cable, unlock it, and accept Trust This Computer.
USB capture can also require the upstream iPhoneMirror-compatible libusb0
filter driver on the target Apple USB device. Do not replace the Apple parent
device driver with WinUSB/Zadig.
