using System.ComponentModel;

namespace SmaetRec.Services;

/// <summary>Very small runtime localization service (Arabic default, English optional).</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private string _lang = "ar";

    private static readonly Dictionary<string, string> Ar = new(StringComparer.Ordinal)
    {
        ["AppName"] = "SMAET REC",
        ["Tagline"] = "استوديو التسجيل",
        ["Status.Preparing"] = "جارٍ التجهيز…",
        ["Status.Ready"] = "جاهز",
        ["Status.PhoneOffline"] = "الآيفون غير متصل",
        ["Status.PhoneWaiting"] = "بانتظار الآيفون",
        ["Status.PhoneConnected"] = "{0} متصل",
        ["Status.PhoneStreaming"] = "{0} — البث يعمل",
        ["Status.UsbDetected"] = "آيفون متصل بالكابل",
        ["Status.AirPlayOff"] = "AirPlay متوقف",
        ["Status.AirPlayError"] = "تعذّر تشغيل AirPlay",
        ["Status.Recording"] = "جارٍ التسجيل",
        ["Status.Saved"] = "تم الحفظ",
        ["Settings"] = "الإعدادات",
        ["Action.Connect"] = "وصّل الآيفون",
        ["Action.Screen"] = "شاشة الكمبيوتر",
        ["Action.HideScreen"] = "إخفاء الشاشة",
        ["Action.Draw"] = "ارسم",
        ["Action.Effects"] = "تأثيرات",
        ["Action.Record"] = "سجّل",
        ["Action.Stop"] = "إيقاف",
        ["Hint.NoScreen"] = "اضغط «شاشة الكمبيوتر» لعرضها هنا",
        ["Hint.NoPhone"] = "وصّل الآيفون لعرض شاشته",
        ["Phone.Offline"] = "غير متصل",
        ["Connect.Title"] = "توصيل الآيفون",
        ["Connect.Subtitle"] = "بدون تطبيقات على الآيفون. فقط نفس شبكة Wi-Fi.",
        ["Connect.Step1"] = "تأكد أن الآيفون والكمبيوتر على نفس شبكة Wi-Fi.",
        ["Connect.Step2"] = "افتح مركز التحكم في الآيفون.",
        ["Connect.Step3"] = "اضغط «تكرار الشاشة» ثم اختر SMAET REC.",
        ["Connect.Server"] = "خادم AirPlay",
        ["Connect.Running"] = "يعمل — بانتظار الجهاز",
        ["Connect.Stopped"] = "متوقف",
        ["Connect.Restart"] = "إعادة تشغيل الاتصال",
        ["Connect.UsbTitle"] = "اتصال USB",
        ["Connect.UsbBody"] = "يتعرّف التطبيق على الآيفون الموصول بالكابل، لكن Apple لا تسمح لتطبيقات الطرف الثالث ببث الشاشة عبر USB. استخدم AirPlay للبث.",
        ["Connect.Close"] = "تم",
        ["Record.NoPhone"] = "يمكنك التسجيل الآن — شاشة الآيفون غير متصلة",
        ["Record.Started"] = "بدأ التسجيل",
        ["Record.Stopped"] = "تم حفظ الفيديو",
        ["Record.OpenFile"] = "فتح الفيديو",
        ["Record.OpenFolder"] = "عرض المجلد",
        ["Record.Saving"] = "جارٍ إنهاء الملف…",
        ["Record.Failed"] = "تعذّر بدء التسجيل",
        ["Record.NoAudio"] = "تم التسجيل بدون صوت",
        ["Error.Generic"] = "حدث خطأ",
        ["ResetLayout"] = "إعادة الترتيب",
        ["Layout.Side"] = "جانب الشاشة",
        ["Layout.Over"] = "فوق الشاشة",
        ["Layout.Center"] = "توسيط",
        ["Orient.Portrait"] = "طولي",
        ["Orient.Landscape"] = "عرضي",
        ["Zoom.Reset"] = "إعادة التكبير",
    };

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["AppName"] = "SMAET REC",
        ["Tagline"] = "Recording Studio",
        ["Status.Preparing"] = "Getting ready…",
        ["Status.Ready"] = "Ready",
        ["Status.PhoneOffline"] = "iPhone not connected",
        ["Status.PhoneWaiting"] = "Waiting for iPhone",
        ["Status.PhoneConnected"] = "{0} connected",
        ["Status.PhoneStreaming"] = "{0} — streaming",
        ["Status.UsbDetected"] = "iPhone connected via USB",
        ["Status.AirPlayOff"] = "AirPlay is off",
        ["Status.AirPlayError"] = "AirPlay could not start",
        ["Status.Recording"] = "Recording",
        ["Status.Saved"] = "Saved",
        ["Settings"] = "Settings",
        ["Action.Connect"] = "Connect iPhone",
        ["Action.Screen"] = "Computer screen",
        ["Action.HideScreen"] = "Hide screen",
        ["Action.Draw"] = "Draw",
        ["Action.Effects"] = "Effects",
        ["Action.Record"] = "Record",
        ["Action.Stop"] = "Stop",
        ["Hint.NoScreen"] = "Tap “Computer screen” to show it here",
        ["Hint.NoPhone"] = "Connect your iPhone to see its screen",
        ["Phone.Offline"] = "Offline",
        ["Connect.Title"] = "Connect iPhone",
        ["Connect.Subtitle"] = "No app on the phone. Just the same Wi-Fi network.",
        ["Connect.Step1"] = "Make sure the iPhone and PC are on the same Wi-Fi.",
        ["Connect.Step2"] = "Open Control Center on the iPhone.",
        ["Connect.Step3"] = "Tap Screen Mirroring, then choose SMAET REC.",
        ["Connect.Server"] = "AirPlay server",
        ["Connect.Running"] = "Running — waiting for a device",
        ["Connect.Stopped"] = "Stopped",
        ["Connect.Restart"] = "Restart connection",
        ["Connect.UsbTitle"] = "USB connection",
        ["Connect.UsbBody"] = "The app detects a cabled iPhone, but Apple does not allow third-party apps to mirror over USB. Use AirPlay.",
        ["Connect.Close"] = "Done",
        ["Record.NoPhone"] = "You can record now — the iPhone is not connected",
        ["Record.Started"] = "Recording started",
        ["Record.Stopped"] = "Video saved",
        ["Record.OpenFile"] = "Open video",
        ["Record.OpenFolder"] = "Show folder",
        ["Record.Saving"] = "Finishing the file…",
        ["Record.Failed"] = "Could not start recording",
        ["Record.NoAudio"] = "Recorded without audio",
        ["Error.Generic"] = "Something went wrong",
        ["ResetLayout"] = "Reset layout",
        ["Layout.Side"] = "Beside screen",
        ["Layout.Over"] = "Over screen",
        ["Layout.Center"] = "Center",
        ["Orient.Portrait"] = "Portrait",
        ["Orient.Landscape"] = "Landscape",
        ["Zoom.Reset"] = "Reset zoom",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string this[string key]
    {
        get
        {
            var dict = _lang == "ar" ? Ar : En;
            return dict.TryGetValue(key, out var value) ? value : key;
        }
    }

    public string Language => _lang;
    public bool IsRtl => _lang == "ar";

    public void SetLanguage(string lang)
    {
        lang = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
        if (lang == _lang) return;
        _lang = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
