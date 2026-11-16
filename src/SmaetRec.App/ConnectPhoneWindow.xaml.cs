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
    private bool _usbReady;
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
            _usbReady = usb.PhonePluggedIn;
            ConnectUsb.IsEnabled = usb.PhonePluggedIn && _usbDeviceId != null && !_usbConnecting && !_studio.UsbSessionActive;
            ConnectUsb.Content = _studio.UsbConnected ? "USB streaming"
                : _usbConnecting ? _studio.UsbStatus
                : _studio.UsbSessionActive ? "Waiting for video..." : "Connect USB";
            Set(UsbPhone, UsbPhoneDot, usb.PhonePluggedIn, Loc.T(usb.PhonePluggedIn ? "UsbPhoneFound" : "UsbPhoneMissing"));
            Set(UsbDriver, UsbDriverDot, usb.AppleSupportInstalled, Loc.T(usb.AppleSupportInstalled ? "UsbDriverFound" : "UsbDriverMissing"));
            Set(UsbNetwork, UsbNetworkDot, usb.NetworkAddress != null, usb.NetworkAddress != null ? Loc.T("UsbNetworkUp", usb.NetworkAddress) : Loc.T("UsbNetworkDown"));
            GetAppleDevices.Visibility = usb.AppleSupportInstalled ? Visibility.Collapsed : Visibility.Visible;
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
        try { await _studio.ConnectUsbAsync(_usbDeviceId); ConnectUsb.Content = "Waiting for video..."; }
        catch (Exception ex) { ConnectUsb.Content = "Connect USB"; MessageBox.Show(this, ex.Message, "Direct USB capture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _usbConnecting = false; ConnectUsb.IsEnabled = _usbReady && _usbDeviceId != null && !_studio.UsbSessionActive; }
    }

    private void ThreeUToolsScreen_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "First open 3uTools, choose Toolbox > Realtime Screen, and leave the iPhone preview window visible. Then select that window in the next list.",
            "3uTools Realtime Screen", MessageBoxButton.OK, MessageBoxImage.Information);

        var picker = new ScreenPickerWindow(
            _studio.Engine.ScreenActive,
            windowsOnly: true,
            dialogTitle: "Select the 3uTools iPhone screen",
            instructions: "Choose the live iPhone preview window created by 3uTools. Keep that window restored and visible while capturing.");
        if (picker.ShowDialog() != true) return;
        if (picker.Selected is not { Kind: CaptureKind.Window } target)
        {
            MessageBox.Show(this, "Select the 3uTools Realtime Screen window, not a desktop monitor.",
                "3uTools Realtime Screen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!target.StillExists)
        {
            MessageBox.Show(this, "That window has closed. Reopen 3uTools Realtime Screen and try again.",
                "3uTools Realtime Screen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        lock (_studio.Scene.Sync) _studio.Scene.Screen.Visible = true;
        _studio.Scene.NotifyChanged();
        _studio.Engine.ShowScreen(target);
        MessageBox.Show(this,
            "The selected 3uTools window is now the live desktop source in SMAET REC. You can record it with the normal Record button.",
            "3uTools Realtime Screen", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnostics is { IsLoaded: true }) { _diagnostics.Activate(); return; }
        _diagnostics = new ConnectionDiagnosticsWindow(_studio) { Owner = this };
        _diagnostics.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void InstallUsbDriver_Click(object sender, RoutedEventArgs e)
    {
        string driverTool = Path.Combine(AppContext.BaseDirectory, "usb", "iPhoneMirror.Driver.exe");
        if (!File.Exists(driverTool))
        {
            MessageBox.Show(this, "The USB driver manager is not included in this build. Rebuild using the repository Windows workflow.", "USB driver setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(driverTool) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "USB driver setup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GetAppleDevices_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NP83LWLPZ9K") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Process.Start(new ProcessStartInfo("https://apps.microsoft.com/detail/9np83lwlpz9k") { UseShellExecute = true });
        }
    }
}
