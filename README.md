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

### إصلاح 1.0.2 — الشاشة التي لا تظهر

إذا كان الآيفون يتصل ويظهر «متصل» لكن الصورة لا تظهر، فالسبب كان في فك تشفير الفيديو: الآيفون يشفّر كل صور المرآة كتدفق AES‑CTR واحد متصل، وأي حزمة طولها ليس من مضاعفات 16 بايت تترك بقية مفتاح التشفير للحزمة التالية. كان الإصدار السابق يبدأ كل حزمة من جديد، فتفسد كل الصور بعد أول حزمة. الآن يُكمل فك التشفير من حيث انتهى بالضبط (نفس سلوك UxPlay وRPiPlay والمستقبلات الحديثة). كذلك:

- يُقبل رد FairPlay الثاني بطول 164 أو 168 بايت (بعض إصدارات iOS ترسله 168).
- صار رد `/info` يحتوي `txtAirPlay` و`txtRAOP` كما يتوقع الآيفون.
- نافذة التشخيص تعرض الآن عدد حزم الفيديو والصور والإطارات المفكوكة والحزم التالفة، وزر «نسخ السجل».
- لم يعد الاتصال يُقطع إذا فشل التحقق من توقيع الاقتران؛ يُسجَّل الخطأ وتستمر الجلسة.

### إصلاح 1.0.3 — الحزم التالفة والتقطيع

بعد 1.0.2 بقيت حالة يظهر فيها «تالفة» في التشخيص ثم يتجمّد الفيديو. السبب: مفتاح الفيديو يُخلط مع سر ECDH **فقط إذا اكتمل الاقتران فعلاً**. بعض الجلسات تُكمل `pair-verify` عندنا لكن الآيفون يستخدم المفتاح المباشر، فيُفك التشفير بمفتاح خاطئ وتصبح كل حزمة «تالفة». الآن يجرّب المستقبل **المفتاحين** (FairPlay المباشر، وFairPlay+ECDH) على أول صور، ويختار تلقائيًا المفتاح الذي ينتج H.264 صالحًا، ويظهر اسمه في التشخيص.

كذلك:
- تُحوَّل طوابع وقت الآيفون إلى أزمنة نسبية صغيرة قبل تمريرها لمفكك Windows، لتجنّب تعليق المفكك أو تقطيع العرض.
- تُتجاهل حزم الإيقاف/الاستئناف الفارغة بدل اعتبارها خطأ في SPS/PPS.
- يُسجَّل نوع أول حزم المرآة والمفتاح الذي نجح، لتشخيص أسرع.

**بخصوص التعريفات:** AirPlay عبر Wi‑Fi **لا يحتاج أي تعريفات iPhone**. تعريفات Apple مطلوبة فقط لطريقة USB عبر شبكة «نقطة الاتصال الشخصية»، وتُثبّت من تطبيق **Apple Devices** الرسمي في متجر مايكروسوفت (زر «تثبيت Apple Devices» في نافذة الربط). لا يمكن تضمين تعريفات Apple داخل التطبيق لأنها ملكية خاصة بـApple.

### ربط الآيفون بالكابل (USB مباشر)

1. ثبّت تطبيق **Apple Devices** من متجر مايكروسوفت أو iTunes المكتبي، ليكون دعم أجهزة Apple موجودًا على ويندوز.
2. وصّل الآيفون بكابل بيانات، وافتح قفله، ثم اضغط «وثوق بهذا الكمبيوتر» على الهاتف.
3. افتح SMAET REC واضغط **Connect USB**. اترك الهاتف مفتوحًا حتى تظهر صورته.

هذا مسار التقاط QuickTime مباشر من USB؛ لا تحتاج إلى تشغيل «نقطة اتصال شخصية» أو فتح «انعكاس الشاشة». قبل الاستسلام، يجرّب محرك الالتقاط أوضاع التوافق الثلاثة (Demo ثم AirPlay ثم Aisi) بمهلة قصيرة لكل وضع، ولا يعتبر الاتصال ناجحًا إلا بعد وصول إطار فيديو حقيقي. نافذة الربط تفحص الآن **حزمة تعريف Apple USB** و**حالة خدمة Apple Mobile Device** كلًا على حدة؛ ظهور الهاتف في تطبيق الصور وحده لا يثبت اكتمال الاثنين. زر **USB driver setup / repair** يفتح مدير iPhoneMirror الذي يفحص ويصلح دعم Apple ومرشح التقاط USB بشكل منفصل عند الحاجة.

