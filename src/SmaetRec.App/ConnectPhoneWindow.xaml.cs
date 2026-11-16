using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SmaetRec.AirPlay;
using SmaetRec.Core;
using SmaetRec.Media;

namespace SmaetRec.App;

/// <summary>Shows how to connect the iPhone by Wi-Fi or by cable, with live status for both.</summary>
public partial class ConnectPhoneWindow : Window
{
    private readonly Studio _studio;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _checking;
    private bool _usbConnecting;
    private string? _usbDeviceId;
    private ConnectionDiagnosticsWindow? _diagnostics;

    public ConnectPhoneWindow(Studio studio)
    {
        _studio = studio;
        DialogSetup.Apply(this);
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private async void Refresh()
    {
        string name = _studio.Receiver.State == ReceiverState.Stopped ? _studio.Settings.ReceiverName : _studio.Receiver.Name;
        WifiStep4.Text = Loc.T("WifiStep4", name);
        UsbStep3.Text = Loc.T("UsbStep3", name);
        switch (_studio.Receiver.State)
        {
            case ReceiverState.Connected:
                ReceiverText.Text = Loc.T("PhoneConnected"); ReceiverDot.Fill = Res("GreenBrush"); break;
            case ReceiverState.Waiting:
                ReceiverText.Text = _studio.Receiver.AnnouncedAdapters == 0
                    ? Loc.T("ReceiverNoNetwork")
                    : Loc.T("ReceiverRunning", name);
                ReceiverDot.Fill = Res("AmberBrush"); break;
            case ReceiverState.Failed:
                ReceiverText.Text = Loc.T("ReceiverFailed", _studio.Receiver.LastError ?? ""); ReceiverDot.Fill = Res("RedBrush"); break;
            default:
                ReceiverText.Text = Loc.T("PhoneOff"); ReceiverDot.Fill = Res("SubtleBrush"); break;
        }

        if (_checking) return;
        _checking = true;
        try
        {
            var usb = await Task.Run(UsbPhoneDetector.Check);
            _usbDeviceId = usb.DeviceId;
            ConnectUsb.IsEnabled = _usbDeviceId != null && !_usbConnecting;
            ConnectUsb.Content = _studio.UsbConnected ? "USB connected" : "Connect USB";
            Set(UsbPhone, UsbPhoneDot, usb.PhonePluggedIn, Loc.T(usb.PhonePluggedIn ? "UsbPhoneFound" : "UsbPhoneMissing"));
            Set(UsbDriver, UsbDriverDot, usb.AppleDriverInstalled, Loc.T(usb.AppleDriverInstalled ? "UsbDriverFound" : "UsbDriverMissing"));
            Set(UsbNetwork, UsbNetworkDot, usb.NetworkAddress != null, usb.NetworkAddress != null ? Loc.T("UsbNetworkUp", usb.NetworkAddress) : Loc.T("UsbNetworkDown"));
            GetAppleDevices.Visibility = usb.AppleDriverInstalled ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _checking = false; }
    }

    private void Set(System.Windows.Controls.TextBlock text, System.Windows.Shapes.Ellipse dot, bool ok, string message)
    {
        text.Text = message;
        text.Foreground = Res(ok ? "TextBrush" : "SubtleBrush");
        dot.Fill = Res(ok ? "GreenBrush" : "SubtleBrush");
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e) => _studio.StartReceiver();

    private async void ConnectUsb_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_usbDeviceId)) { MessageBox.Show(this, "Connect and unlock your iPhone, then try again.", "Direct USB", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        _usbConnecting = true; ConnectUsb.IsEnabled = false; ConnectUsb.Content = "Connecting...";
        try { await _studio.ConnectUsbAsync(_usbDeviceId); ConnectUsb.Content = "USB connected"; }
        catch (Exception ex) { ConnectUsb.Content = "Connect USB"; MessageBox.Show(this, ex.Message, "Direct USB capture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _usbConnecting = false; ConnectUsb.IsEnabled = _usbDeviceId != null; }
    }

    private void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnostics is { IsLoaded: true }) { _diagnostics.Activate(); return; }
        _diagnostics = new ConnectionDiagnosticsWindow(_studio) { Owner = this };
        _diagnostics.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void GetAppleDevices_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NP83LWLPZ9K") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Process.Start(new ProcessStartInfo("https://apps.microsoft.com/detail/9np83lwlpz9k") { UseShellExecute = true });
        }
    }
}
