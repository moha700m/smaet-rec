# SMAET REC

<div dir="rtl">

استوديو تسجيل بسيط لويندوز 11: اعرض شاشة الآيفون وشاشة الكمبيوتر معًا، ارسم واشرح، ثم اضغط «تسجيل».

## التثبيت

1. نزّل ملف `SmaetRec-Setup-….exe` من صفحة [Releases](../../releases) (أو من نتائج آخر تشغيل في تبويب Actions).
2. شغّله واضغط «التالي» حتى ينتهي. لا تحتاج لتثبيت أي شيء آخر.
3. افتح **SMAET REC** من سطح المكتب.

> المثبّت غير موقّع بشهادة تجارية، لذلك قد يظهر تحذير SmartScreen («Windows protected your PC»). اضغط **More info** ثم **Run anyway**. الكود كامل ومفتوح في هذا المستودع.

يوجد أيضًا ملف مضغوط `SmaetRec-…-win-x64.zip` يعمل بدون تثبيت: فُكّ الضغط وشغّل `SmaetRec.exe`.

## طريقة الاستخدام

| الزر | ماذا يفعل |
|---|---|
| **ربط الآيفون** | يعرض خطوات الربط بالواي فاي أو بالكابل |
| **شاشة الكمبيوتر** | اختر الشاشة كاملة أو نافذة واحدة |
| **رسم** | قلم، تظليل، سهم، دائرة، مستطيل، نص، تمويه، ممحاة، ضوء، عدسة، تراجع وإعادة |
| **مؤثرات** | تكبير وتصغير، إبراز المؤشر، حركة النقر واللمس، ظلال، خلفيات |
| **تسجيل** / **إيقاف** | يبدأ التسجيل وينهيه. الفيديو يُحفظ في مجلد `الفيديو\SMAET REC` |

في المعاينة: اسحب الآيفون أو الشاشة لتحريكهما، اسحب الزوايا لتغيير الحجم، واسحب الدائرة العلوية للتدوير. عجلة الفأرة تكبّر وتصغّر.

### ربط الآيفون بالواي فاي (AirPlay)

1. الآيفون والكمبيوتر على نفس شبكة الواي فاي.
2. افتح مركز التحكم في الآيفون ← «انعكاس الشاشة».
3. اختر **SMAET REC**.

إذا لم يظهر الاسم في الآيفون:

1. من نافذة «ربط الآيفون» اضغط **«تشخيص الاتصال»** ثم **«إصلاح الاتصال»**. يضيف هذا قاعدة جدار حماية للتطبيق وخدمة mDNS، ويحوّل الشبكة العامة إلى خاصة عند الحاجة (قد يطلب ويندوز إذن المدير).
2. تأكد أن الآيفون والكمبيوتر على **نفس شبكة Wi-Fi**، وأن الشبكة **خاصة** لا عامة، وأن VPN أو عزل العملاء (AP Isolation) غير مفعّل.
3. أعد تشغيل AirPlay من زر «إعادة الاتصال»، ثم أعد فتح «انعكاس الشاشة» في الآيفون.

**بخصوص التعريفات:** AirPlay عبر Wi‑Fi **لا يحتاج أي تعريفات iPhone**. تعريفات Apple مطلوبة فقط لطريقة USB عبر شبكة «نقطة الاتصال الشخصية»، وتُثبّت من تطبيق **Apple Devices** الرسمي في متجر مايكروسوفت (زر «تثبيت Apple Devices» في نافذة الربط). لا يمكن تضمين تعريفات Apple داخل التطبيق لأنها ملكية خاصة بـApple.

### ربط الآيفون بالكابل (USB)

1. ثبّت تطبيق **Apple Devices** من متجر مايكروسوفت (أو iTunes).
2. وصّل الآيفون بالكابل واضغط «وثوق».
3. شغّل «نقطة اتصال شخصية» في الآيفون.
4. مركز التحكم ← «انعكاس الشاشة» ← **SMAET REC**.

بهذه الطريقة تنتقل الصورة عبر الكابل بدل الواي فاي. نافذة «ربط الآيفون» تعرض حالة كل خطوة.

## ما تم التحقق منه

يُبنى التطبيق ويُختبر تلقائيًا على Windows في كل تحديث: رسم المشهد، التقاط شاشة حقيقية، تسجيل 720p/30 و1080p/60 وقراءة الملف والتحقق من أبعاده ومعدل إطاره ومدته، كتابة المسار الصوتي، فك H.264، جلسة AirPlay حتى إعداد البث مع عميل محاكى، تشغيل الواجهة بالعربية والإنجليزية، وتثبيت المثبّت وإزالته.

لم يتوفر جهاز iPhone حقيقي في بيئة البناء، لذلك لم يُختبر الاستقبال من هاتف فعلي أو إصدار iOS محدد. الاتصال يحتاج شبكة محلية واحدة وسماح جدار الحماية. صوت الآيفون لا يُسجَّل في هذه النسخة.