## ما تم التحقق منه

يُبنى التطبيق ويُختبر تلقائيًا على Windows في كل تحديث: رسم المشهد، التقاط شاشة حقيقية، تسجيل 720p/30 و1080p/60 وقراءة الملف والتحقق من أبعاده ومعدل إطاره ومدته، كتابة المسار الصوتي، فك H.264، وجلسة AirPlay كاملة مع عميل محاكى (اقتران، FairPlay، إعداد البث، وفك صور H.264 مشفّرة)، تشغيل الواجهة بالعربية والإنجليزية، وتثبيت المثبّت وإزالته.

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

### Fixed in 1.0.2 — the screen that never appeared

When the iPhone connected and the app said "connected" but no picture appeared, the cause was video decryption: the iPhone encrypts the whole mirror stream as **one continuous AES-CTR keystream**, and any packet whose length is not a multiple of 16 bytes leaves the tail of a keystream block for the next packet. The previous build restarted the cipher at every packet, corrupting every picture after the first one. Decryption now continues exactly where the previous packet stopped (the behaviour of UxPlay, RPiPlay and current receivers). Also:

- The second FairPlay reply is accepted at 164 **or** 168 bytes (some iOS versions send 168).
- `/info` now carries `txtAirPlay` and `txtRAOP` the way iOS expects.
- Connection diagnostics show video packets, pictures, decoded frames and invalid packets, plus a **Copy log** button.
- A failed pair-verify signature no longer drops the connection; it is logged and the session continues.

### Fixed in 1.0.3 — invalid packets and stuttering

After 1.0.2, some sessions still showed rising `invalid` packets and the video froze. The video key must be mixed with the ECDH secret **only when pairing really completed**; some sessions finish `pair-verify` on our side while the iPhone uses the direct FairPlay key, so every packet decrypted to garbage. The receiver now tries **both** derivations (direct FairPlay and FairPlay+ECDH) on the first pictures, picks the one that yields valid H.264 automatically, and shows the chosen key in diagnostics.

Also:
- iPhone timestamps are converted to small relative sample times before Windows Media Foundation, avoiding decoder stalls and stutter.
- Empty pause/resume packets are ignored instead of being reported as bad SPS/PPS.
- The first mirror packet types and the successful key variant are logged for faster diagnosis.

**Drivers:** AirPlay over Wi-Fi needs **no iPhone drivers**. Apple's drivers are only needed for the USB hotspot network, installed from the official **Apple Devices** app in the Microsoft Store (button in the Connect window). Apple's proprietary drivers cannot be bundled in this app.

**USB cable:** install *Apple Devices* (Microsoft Store) or iTunes → plug in and tap *Trust* → turn on *Personal Hotspot* → Control Center → Screen Mirroring → **SMAET REC**. The picture then travels over the cable's network link instead of Wi-Fi. This is AirPlay over the wire, not a raw USB video capture: Windows has no supported driver for raw iPhone screen capture, and the unofficial route replaces Apple's driver and breaks syncing, so it is deliberately not used.

### Limitations and what has been tested

Tested automatically on every build (GitHub Windows runner): drawing of the scene, real screen capture, 720p/30 and 1080p/60 recordings read back and checked (size, frame rate, orientation, duration), sound track writing, H.264 decoding, a full AirPlay session against a simulated iPhone (pairing, FairPlay, stream setup, and decryption of encrypted H.264 pictures), app start-up in Arabic and English, installer install/uninstall.

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


إذا كان 3uTools يعرض الآيفون لكن اتصال USB المباشر داخل SMAET REC لا يكتمل، افتح نافذة **ربط الآيفون** واضغط **عرض الآيفون عبر 3uTools**. يفتح البرنامج 3uTools ثم يعرض منتقيًا متجددًا لنوافذه. داخل 3uTools اختر **Toolbox → Realtime Screen**؛ ستظهر نافذة العرض في القائمة تلقائيًا. اخترها لتظهر داخل معاينة SMAET REC، مع استمرار أدوات الرسم والتأثيرات والتسجيل المعتادة، ومن دون OBS. لا تغيّر تعريفات 3uTools العاملة لمجرد استخدام هذا المسار.
