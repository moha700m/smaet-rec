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
    private bool _detailsOpenedOnce;
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
            UpdateUsbButton();
            Set(UsbPhone, UsbPhoneDot, usb.PhonePluggedIn, Loc.T(usb.PhonePluggedIn ? "UsbPhoneFound" : "UsbPhoneMissing"));
            Set(UsbDriver, UsbDriverDot, usb.AppleUsbDriverInstalled, Loc.T(usb.AppleUsbDriverInstalled ? "UsbDriverFound" : "UsbDriverMissing"));
            Set(UsbService, UsbServiceDot, usb.AppleMobileDeviceServiceRunning,
                Loc.T(usb.AppleMobileDeviceServiceRunning ? "UsbServiceRunning" : "UsbServiceStopped"));
            string muxMessage = usb.AppleMuxAvailable
                ? Loc.T("UsbMuxReady")
                : usb.HasAppleMuxConflict
                    ? Loc.T("UsbMuxConflict", usb.AppleMuxListener ?? "unknown process")
                    : Loc.T("UsbMuxUnavailable");
            Set(UsbMux, UsbMuxDot, usb.AppleMuxAvailable, muxMessage);
            string filterMessage = !usb.PhonePluggedIn
                ? Loc.T("UsbCaptureFilterNoPhone")
                : usb.CaptureFilterAttached
                    ? Loc.T("UsbCaptureFilterFound")
                    : Loc.T("UsbCaptureFilterMissing");
            Set(UsbCaptureFilter, UsbCaptureFilterDot, usb.CaptureFilterAttached, filterMessage);
            Set(UsbParent, UsbParentDot, usb.PhonePluggedIn && usb.ParentCompositeDriverReady,
                Loc.T(!usb.PhonePluggedIn ? "UsbParentNoPhone" : usb.ParentCompositeDriverReady ? "UsbParentReady" : "UsbParentWrong", usb.ParentDriverService ?? "unknown"));
            GetAppleDevices.Visibility = usb.AppleUsbDriverInstalled ? Visibility.Collapsed : Visibility.Visible;

            // One plain sentence on top; the technical rows stay folded away unless something needs fixing.
            string? problem =
                !usb.PhonePluggedIn ? Loc.T("UsbPhoneMissing")
                : !usb.AppleUsbDriverInstalled ? Loc.T("UsbDriverMissing")
                : !usb.AppleMuxAvailable ? muxMessage
                : !usb.ParentCompositeDriverReady ? Loc.T("UsbParentWrong", usb.ParentDriverService ?? "unknown")
                : !usb.CaptureFilterAttached ? filterMessage
                : null;
            bool live = _studio.UsbState is Studio.UsbPhase.Live or Studio.UsbPhase.Stalled;
            UsbSummary.Text = live ? Loc.T("UsbLive", _studio.UsbFps.ToString("0")) : problem ?? Loc.T("UsbReady");
            UsbSummaryDot.Fill = Res(live || problem == null ? "GreenBrush" : usb.PhonePluggedIn ? "AmberBrush" : "SubtleBrush");
            InstallUsbDriver.Visibility = File.Exists(Path.Combine(AppContext.BaseDirectory, "usb", "iPhoneMirror.Driver.exe")) ? Visibility.Visible : Visibility.Collapsed;
            if (problem != null && usb.PhonePluggedIn && !_detailsOpenedOnce) { _detailsOpenedOnce = true; UsbDetails.IsExpanded = true; }
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

    /// <summary>One button for the whole USB session: connect, stop while live, or try again after a failure.</summary>
    private void UpdateUsbButton()
    {
        var phase = _studio.UsbState;
        bool live = phase is Studio.UsbPhase.Live or Studio.UsbPhase.Stalled;
        bool busy = _usbConnecting || phase is Studio.UsbPhase.Starting or Studio.UsbPhase.Reconnecting;
        ConnectUsb.Content = live ? Loc.T("UsbStop", _studio.UsbFps.ToString("0"))
            : busy ? Loc.T("UsbConnecting")
            : phase == Studio.UsbPhase.Failed ? Loc.T("UsbRetry") : Loc.T("UsbConnect");
        ConnectUsb.IsEnabled = live || (!busy && _usbReady && _usbDeviceId != null);
        ConnectUsb.ToolTip = busy || phase == Studio.UsbPhase.Failed ? _studio.UsbStatus : null;
        UsbProgress.Text = busy || phase == Studio.UsbPhase.Failed ? _studio.UsbStatus : "";
        UsbProgress.Visibility = UsbProgress.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ConnectUsb_Click(object sender, RoutedEventArgs e)
    {
        if (_studio.UsbState is Studio.UsbPhase.Live or Studio.UsbPhase.Stalled)
        {
            ConnectUsb.IsEnabled = false;
            await _studio.StopUsbAsync();
            UpdateUsbButton();
            return;
        }
        if (string.IsNullOrWhiteSpace(_usbDeviceId))
        {
            MessageBox.Show(this, Loc.T("UsbPlugFirst"), Loc.T("UsbFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _usbConnecting = true;
        UpdateUsbButton();
        try { await _studio.ConnectUsbAsync(_usbDeviceId); }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or ArgumentException or OperationCanceledException or PlatformNotSupportedException)
        {
            MessageBox.Show(this, Loc.T("UsbFailedBody", ex.Message), Loc.T("UsbFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _usbConnecting = false; UpdateUsbButton(); }
    }

    private void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnostics is { IsLoaded: true }) { _diagnostics.Activate(); return; }
        _diagnostics = new ConnectionDiagnosticsWindow(_studio) { Owner = this };
        _diagnostics.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Open3uToolsScreen_Click(object sender, RoutedEventArgs e)
    {
        ThreeUToolsScreen.IsEnabled = false;
        try
        {
            bool activated = TryActivateRunning3uTools();
            if (!activated)
            {
                string? executable = Find3uToolsExecutable();
                if (executable is null)
                {
                    MessageBox.Show(this, Loc.T("ThreeUToolsNotInstalled"),
                        Loc.T("Open3uToolsScreen"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            }

            await Task.Delay(350);
            var picker = new ThreeUToolsWindowPicker { Owner = this };
            if (picker.ShowDialog() != true || picker.Selected is null) return;

            if (Owner is MainWindow main)
            {
                main.ShowCapturedWindow(picker.Selected, fullCanvas: true);
                Close();
                main.Activate();
            }
            else
            {
                MessageBox.Show(this, Loc.T("ThreeUToolsOwnerMissing"),
                    Loc.T("Open3uToolsScreen"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or
            InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Loc.T("ThreeUToolsOpenFailed", error.Message),
                Loc.T("Open3uToolsScreen"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ThreeUToolsScreen.IsEnabled = true;
        }
    }

    private static bool TryActivateRunning3uTools()
    {
        foreach (var process in Process.GetProcessesByName("3uTools"))
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(process.MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
                catch (Exception error) when (error is InvalidOperationException or
                    System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        return false;
    }

    private static string? Find3uToolsExecutable()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        ];
        foreach (string root in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string path = Path.Combine(root, "3uTools", "3uTools.exe");
            if (File.Exists(path)) return path;
        }

        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
        {
            try
            {
                using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var appPath = machine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\3uTools.exe");
                string? registered = ExistingExecutable(appPath?.GetValue(null) as string);
                if (registered is not null) return registered;

                string? folder = appPath?.GetValue("Path") as string;
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    string candidate = Path.Combine(folder, "3uTools.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
            {
            }
        }
        return null;
    }

    private static string? ExistingExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string path = value.Trim().Trim('"');
        int suffix = path.IndexOf(",0", StringComparison.Ordinal);
        if (suffix > 0) path = path[..suffix].Trim().Trim('"');
        return File.Exists(path) ? path : null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private void InstallUsbDriver_Click(object sender, RoutedEventArgs e)
    {
        string driverTool = Path.Combine(AppContext.BaseDirectory, "usb", "iPhoneMirror.Driver.exe");
        if (!File.Exists(driverTool))
        {
            MessageBox.Show(this, Loc.T("UsbDriverToolMissing"), Loc.T("UsbFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
