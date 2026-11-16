# Engineering review of comparable projects

Reviewed on 2026-11-16 from source (shallow clones of each default branch), not from descriptions.
Purpose: decide how SMAET REC gets an iPhone picture into its one preview/recording engine.

## What each project actually does

| Project | Licence | How the iPhone picture is obtained | Decode | Reconnect / failure handling | Distribution |
|---|---|---|---|---|---|
| [iPhoneMirror](https://github.com/RayrenSX/iPhoneMirror) | GPL-3.0-only | Own C++ core: Apple usbmux for identity/pairing, then the QuickTime USB screen protocol over a per-device `libusb0` filter (`src/Core/src/Transport`, `Protocol`, `Capture`). Stable C ABI (`CoreApi.h`). | In the core; publishes BGRA8 or NV12 frames | Explicit capture states and failure stages, USB configuration restored on stop, native tests | Native DLL + driver manager |
| [Mirrorlab](https://github.com/SimonDaBoss/mirrorlab) | App Apache-2.0; native helpers GPL-3.0-or-later | AirPlay: patched **UxPlay** helper. USB: helper built on the pinned **iPhoneMirror** protocol code (`native/iphone-usb.cpp`). Media crosses to the app over framed localhost streams. | WebCodecs in the renderer (`src/renderer/video.ts`); waits for a key frame, skips to the next key frame when the decode queue grows | "needKey" gate, key-frame request on backlog, decoder re-configure when closed | Electron portable ZIP |
| [USBMirror](https://github.com/elghifari/USBMirror) | GPL-3.0-only | Win32 frontend that loads the **iPhoneMirror v1.8.3** capture engine and calls `im_session_copy_latest_video_frame` (`src/main.cpp`) | Engine | Retry with growing delay, failure kinds mapped to plain messages ("Connected, but no video arrived…") | Single EXE + driver tool |
| [IosScreenCaptureTool](https://github.com/BieleckiLtd/IosScreenCaptureTool) | MIT | Shells out to **pymobiledevice3**: developer tunnel + developer screenshot service (`PymobiledeviceClient.cs`). Needs Developer Mode, Python, and administrator rights for the tunnel. | Still images | Re-polls the device list | ZIP + Python bootstrap |
| [libimobiledevice](https://github.com/libimobiledevice/libimobiledevice), [libusbmuxd](https://github.com/libimobiledevice/libusbmuxd) | LGPL-2.1 | Lockdown, pairing and usbmux client libraries. No screen video: the `screenshotr` service gives single developer screenshots only. | — | — | Libraries |
| [usbmuxd](https://github.com/libimobiledevice/usbmuxd) | GPL-2.0/3.0 | The usbmux daemon for Linux/macOS. On Windows that role is played by Apple Mobile Device Service on `127.0.0.1:27015`. | — | — | Daemon |

3uTools publishes no API or protocol for its viewer. Its own tutorial documents only the in-app path
**Toolbox → Realtime Screen** (https://www.3u.com/tutorial/details/145/…), which is what SMAET REC tells the user to open.

## Comparison on the points that matter

- **Start-up speed and frame rate.** The QuickTime USB path (iPhoneMirror and everything built on it) delivers the
  phone's own H.264 stream, up to 60 fps, within a few seconds of the handshake. The pymobiledevice3 screenshot path is
  a sequence of stills and needs Developer Mode; it is not a video path. AirPlay depends on Wi‑Fi quality.
- **Stability / cable handling.** The projects that work well all treat "process started" and "video arrived" as
  different things, name the stage that failed, and retry with a delay. USBMirror and Mirrorlab both do; SMAET REC now
  does the same (first-frame gate, stall watch, three timed reconnects, stage shown to the user).
- **Decoding.** Two workable designs: decode inside the capture engine (iPhoneMirror, USBMirror) or forward H.264 and
  decode in the app (Mirrorlab). For AirPlay SMAET REC decodes with the Windows H.264 decoder; like Mirrorlab's
  renderer it must start on a packet that really carries SPS/PPS. That gate was the cause of "pictures arrive, nothing
  is shown" and is now decided from the bytes.
- **Resource use.** Engine-side decode plus one BGRA copy per frame is the cheapest route into a Direct2D compositor.
  SMAET REC reads each USB frame from the pipe straight into the compositor's frame buffer (one copy, no per-frame
  allocation).
- **Drivers.** Every QuickTime-USB implementation needs Apple's USB driver and a per-device `libusb0` capture filter.
  None of the reviewed projects avoids this. Replacing the Apple parent driver (Zadig-style) breaks Apple software and
  is not done.
- **Licences.** The only complete, maintained Windows implementation of the USB screen protocol is GPL-3.0-only.
  libimobiledevice is LGPL but has no video. So GPL code must stay out of the MIT application process.

## Decision

1. **Direct USB**: keep iPhoneMirror's core as a **separate GPL-3.0 process** (`usb/iphone-usb-host.exe`, source in
   `usb-host/` and the pinned submodule), talking to the MIT app over a private named pipe with decoded BGRA frames.
   Ship the GPL licence, notices and corresponding source with every package. No GPL code is linked into `SmaetRec.exe`.
2. **AirPlay**: keep SMAET REC's own MIT receiver and the Windows decoder. UxPlay (GPL) is used as protocol reference
   only.
3. **3uTools Realtime Screen**: capture the viewer **window** with Windows.Graphics.Capture (GDI fallback) and feed it to
   the same scene as every other source. No injection, no private protocol, no intermediate file, no OBS.
4. **One engine**: USB frames and AirPlay frames enter the same phone layer (`MemoryFrameSource`); window and screen
   capture enter the screen layer; the compositor output is both the preview and the recorder input.
5. **Not adopted**: pymobiledevice3 screenshots (stills, Developer Mode, Python runtime, admin tunnel) and any driver
   replacement.

## What this review cannot settle

Which path works on a particular PC and iPhone/iOS version can only be shown on real hardware. The automated tests
cover the code on each side of the device; they do not replace a real-device acceptance run.
