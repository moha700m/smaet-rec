using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SmaetRec.AirPlay;
using SmaetRec.Core;
using SmaetRec.Media;

namespace SmaetRec.App;

/// <summary>Explains why an iPhone cannot see or reach the receiver, and can repair the common Windows causes.</summary>
public partial class ConnectionDiagnosticsWindow : Window
{
    private readonly Studio _studio;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private NetworkProfileInfo? _profile;
    private bool _busy, _refreshing;
    private DateTime _lastFullCheck = DateTime.MinValue;
    private bool? _firewall;
    private UsbPhoneStatus? _usb;

    public ConnectionDiagnosticsWindow(Studio studio)
    {
        _studio = studio;
        DialogSetup.Apply(this);
        InitializeComponent();
        Loaded += async (_, _) => { await RefreshAsync(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private async Task RefreshAsync()
    {
        if (_busy || _refreshing) return;
        _refreshing = true;
        try
        {
            var state = _studio.Receiver.State;
            switch (state)
            {
                case ReceiverState.Connected:
                {
                    var who = string.Join(" · ", new[] { _studio.Receiver.ClientName, _studio.Receiver.ClientModel, _studio.Receiver.ClientVersion }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    AirPlayText.Text = who.Length == 0
                        ? Loc.T("DiagAirPlayConnected", _studio.Receiver.Name)
                        : Loc.T("DiagAirPlayConnected", _studio.Receiver.Name) + " — " + who;
                    AirPlayDot.Fill = Res("GreenBrush");
                    break;
                }
                case ReceiverState.Waiting:
                    AirPlayText.Text = Loc.T("DiagAirPlayRunning", _studio.Receiver.ControlPort, _studio.Receiver.AnnouncedAdapters);
                    AirPlayDot.Fill = Res("GreenBrush"); break;
                case ReceiverState.Failed:
                    AirPlayText.Text = Loc.T("DiagAirPlayFailed", _studio.Receiver.LastError ?? Loc.T("Unknown"));
                    AirPlayDot.Fill = Res("RedBrush"); break;
                default:
                    AirPlayText.Text = Loc.T("DiagAirPlayStopped");
                    AirPlayDot.Fill = Res("SubtleBrush"); break;
            }

            var receiver = _studio.Receiver;
            if (receiver.MirrorCodecUnsupported)
            {
                VideoText.Text = Loc.T("DiagVideoCodec");
                VideoDot.Fill = Res("RedBrush");
            }
            else if (receiver.MirrorPackets == 0)
            {
                VideoText.Text = Loc.T("DiagVideoWaiting");
                VideoDot.Fill = Res("AmberBrush");
            }
            else
            {
                VideoText.Text = Loc.T("DiagVideoLine", receiver.MirrorPackets, receiver.MirrorPictures,
                    _studio.PhoneFramesDecoded, receiver.MirrorInvalid)
                    + (receiver.MirrorKeyVariant is { } key ? $" · {key}" : "");
                VideoDot.Fill = Res(_studio.PhoneFramesDecoded > 0 ? "GreenBrush" : "AmberBrush");
                // Say why nothing is shown, in words, so a screenshot of this window is enough to diagnose it.
                if (_studio.PhoneDecoderError is { } decoderError)
                {
                    VideoText.Text += Environment.NewLine + Loc.T("DiagVideoDecoderError", decoderError);
                    VideoDot.Fill = Res("RedBrush");
                }
                else if (_studio.PhoneFramesDecoded == 0 && receiver.MirrorPictures > 30)
                {
                    VideoText.Text += Environment.NewLine + (receiver.MirrorHasParameterSets
                        ? Loc.T("DiagVideoWaitingStart", _studio.PhonePacketsWaitingForStart)
                        : Loc.T("DiagVideoNoSps"));
                    VideoDot.Fill = Res("RedBrush");
                }
                if (_studio.PhonePacketsDropped > 0)
                {
                    VideoText.Text += Environment.NewLine + Loc.T("DiagTooSlow", _studio.PhonePacketsDropped);
                    VideoDot.Fill = Res("RedBrush");
                }
                VideoText.ToolTip = _studio.PhoneDecoderStats;
            }

            var log = _studio.ReceiverLogSnapshot();
            string text = log.Count == 0 ? Loc.T("DiagNoLog") : string.Join(Environment.NewLine, log);
            if (LogText.Text != text)
            {
                LogText.Text = text;
                LogText.ScrollToEnd();
            }

            // PowerShell-based checks are slower; run them periodically, not on every UI tick.
            if (DateTime.UtcNow - _lastFullCheck < TimeSpan.FromSeconds(8)) return;
            _lastFullCheck = DateTime.UtcNow;

            _profile = await NetworkDiagnostics.GetNetworkProfileAsync();
            if (_profile == null)
            {
                NetworkText.Text = Loc.T("DiagNetworkUnknown");
                NetworkDot.Fill = Res("RedBrush");
            }
            else
            {
                string kind = _profile.Category switch
                {
                    "Private" => Loc.T("DiagNetworkPrivate"),
                    "Domain" => Loc.T("DiagNetworkDomain"),
                    "Public" => Loc.T("DiagNetworkPublic"),
                    _ => Loc.T("DiagNetworkUnknownKind"),
                };
                string addresses = _profile.Addresses.Count > 0
                    ? string.Join(" · ", _profile.Addresses.Take(3))
                    : Loc.T("DiagNetworkNoAddress");
                NetworkText.Text = Loc.T("DiagNetworkLine", _profile.Name ?? "?", kind, addresses);
                NetworkDot.Fill = Res(_profile.Category == "Public" ? "AmberBrush" : "GreenBrush");
            }

            _firewall = await NetworkDiagnostics.FirewallRulePresentAsync();
            FirewallText.Text = _firewall switch
            {
                true => Loc.T("DiagFirewallAllowed"),
                false => Loc.T("DiagFirewallBlocked"),
                null => Loc.T("DiagFirewallUnknown"),
            };
            FirewallDot.Fill = Res(_firewall switch { true => "GreenBrush", false => "RedBrush", _ => "AmberBrush" });

            _usb = await Task.Run(UsbPhoneDetector.Check);
            if (_usb.PhonePluggedIn && !_usb.ParentCompositeDriverReady)
            {
                DriverText.Text = Loc.T("DiagAppleParentDriverWrong", _usb.ParentDriverService ?? "unknown");
                DriverDot.Fill = Res("RedBrush");
            }
            else if (!_usb.AppleUsbDriverInstalled)
            {
                DriverText.Text = Loc.T("DiagAppleUsbDriverMissing");
                DriverDot.Fill = Res("AmberBrush");
            }
            else if (!_usb.AppleSupportInstalled)
            {
                DriverText.Text = Loc.T("DiagAppleServiceStopped");
                DriverDot.Fill = Res("RedBrush");
            }
            else
            {
                DriverText.Text = Loc.T("DiagAppleSupportInstalled");
                DriverDot.Fill = Res("GreenBrush");
            }

            if (_usb.AppleMuxAvailable)
            {
                UsbMuxText.Text = Loc.T("DiagAppleMuxReady");
                UsbMuxDot.Fill = Res("GreenBrush");
            }
            else if (_usb.HasAppleMuxConflict)
            {
                UsbMuxText.Text = Loc.T("DiagAppleMuxConflict", _usb.AppleMuxListener ?? "unknown process");
                UsbMuxDot.Fill = Res("RedBrush");
            }
            else if (_usb.AppleSupportInstalled)
            {
                UsbMuxText.Text = Loc.T("DiagAppleMuxLegacy");
                UsbMuxDot.Fill = Res("AmberBrush");
            }
            else
            {
                UsbMuxText.Text = Loc.T("DiagAppleMuxUnavailable");
                UsbMuxDot.Fill = Res("RedBrush");
            }

            UsbCaptureFilterText.Text = !_usb.PhonePluggedIn
                ? Loc.T("DiagCaptureFilterNoPhone")
                : _usb.CaptureFilterAttached
                    ? Loc.T("DiagCaptureFilterFound")
                    : Loc.T("DiagCaptureFilterMissing");
            UsbCaptureFilterDot.Fill = Res(!_usb.PhonePluggedIn ? "SubtleBrush" :
                _usb.CaptureFilterAttached ? "GreenBrush" : "AmberBrush");
        }
        finally { _refreshing = false; }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RepairButton.IsEnabled = false;
        MessageText.Text = Loc.T("DiagRepairing");
        try
        {
            var result = await NetworkDiagnostics.RepairAsync(_profile);
            MessageText.Text = result.ok ? Loc.T("DiagRepairDone") : Loc.T("DiagRepairFailed", result.message);
            MessageText.Foreground = Res(result.ok ? "GreenBrush" : "RedBrush");
            if (result.ok)
            {
                _lastFullCheck = DateTime.MinValue;
                _studio.StartReceiver();
                await Task.Delay(1200);
            }
        }
        finally
        {
            _busy = false;
            RepairButton.IsEnabled = true;
            await RefreshAsync();
        }
    }

    private void NetworkSettings_Click(object sender, RoutedEventArgs e) => NetworkDiagnostics.OpenNetworkSettings();

    private void SaveSample_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = _studio.SaveDiagnosticSample();
            if (folder == null)
            {
                MessageText.Text = Loc.T("DiagSampleNone");
                MessageText.Foreground = Res("AmberBrush");
                return;
            }
            MessageText.Text = Loc.T("DiagSampleSaved", folder);
            MessageText.Foreground = Res("GreenBrush");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageText.Text = ex.Message;
            MessageText.Foreground = Res("RedBrush");
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = string.Join(Environment.NewLine, _studio.ReceiverLogSnapshot());
            if (_studio.PhoneDecoderStats is { } stats) text += Environment.NewLine + "Decoder now: " + stats;
            Clipboard.SetText(text.Length == 0 ? Loc.T("DiagNoLog") : text);
            MessageText.Text = Loc.T("DiagCopied");
            MessageText.Foreground = Res("GreenBrush");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            MessageText.Text = Loc.T("DiagCopyFailed");
            MessageText.Foreground = Res("RedBrush");
        }
    }
    private async void CopyUsbReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _usb ??= await Task.Run(UsbPhoneDetector.Check);
            var status = _usb;
            string report = string.Join(Environment.NewLine, new[]
            {
                "SMAET REC — Apple USB diagnostic report",
                $"Captured: {DateTimeOffset.Now:O}",
                $"Phone detected: {status.PhonePluggedIn}",
                $"Apple USB driver: {status.AppleUsbDriverInf ?? (status.AppleUsbDriverInstalled ? "installed (INF not resolved)" : "missing")}",
                $"Apple Mobile Device Service installed: {status.AppleMobileDeviceServiceInstalled}",
                $"Apple Mobile Device Service running: {status.AppleMobileDeviceServiceRunning}",
                $"Apple usbmux ListDevices: {status.AppleMuxAvailable}",
                $"Apple usbmux port 27015 occupied: {status.AppleMuxPortOccupied}",
                $"Port 27015 listener: {status.AppleMuxListener ?? "none identified"}",
                $"Apple usbmux conflict: {status.HasAppleMuxConflict}",
                $"USB parent service: {status.ParentDriverService ?? "unknown"}",
                $"USB composite parent ready: {status.ParentCompositeDriverReady}",
                $"Per-device libusb0 capture filter: {status.CaptureFilterAttached}",
                $"Apple support ready: {status.AppleSupportInstalled}",
                "",
                "Note: The report does not include the phone serial or personal data."
            });

            Clipboard.SetText(report);
            MessageText.Text = Loc.T("DiagCopied");
            MessageText.Foreground = Res("GreenBrush");
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or
            InvalidOperationException or System.Security.SecurityException)
        {
            MessageText.Text = Loc.T("DiagCopyFailed");
            MessageText.Foreground = Res("RedBrush");
        }
    }

    private void InstallDrivers_Click(object sender, RoutedEventArgs e) => NetworkDiagnostics.OpenAppleDevicesStore();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