## ما يجب أن تعرفه

- **صوت الآيفون لا يُسجَّل** في هذه النسخة. يُسجَّل الميكروفون وصوت الكمبيوتر.
- المحتوى المحمي (مثل تطبيقات الأفلام) يظهر أسود، وهذا من الآيفون نفسه.
- نافذة البرنامج لا تظهر داخل التسجيل، حتى لا ترى الاستوديو داخل نفسه.

</div>

---

## English

A simple recording studio for Windows 11: show your iPhone and your computer screen together, draw and explain, press Record.

### Install

Download `SmaetRec-Setup-….exe` from [Releases](../../releases) (or from the latest run in the Actions tab) and run it. Nothing else is needed — the .NET runtime is included. A portable `SmaetRec-…-win-x64.zip` is also built: unzip and run `SmaetRec.exe`.

The installer is not commercially code-signed, so SmartScreen may warn (“Windows protected your PC”). Choose **More info → Run anyway**. The full source is in this repository.

Requires Windows 10 version 2004 or later (built for Windows 11), 64-bit.

### Use

- **Connect iPhone** — shows the steps for Wi-Fi (AirPlay) or USB cable, with live status.
- **Computer screen** — pick a whole screen or one window.
- **Draw** — pen, highlighter, arrow, circle, rectangle, text, blur, eraser, spotlight, magnifier, undo/redo.
- **Effects** — zoom, cursor highlight, click/touch ripple, shadows, backgrounds.
- **Record / Stop** — MP4 (H.264 + AAC) saved to `Videos\SMAET REC`.

In the preview, drag a layer to move it, drag a corner to resize, drag the top handle to rotate. The mouse wheel zooms. Settings (gear, top corner) hold language, quality (720p / 1080p / 4K), 30 or 60 fps, sound sources, save folder, and save/open project. The layout and drawings are saved automatically and restored next time.

### Connecting the iPhone

**Wi-Fi (AirPlay):** same Wi-Fi network → Control Center → Screen Mirroring → **SMAET REC**.

If the name never appears: open **Connect iPhone → Connection diagnostics → Repair connection**. This adds the app's firewall and mDNS rules and sets a Public network to Private when needed (Windows may ask for administrator rights). Also check that both devices are on the same Wi-Fi, VPN/AP isolation is off, and the network is Private.

**Drivers:** AirPlay over Wi-Fi needs **no iPhone drivers**. Apple's drivers are only needed for the USB hotspot network, installed from the official **Apple Devices** app in the Microsoft Store (button in the Connect window). Apple's proprietary drivers cannot be bundled in this app.

**USB cable:** install *Apple Devices* (Microsoft Store) or iTunes → plug in and tap *Trust* → turn on *Personal Hotspot* → Control Center → Screen Mirroring → **SMAET REC**. The picture then travels over the cable's network link instead of Wi-Fi. This is AirPlay over the wire, not a raw USB video capture: Windows has no supported driver for raw iPhone screen capture, and the unofficial route replaces Apple's driver and breaks syncing, so it is deliberately not used.

### Limitations and what has been tested

Tested automatically on every build (GitHub Windows runner): drawing of the scene, real screen capture, 720p/30 and 1080p/60 recordings read back and checked (size, frame rate, orientation, duration), sound track writing, H.264 decoding, the AirPlay conversation up to stream setup against a simulated client, app start-up in Arabic and English, installer install/uninstall.

**Not tested on real hardware** (no device was available during development):

- Receiving from a physical iPhone, over Wi-Fi or USB. The receiver follows the same protocol as other open-source AirPlay receivers, but it has not been confirmed against a real phone or against specific iOS versions.
- Microphone and system-sound capture from real devices (the build machine has no sound hardware; the mixing and file-writing paths are tested with generated sound).
- Hardware video encoders (NVIDIA / Intel / AMD) and 4K at 60 fps. Windows picks a hardware encoder automatically when one exists; otherwise the built-in software encoder is used.

Known gaps: iPhone sound is not recorded; DRM-protected iPhone content appears black; touches on the iPhone are not transmitted by AirPlay, so the touch ripple is placed by clicking on the preview.

### Build from source

```bash
dotnet build SmaetRec.sln -c Release
```

```bash
dotnet test tests/SmaetRec.Tests
```

```bash
dotnet test tests/SmaetRec.WinTests
```

The first two work on any OS with the .NET 8 SDK; the Windows tests and running the app need Windows. `--dev-simulated-source` starts the app with moving test pictures (clearly labelled as a simulation) instead of real devices.

Project layout: `SmaetRec.Core` (scene, drawings, projects, text), `SmaetRec.AirPlay` (receiver), `SmaetRec.Media` (capture, compositor, recorder, audio), `SmaetRec.App` (WPF interface). Third-party code is listed in `THIRD-PARTY-NOTICES.md`.

### License

SMAET REC is released under the MIT License (see `LICENSE`). Third-party components keep their own licenses; see `THIRD-PARTY-NOTICES.md`.
