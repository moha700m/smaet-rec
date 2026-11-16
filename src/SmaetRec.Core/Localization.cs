using System.ComponentModel;

namespace SmaetRec.Core;

/// <summary>All user-visible text, Arabic first. Bind in XAML with {Binding [Key], Source={x:Static core:Loc.Instance}}.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _language = "ar";
    public string Language
    {
        get => _language;
        set
        {
            value = value == "en" ? "en" : "ar";
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }

    public bool IsRtl => _language == "ar";

    public string this[string key] => T(key);

    public static string T(string key) =>
        Table.TryGetValue(key, out var v) ? (Instance._language == "en" ? v.En : v.Ar) : key;

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    public static IReadOnlyDictionary<string, (string Ar, string En)> All => Table;

    private static readonly Dictionary<string, (string Ar, string En)> Table = new()
    {
        // bottom bar
        ["ConnectPhone"] = ("ربط الآيفون", "Connect iPhone"),
        ["ShowScreen"] = ("شاشة الكمبيوتر", "Computer screen"),
        ["Draw"] = ("رسم", "Draw"),
        ["Effects"] = ("مؤثرات", "Effects"),
        ["Record"] = ("تسجيل", "Record"),
        ["Stop"] = ("إيقاف", "Stop"),
        ["Arrange"] = ("ترتيب", "Arrange"),
        // top bar / status
        ["Settings"] = ("الإعدادات", "Settings"),
        ["PhoneConnected"] = ("الآيفون متصل", "iPhone connected"),
        ["PhoneWaiting"] = ("بانتظار الآيفون", "Waiting for iPhone"),
        ["PhoneOff"] = ("الآيفون غير متصل", "iPhone not connected"),
        ["PhoneError"] = ("تعذّر تشغيل استقبال الآيفون", "iPhone receiver could not start"),
        ["ScreenOn"] = ("الشاشة ظاهرة", "Screen showing"),
        ["ScreenOff"] = ("الشاشة مخفية", "Screen hidden"),
        ["Recording"] = ("جارٍ التسجيل", "Recording"),
        ["Ready"] = ("جاهز", "Ready"),
        ["MicOn"] = ("الميكروفون يُسجَّل", "Microphone is recording"),
        ["MicOff"] = ("بدون ميكروفون", "No microphone"),
        ["SystemAudioOn"] = ("صوت الكمبيوتر يُسجَّل", "Computer sound is recording"),
        ["SystemAudioOff"] = ("بدون صوت الكمبيوتر", "No computer sound"),
        ["PhoneAudioNote"] = ("صوت الآيفون لا يُسجَّل", "iPhone sound is not recorded"),
        // arrange bar
        ["SideBySide"] = ("جنبًا إلى جنب", "Side by side"),
        ["PhoneOverScreen"] = ("الآيفون فوق الشاشة", "iPhone over screen"),
        ["ResetLayout"] = ("إعادة الترتيب", "Reset"),
        ["Rotate"] = ("تدوير", "Rotate"),
        ["Orientation"] = ("طولي / عرضي", "Portrait / landscape"),
        ["HidePhone"] = ("إخفاء الآيفون", "Hide iPhone"),
        ["ShowPhone"] = ("إظهار الآيفون", "Show iPhone"),
        ["HideScreen"] = ("إخفاء الشاشة", "Hide screen"),
        ["ArrangeHint"] = ("اسحب لتحريك، واسحب الزوايا لتغيير الحجم", "Drag to move, drag the corners to resize"),
        // draw bar
        ["Pen"] = ("قلم", "Pen"),
        ["Highlighter"] = ("تظليل", "Highlighter"),
        ["Arrow"] = ("سهم", "Arrow"),
        ["Circle"] = ("دائرة", "Circle"),
        ["Rectangle"] = ("مستطيل", "Rectangle"),
        ["Text"] = ("نص", "Text"),
        ["Eraser"] = ("ممحاة", "Eraser"),
        ["Undo"] = ("تراجع", "Undo"),
        ["Redo"] = ("إعادة", "Redo"),
        ["ClearAll"] = ("مسح الكل", "Clear all"),
        ["Spotlight"] = ("ضوء", "Spotlight"),
        ["Magnifier"] = ("عدسة", "Magnifier"),
        ["Blur"] = ("تمويه", "Blur"),
        ["Color"] = ("اللون", "Colour"),
        ["Thickness"] = ("السماكة", "Thickness"),
        ["TypeText"] = ("اكتب النص ثم اضغط Enter", "Type your text, then press Enter"),
        // effects bar
        ["ZoomIn"] = ("تكبير", "Zoom in"),
        ["ZoomOut"] = ("تصغير", "Zoom out"),
        ["ZoomReset"] = ("حجم عادي", "Normal size"),
        ["CursorHighlight"] = ("إبراز المؤشر", "Highlight cursor"),
        ["ClickEffect"] = ("حركة النقر واللمس", "Click and touch ripple"),
        ["Shadows"] = ("ظلال", "Shadows"),
        ["Background"] = ("الخلفية", "Background"),
        // connect dialog
        ["ConnectTitle"] = ("ربط الآيفون", "Connect your iPhone"),
        ["WifiTab"] = ("واي فاي (AirPlay)", "Wi-Fi (AirPlay)"),
        ["UsbTab"] = ("كابل USB", "USB cable"),
        ["WifiStep1"] = ("تأكد أن الآيفون والكمبيوتر على نفس شبكة الواي فاي", "Put the iPhone and this computer on the same Wi-Fi"),
        ["WifiStep2"] = ("افتح مركز التحكم في الآيفون (اسحب من أعلى اليمين)", "Open Control Center on the iPhone (swipe down from the top right)"),
        ["WifiStep3"] = ("اضغط على «انعكاس الشاشة»", "Tap “Screen Mirroring”"),
        ["WifiStep4"] = ("اختر «{0}»", "Choose “{0}”"),
        ["UsbStep1"] = ("وصّل الآيفون بالكمبيوتر بالكابل واضغط «وثوق» على الآيفون", "Plug the iPhone in with its cable and tap “Trust” on the iPhone"),
        ["UsbStep2"] = ("يلزم تعريف Apple USB وناقل usbmux سليم للتعرّف على الآيفون؛ الفحص يختبر الرد الفعلي لا اسم الخدمة فقط", "The Apple USB driver and a working usbmux transport are required; diagnostics test a real protocol reply, not just a service name"),
        ["UsbStep3"] = ("اترك الآيفون مفتوحًا واضغط «Connect USB» بالأسفل — لا تحتاج إلى انعكاس الشاشة أو نقطة الاتصال", "Keep the iPhone unlocked and press “Connect USB” below — Screen Mirroring and Personal Hotspot are not required"),
        ["UsbNote"] = ("الاتصال المباشر يحتاج حزمة Apple USB وردًا صحيحًا من usbmux على المنفذ 27015، إضافة إلى مرشح التقاط خاص بهذا الآيفون. الفحص يوضح كل مكوّن على حدة. لعرض الآيفون بدون OBS اضغط «عرض الآيفون عبر 3uTools»، وافتح Toolbox ← Realtime Screen؛ ستظهر نافذته في القائمة المتجددة لاختيارها إلى معاينة SMAET REC.", "Direct USB requires the Apple USB package, a valid usbmux response on port 27015, and this iPhone's capture filter. Diagnostics report each component separately. To show the iPhone without OBS, press “iPhone screen via 3uTools” and open Toolbox → Realtime Screen; its window appears in the live-updating list for selection into the SMAET REC preview."),
        ["UsbPhoneFound"] = ("تم العثور على آيفون موصول بالكابل", "An iPhone is plugged in"),
        ["UsbPhoneMissing"] = ("لا يوجد آيفون موصول بالكابل", "No iPhone is plugged in"),
        ["UsbDriverFound"] = ("حزمة تعريف Apple USB موجودة", "Apple USB driver package is installed"),
        ["UsbDriverMissing"] = ("حزمة تعريف Apple USB غير موجودة — ثبّت Apple Devices أو دعم Apple للأجهزة", "Apple USB driver package is missing — install Apple Devices or Apple Mobile Device Support"),
        ["UsbServiceRunning"] = ("خدمة Apple Mobile Device مثبتة وتعمل", "Apple Mobile Device Service is installed and running"),
        ["UsbServiceStopped"] = ("خدمة Apple Mobile Device غير مثبتة أو متوقفة؛ قد يعمل usbmux بديلًا إذا اجتاز فحص البروتوكول", "Apple Mobile Device Service is missing or stopped; a compatible usbmux provider may still pass the protocol check"),
        ["UsbParentReady"] = ("تعريف الجهاز الأب سليم: USB Composite Device (usbccgp)", "Phone parent driver is USB Composite Device (usbccgp)"),
        ["UsbParentWrong"] = ("تعريف الجهاز الأب غير متوقع ({0}) — افتح إصلاح تعريف USB المتقدم", "Unexpected phone parent driver ({0}) — open advanced USB driver repair"),
        ["UsbParentNoPhone"] = ("تعريف الجهاز الأب: يظهر عند توصيل الآيفون", "Phone parent driver: shown when the iPhone is connected"),
        ["UsbMuxReady"] = ("ناقل Apple usbmux يرد بشكل صحيح على ListDevices (المنفذ 27015)", "Apple usbmux successfully answered ListDevices (port 27015)"),
        ["UsbMuxConflict"] = ("المنفذ 27015 مشغول بواسطة {0} لكنه لا يرد كبروتوكول Apple usbmux؛ لا توقف العملية تلقائيًا", "Port 27015 is occupied by {0}, but it did not answer as Apple usbmux; the process was not stopped"),
        ["UsbMuxUnavailable"] = ("لا يوجد رد usbmux صالح على 127.0.0.1:27015؛ لا يمكن تأكيد اتصال iTunes/Apple", "No valid usbmux response on 127.0.0.1:27015; the iTunes/Apple transport cannot be confirmed"),
        ["UsbCaptureFilterFound"] = ("مرشح التقاط USB libusb0 مرتبط بهذا الآيفون", "The libusb0 USB capture filter is attached to this iPhone"),
        ["UsbCaptureFilterMissing"] = ("مرشح التقاط USB libusb0 غير مثبت لهذا الآيفون — افتح إعداد/إصلاح USB", "The libusb0 capture filter is not installed for this iPhone — open USB setup / repair"),
        ["UsbCaptureFilterNoPhone"] = ("مرشح التقاط USB الخاص بالجهاز يظهر عند توصيل الآيفون", "The per-device USB capture filter is shown when the iPhone is connected"),
        ["DiagAppleMuxReady"] = ("اختبار usbmux ناجح: استقبل ListDevices ردًا صحيحًا على المنفذ 27015", "usbmux test passed: a valid ListDevices response was received on port 27015"),
        ["DiagAppleMuxConflict"] = ("المنفذ 27015 مشغول بواسطة {0} لكن الرد ليس Apple usbmux؛ يوجد تعارض محتمل. لم يتم إيقاف أي عملية.", "Port 27015 is occupied by {0}, but the reply is not Apple usbmux; a possible conflict exists. No process was stopped."),
        ["DiagAppleMuxUnavailable"] = ("لم يصل رد usbmux صالح من 127.0.0.1:27015؛ وجود الهاتف في الصور لا يثبت عمل هذا الاتصال.", "No valid usbmux response came from 127.0.0.1:27015; seeing the phone in Photos does not prove this transport works."),
        ["DiagAppleMuxLegacy"] = ("حزمة Apple والخدمة تعملان، لكن اختبار ListDevices لم يؤكد ناقل usbmux بعد", "The Apple package and service are running, but the ListDevices probe has not confirmed usbmux yet"),
        ["DiagCaptureFilterFound"] = ("مرشح libusb0 مثبت على الجهاز الأب لهذا الآيفون", "The libusb0 filter is attached to this iPhone's USB parent"),
        ["DiagCaptureFilterMissing"] = ("مرشح libusb0 غير مثبت على هذا الآيفون؛ افتح مدير تعريف iPhoneMirror لإعداده", "The libusb0 filter is missing for this iPhone; open the iPhoneMirror driver manager to set it up"),
        ["DiagCopyUsbReport"] = ("نسخ تقرير USB", "Copy USB report"),
        ["GetAppleDevices"] = ("تثبيت Apple Devices", "Get Apple Devices"),
        ["Reconnect"] = ("إعادة الاتصال", "Reconnect"),
        ["Close"] = ("إغلاق", "Close"),
        ["ReceiverRunning"] = ("الكمبيوتر جاهز ويظهر في الآيفون باسم «{0}»", "This computer is ready and appears on the iPhone as “{0}”"),
        ["ReceiverNoNetwork"] = ("لا توجد شبكة جاهزة — وصّل الكمبيوتر بشبكة Wi-Fi أو بكابل الآيفون", "No network ready — connect this computer to Wi-Fi or the iPhone cable"),
        ["DiagTitle"] = ("تشخيص الاتصال", "Connection diagnostics"),
        ["DiagSubtitle"] = ("هذه الفحوصات تشرح سبب عدم ظهور SMAET REC في قائمة انعكاس الشاشة، ويمكنها إصلاح أشهر أسباب ويندوز.", "These checks explain why SMAET REC may not appear in Screen Mirroring, and can fix the common Windows causes."),
        ["DiagRepair"] = ("إصلاح الاتصال", "Repair connection"),
        ["DiagNetworkSettings"] = ("إعدادات الشبكة", "Network settings"),
        ["DiagInstallDrivers"] = ("تثبيت Apple Devices", "Install Apple Devices"),
        ["DiagClose"] = ("إغلاق", "Close"),
        ["DiagLog"] = ("آخر رسائل AirPlay", "Recent AirPlay messages"),
        ["DiagNoLog"] = ("لا توجد رسائل بعد.", "No messages yet."),
        ["DiagHint"] = ("AirPlay عبر Wi-Fi لا يحتاج تعريفات Apple. أمّا USB المباشر فيحتاج تعريف Apple USB، وردًا صحيحًا من usbmux، ومرشح التقاط على الآيفون المحدد. التشخيص يقرأ هذه الحالات فقط ولا يغيّر التعريفات.", "AirPlay over Wi-Fi needs no Apple drivers. Direct USB requires the Apple USB driver, a valid usbmux reply and a capture filter on the selected iPhone. Diagnostics are read-only and do not change drivers."),
        ["DiagAppleSupportInstalled"] = ("تعريف Apple USB وخدمة Apple Mobile Device جاهزان", "Apple USB driver and Apple Mobile Device service are ready"),
        ["DiagAppleSupportMissing"] = ("مكوّنات دعم Apple للأجهزة غير مكتملة", "Apple device support components are incomplete"),
        ["DiagAppleUsbDriverMissing"] = ("حزمة تعريف Apple USB غير موجودة — ثبّت Apple Devices أو أصلح دعم Apple من زر إصلاح USB", "Apple USB driver package is missing — install Apple Devices or repair Apple support from USB driver setup"),
        ["DiagAppleServiceStopped"] = ("حزمة تعريف Apple موجودة لكن خدمة Apple Mobile Device متوقفة أو غير مثبتة", "Apple USB driver package exists, but Apple Mobile Device Service is stopped or missing"),
        ["DiagAppleParentDriverWrong"] = ("تعريف الجهاز الأب الحالي هو {0} وليس USB Composite Device (usbccgp). لا تغيّره يدويًا؛ افتح مدير iPhoneMirror المتقدم لمراجعة الربط مع تأكيد.", "The phone parent driver is {0}, not USB Composite Device (usbccgp). Do not change it manually; review parent binding in the iPhoneMirror advanced driver manager with confirmation."),
        ["DiagRepairing"] = ("جارٍ تطبيق الإصلاح… قد يطلب ويندوز إذن المدير.", "Applying the repair… Windows may ask for administrator permission."),
        ["DiagRepairDone"] = ("تم تحديث قواعد الاتصال. أعد المحاولة من الآيفون.", "Connection rules updated. Try again from the iPhone."),
        ["DiagRepairFailed"] = ("تعذّر الإصلاح: {0}", "Repair failed: {0}"),
        ["DiagAirPlayConnected"] = ("AirPlay يعمل — {0} متصل الآن", "AirPlay is running — {0} is connected"),
        ["DiagAirPlayRunning"] = ("AirPlay يعمل — المنفذ {0} — {1} واجهة شبكة", "AirPlay is running — port {0} — {1} network adapter(s)"),
        ["DiagAirPlayFailed"] = ("تعذّر تشغيل AirPlay: {0}", "AirPlay could not start: {0}"),
        ["DiagAirPlayStopped"] = ("AirPlay متوقف — أعد التشغيل من نافذة الربط", "AirPlay is stopped — restart it from the Connect window"),
        ["DiagNetworkUnknown"] = ("تعذّر قراءة حالة الشبكة.", "Could not read the network state."),
        ["DiagNetworkPrivate"] = ("خاصة", "Private"),
        ["DiagNetworkDomain"] = ("نطاق عمل", "Domain"),
        ["DiagNetworkPublic"] = ("عامة", "Public"),
        ["DiagNetworkUnknownKind"] = ("غير معروفة", "Unknown"),
        ["DiagNetworkNoAddress"] = ("لا يوجد عنوان IPv4", "no IPv4 address"),
        ["DiagNetworkLine"] = ("الشبكة: {0} — {1} · {2}", "Network: {0} — {1} · {2}"),
        ["DiagFirewallAllowed"] = ("جدار الحماية: لديك قاعدة سماح للتطبيق", "Firewall: an allow rule for the app exists"),
        ["DiagFirewallBlocked"] = ("جدار الحماية: لا توجد قاعدة سماح — اضغط «إصلاح الاتصال»", "Firewall: no allow rule — press “Repair connection”"),
        ["DiagFirewallUnknown"] = ("جدار الحماية: تعذّر التحقق — يمكنك تجربة الإصلاح", "Firewall: could not check — you can still try the repair"),
        ["DiagDriverInstalled"] = ("تعريفات Apple مثبتة (لازمة لشبكة USB)", "Apple device software installed (needed for the USB network)"),
        ["DiagDriverMissing"] = ("تعريفات Apple غير مثبتة — لازمة فقط لطريقة USB", "Apple device software missing — only needed for the USB method"),
        ["DiagVideoWaiting"] = ("الفيديو: لم تصل حزم من الآيفون بعد — اختر SMAET REC من «انعكاس الشاشة».", "Video: no packets from the iPhone yet — choose SMAET REC in Screen Mirroring."),
        ["DiagVideoLine"] = ("الفيديو: {0} حزمة · {1} صورة · {2} إطار مفكوك · {3} تالفة", "Video: {0} packets · {1} pictures · {2} frames decoded · {3} invalid"),
        ["DiagVideoCodec"] = ("الفيديو: الآيفون يرسل HEVC (H.265) وهذا الإصدار يفك H.264 فقط.", "Video: the iPhone is sending HEVC (H.265); this build decodes H.264 only."),
        ["DiagCopyLog"] = ("نسخ السجل", "Copy log"),
        ["DiagCopied"] = ("تم نسخ سجل AirPlay.", "AirPlay log copied."),
        ["DiagCopyFailed"] = ("تعذّر النسخ إلى الحافظة.", "Could not copy to the clipboard."),
        ["Unknown"] = ("خطأ غير معروف", "unknown error"),
        ["ReceiverFailed"] = ("تعذّر التشغيل: {0}", "Could not start: {0}"),
        ["FirewallHint"] = ("إذا لم يظهر الاسم في الآيفون، اضغط «تشخيص الاتصال» ثم «إصلاح الاتصال» ليضيف قاعدة جدار الحماية تلقائيًا.", "If the name does not appear on the iPhone, open “Connection diagnostics” and press “Repair connection” to add the firewall rule automatically."),
        ["PhonePlaceholder"] = ("اربط الآيفون", "Connect iPhone"),
        ["ScreenPlaceholder"] = ("اضغط «شاشة الكمبيوتر»", "Press “Computer screen”"),
        // screen picker
        ["PickScreenTitle"] = ("ماذا تريد أن تعرض؟", "What do you want to show?"),
        ["WholeScreen"] = ("الشاشة كاملة", "Whole screen"),
        ["ScreenN"] = ("الشاشة {0}", "Screen {0}"),
        ["Windows"] = ("نافذة واحدة", "One window"),
        ["ThreeUToolsPickerTitle"] = ("اختيار نافذة 3uTools", "Select a 3uTools window"),
        ["ThreeUToolsPickerInstructions"] = ("افتح 3uTools ثم اختر Toolbox ← Realtime Screen. ستظهر نافذة العرض هنا تلقائيًا عند فتحها؛ اخترها لعرضها داخل معاينة SMAET REC. اترك النافذة مفتوحة وغير مصغّرة.", "Open 3uTools and choose Toolbox → Realtime Screen. The viewer window appears here automatically when it opens; select it to show it in the SMAET REC preview. Keep the viewer open and not minimized."),
        ["ThreeUToolsRefresh"] = ("تحديث القائمة", "Refresh list"),
        ["ThreeUToolsAllWindows"] = ("عرض كل النوافذ", "Show all windows"),
        ["ThreeUToolsNoWindow"] = ("بانتظار نافذة Realtime Screen من 3uTools… افتح Toolbox ← Realtime Screen. تتحدث القائمة تلقائيًا.", "Waiting for the 3uTools Realtime Screen window… Open Toolbox → Realtime Screen. The list refreshes automatically."),
        ["ThreeUToolsWindowCount"] = ("تم العثور على {0} نافذة. اختر نافذة عرض الآيفون.", "Found {0} window(s). Choose the iPhone viewer window."),
        ["ThreeUToolsWindowClosed"] = ("أُغلقت النافذة قبل اختيارها. حدّث القائمة واختر نافذة أخرى.", "The window closed before selection. Refresh the list and choose another window."),
        ["ThreeUToolsScanFailed"] = ("تعذّر فحص النوافذ: {0}", "Could not inspect windows: {0}"),
        ["Open3uToolsScreen"] = ("عرض الآيفون عبر 3uTools", "iPhone screen via 3uTools"),
        ["ThreeUToolsWindowMissing"] = ("لم أجد نافذة 3uTools ظاهرة. تأكد من فتح Realtime Screen وأن نافذته غير مصغّرة.", "No visible 3uTools window was found. Open Realtime Screen and make sure its window is not minimized."),
        ["ThreeUToolsNotInstalled"] = ("لم أجد 3uTools مثبتًا في المسارات المعتادة أو سجل Windows. ثبّت 3uTools من موقعه الرسمي ثم جرّب مرة أخرى.", "3uTools was not found in its usual install paths or the Windows App Paths registry. Install 3uTools from its official website and try again."),
        ["ThreeUToolsOwnerMissing"] = ("تعذّر إرسال نافذة العرض إلى المعاينة الرئيسية.", "Could not route the viewer window to the main preview."),
        ["ThreeUToolsOpenFailed"] = ("تعذّر فتح 3uTools أو الوصول إلى نافذة العرض: {0}", "Could not open 3uTools or access its viewer window: {0}"),
        ["Cancel"] = ("إلغاء", "Cancel"),
        // settings
        ["Language"] = ("اللغة", "Language"),
        ["Quality"] = ("جودة الفيديو", "Video quality"),
        ["Smoothness"] = ("السلاسة", "Smoothness"),
        ["Fps30"] = ("30 إطار", "30 fps"),
        ["Fps60"] = ("60 إطار", "60 fps"),
        ["Microphone"] = ("تسجيل الميكروفون", "Record microphone"),
        ["SystemAudio"] = ("تسجيل صوت الكمبيوتر", "Record computer sound"),
        ["SaveFolder"] = ("مكان حفظ الفيديو", "Where videos are saved"),
        ["Change"] = ("تغيير", "Change"),
        ["OpenFolder"] = ("فتح المجلد", "Open folder"),
        ["SaveProject"] = ("حفظ المشروع", "Save project"),
        ["OpenProject"] = ("فتح مشروع", "Open project"),
        ["PhoneName"] = ("اسم الكمبيوتر في الآيفون", "Name shown on the iPhone"),
        ["Project"] = ("المشروع", "Project"),
        ["ProjectFiles"] = ("مشروع SMAET REC", "SMAET REC project"),
        // messages
        ["Saved"] = ("تم حفظ الفيديو", "Video saved"),
        ["ShowVideo"] = ("عرض الفيديو", "Show video"),
        ["RecordFailed"] = ("تعذّر التسجيل: {0}", "Recording failed: {0}"),
        ["CaptureFailed"] = ("تعذّر عرض الشاشة: {0}", "Could not show the screen: {0}"),
        ["CaptureWindowClosed"] = ("نافذة العرض لم تعد متاحة. افتحها مرة أخرى ثم أعد اختيارها.", "The selected window is no longer available. Reopen it and choose it again."),
        ["ProjectSaved"] = ("تم حفظ المشروع", "Project saved"),
        ["ProjectOpenFailed"] = ("تعذّر فتح المشروع", "Could not open the project"),
        ["NothingToRecord"] = ("لا يوجد شيء للتسجيل. اعرض الشاشة أو اربط الآيفون أولًا", "Nothing to record yet. Show the screen or connect the iPhone first"),
        ["Simulation"] = ("محاكاة للتجربة", "SIMULATION"),
    };
}
