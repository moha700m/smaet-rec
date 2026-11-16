# Third-party notices

SMAET REC includes or links to the following open-source software.

| Component | Use | Licence |
|---|---|---|
| [airplayreceiver](https://github.com/SteeBono/airplayreceiver) by Stefano Bono | FairPlay key handling code and tables in `src/SmaetRec.AirPlay/FairPlay/` (adapted) | MIT — full text in `src/SmaetRec.AirPlay/FairPlay/LICENSE-airplayreceiver.txt` |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) | Direct3D 11, Direct2D, DirectWrite, Media Foundation bindings | MIT |
| [NAudio](https://github.com/naudio/NAudio) | Microphone and system-sound capture (WASAPI) | MIT |
| [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) | Ed25519 / X25519 for AirPlay pairing | MIT |
| [Makaretu.Dns](https://github.com/richardschneider/net-dns) | Tests only: independent parser used to check the mDNS packets | MIT |
| [.NET runtime and WPF](https://github.com/dotnet) | Application runtime (bundled in the self-contained package) | MIT |

[UxPlay](https://github.com/FDH2/UxPlay) (GPL-3.0) was consulted as documentation of the AirPlay mirroring protocol. No UxPlay code is included.

AirPlay, iPhone and Apple TV are trademarks of Apple Inc. SMAET REC is not affiliated with or endorsed by Apple.

| [iPhoneMirror](https://github.com/RayrenSX/iPhoneMirror) and SMAET USB host adapter | Separate GPL-3.0-only native process under `usb-host/`, linked against the pinned upstream engine in `third_party/iphone-usb-host`; never loaded into the MIT WPF process | GPL-3.0-only; full license in `usb-host/LICENSE-GPL-3.0.txt`, upstream notices included in the corresponding-source archive |
