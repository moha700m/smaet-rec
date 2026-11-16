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
        ["UsbStep2"] = ("تأكد من تثبيت تطبيق Apple Devices أو iTunes على الكمبيوتر", "Make sure Apple Devices or iTunes is installed on this computer"),
        ["UsbStep3"] = ("اترك الآيفون مفتوحًا واضغط «Connect USB» بالأسفل — لا تحتاج إلى انعكاس الشاشة أو نقطة الاتصال", "Keep the iPhone unlocked and press “Connect USB” below — Screen Mirroring and Personal Hotspot are not required"),
        ["UsbNote"] = ("طريقة الكابل ترسل الصورة عبر شبكة الكابل بدل الواي فاي، وتحتاج تطبيق «Apple Devices» أو iTunes على الكمبيوتر. أما AirPlay عبر الواي فاي فلا يحتاج أي تعريفات iPhone.", "The cable method sends the picture over the cable network instead of Wi-Fi. It needs the “Apple Devices” app or iTunes on this computer. AirPlay over Wi-Fi needs no iPhone drivers."),
        ["UsbPhoneFound"] = ("تم العثور على آيفون موصول بالكابل", "An iPhone is plugged in"),
        ["UsbPhoneMissing"] = ("لا يوجد آيفون موصول بالكابل", "No iPhone is plugged in"),
        ["UsbDriverFound"] = ("برنامج Apple للأجهزة مثبّت", "Apple device software is installed"),
        ["UsbDriverMissing"] = ("برنامج Apple للأجهزة غير مثبّت — ثبّت «Apple Devices» من متجر مايكروسوفت", "Apple device software is missing — install “Apple Devices” from the Microsoft Store"),
        ["UsbNetworkUp"] = ("شبكة الكابل تعمل ({0})", "Cable network is working ({0})"),
        ["UsbNetworkDown"] = ("شبكة الكابل لا تعمل بعد — شغّل «نقطة اتصال شخصية»", "Cable network is not up yet — turn on “Personal Hotspot”"),
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
        ["DiagHint"] = ("AirPlay لا يحتاج تعريفات. تعريفات Apple مطلوبة فقط عند استخدام شبكة USB (Personal Hotspot). إذا كانت الشبكة «عامة» فسيحوّلها الإصلاح إلى «خاصة» ليسمح ويندوز بالاكتشاف.", "AirPlay needs no drivers. Apple drivers are only needed for the USB hotspot network. If the network is Public, the repair switches it to Private so Windows allows discovery."),
        ["DiagAppleSupportInstalled"] = ("خدمات Apple للأجهزة موجودة", "Apple device support is installed"),
        ["DiagAppleSupportMissing"] = ("خدمات Apple للأجهزة غير موجودة", "Apple device support is missing"),
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
        ["ProjectSaved"] = ("تم حفظ المشروع", "Project saved"),
        ["ProjectOpenFailed"] = ("تعذّر فتح المشروع", "Could not open the project"),
        ["NothingToRecord"] = ("لا يوجد شيء للتسجيل. اعرض الشاشة أو اربط الآيفون أولًا", "Nothing to record yet. Show the screen or connect the iPhone first"),
        ["Simulation"] = ("محاكاة للتجربة", "SIMULATION"),
    };
}
