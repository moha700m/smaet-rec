using System.Runtime.InteropServices;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

/// <summary>Read-only Apple USB readiness and phone presence information.</summary>
public sealed record UsbPhoneStatus(
    bool PhonePluggedIn,
    bool AppleUsbDriverInstalled,
    bool AppleMobileDeviceServiceRunning,
    string? ParentDriverService = null,
    string? DeviceId = null,
    bool AppleMobileDeviceServiceInstalled = false,
    bool AppleMuxAvailable = false,
    bool AppleMuxPortOccupied = false,
    string? AppleMuxListener = null,
    string? AppleUsbDriverInf = null,
    bool CaptureFilterAttached = false)
{
    public bool HasAppleMuxConflict => AppleMuxPortOccupied && !AppleMuxAvailable;

    // The Store and desktop Apple packages can expose usbmux differently.
    // A valid ListDevices response is stronger evidence than a service name;
    // a busy but incompatible listener must never be treated as healthy.
    public bool AppleSupportInstalled => AppleUsbDriverInstalled && !HasAppleMuxConflict &&
        (AppleMuxAvailable || (AppleMobileDeviceServiceInstalled && AppleMobileDeviceServiceRunning));

    public bool ParentCompositeDriverReady =>
        !PhonePluggedIn || string.Equals(ParentDriverService, "usbccgp", StringComparison.OrdinalIgnoreCase);

    // This is USB presence only, not a claim that Apple pairing/capture works.
    public bool Ready => PhonePluggedIn;
}

/// <summary>
/// Separately checks physical Apple USB presence, Apple's USB INF package,
/// and the Apple Mobile Device service. This is read-only; repair remains an
/// explicit action in the upstream iPhoneMirror driver manager.
/// </summary>
public static class UsbPhoneDetector
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceRunning = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    public static UsbPhoneStatus Check()
    {
        var device = FindAppleMobileUsbDevice();
        string? driverInf = FindAppleUsbDriverInf();
        var service = QueryAppleMobileDeviceService();
        var mux = AppleUsbMuxProbe.Inspect();
        string? parentService = device is null ? null : ReadParentDriverService(device.InstanceId);
        bool captureFilter = device is not null && HasCaptureFilter(device.InstanceId);
        return new UsbPhoneStatus(
            device is not null,
            driverInf is not null,
            service.Running,
            parentService,
            device?.Serial,
            service.Installed,
            mux.Available,
            mux.PortOccupied,
            mux.Listener,
            driverInf,
            captureFilter);
    }

    private sealed record ApplePnpDevice(string Serial, string InstanceId);

    private static readonly HashSet<ushort> AppleMobileCaptureProductIds =
    [
        0x1290, 0x1291, 0x1292, 0x1293, 0x1294, 0x1297, 0x1299, 0x129A,
        0x129C, 0x129D, 0x129E, 0x129F, 0x12A0, 0x12A1, 0x12A2, 0x12A3,
        0x12A4, 0x12A5, 0x12A6, 0x12A8, 0x12A9, 0x12AA, 0x12AB, 0x12AC
    ];

    /// <summary>Accept only known Apple iPhone/iPad composite-parent PnP IDs.</summary>
    public static bool IsAppleMobileCaptureParentInstanceId(string? instanceId) =>
        TryParseAppleMobileCaptureParent(instanceId, out _);

    /// <summary>Returns the exact PnP serial only for a supported Apple mobile USB parent.</summary>
    public static string? ParseAppleMobileCaptureParentSerial(string? instanceId) =>
        TryParseAppleMobileCaptureParent(instanceId, out string? serial) ? serial : null;

    private static bool TryParseAppleMobileCaptureParent(string? instanceId, out string? serial)
    {
        serial = null;
        const string prefix = @"USB\VID_05AC&PID_";
        if (string.IsNullOrWhiteSpace(instanceId) ||
            !instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            return false;

        int productEnd = instanceId.IndexOf('\\', prefix.Length);
        if (productEnd != prefix.Length + 4 ||
            instanceId.IndexOf('\\', productEnd + 1) >= 0)
            return false;

        if (!ushort.TryParse(instanceId.AsSpan(prefix.Length, 4),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out ushort productId) ||
            !AppleMobileCaptureProductIds.Contains(productId))
            return false;

        string candidate = instanceId[(productEnd + 1)..].Trim();
        if (candidate.Length is < 16 or > 48 ||
            !candidate.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            return false;

        serial = candidate;
        return true;
    }

    private static ApplePnpDevice? FindAppleMobileUsbDevice()
    {
        const uint filterEnumerator = 0x1, filterPresent = 0x100;
        try
        {
            if (CM_Get_Device_ID_List_Size(out uint length, "USB", filterEnumerator | filterPresent) != 0 || length == 0)
                return null;
            var buffer = new char[length];
            if (CM_Get_Device_ID_List("USB", buffer, length, filterEnumerator | filterPresent) != 0)
                return null;
            foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                string? serial = ParseAppleMobileCaptureParentSerial(id);
                if (serial is not null) return new ApplePnpDevice(serial, id);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return null;
    }

    private static string? ReadParentDriverService(string instanceId)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\" + instanceId, writable: false);
            return key?.GetValue("Service") as string;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? FindAppleUsbDriverInf()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string driverStore = Path.Combine(windows, "System32", "DriverStore", "FileRepository");
        string[] infNames = ["appleusb.inf", "usbaapl64.inf", "usbaapl.inf"];
        if (Directory.Exists(driverStore))
        {
            foreach (string inf in infNames)
            {
                try
                {
                    foreach (string directory in Directory.EnumerateDirectories(
                                 driverStore, inf + "_*", SearchOption.TopDirectoryOnly))
                    {
                        if (File.Exists(Path.Combine(directory, inf))) return inf;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        string[] programRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];
        foreach (string root in programRoots.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string legacyDrivers = Path.Combine(root, "Common Files", "Apple", "Mobile Device Support", "Drivers");
            foreach (string inf in infNames)
            {
                if (File.Exists(Path.Combine(legacyDrivers, inf))) return inf;
            }
        }
        return null;
    }

    private sealed record AppleServiceStatus(bool Installed, bool Running, string? Name);

    private static AppleServiceStatus QueryAppleMobileDeviceService()
    {
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) return new(false, false, null);
        try
        {
            foreach (string serviceName in new[] { "Apple Mobile Device Service", "AppleMobileDeviceService" })
            {
                IntPtr service = OpenService(manager, serviceName, ServiceQueryStatus);
                if (service == IntPtr.Zero) continue;
                try
                {
                    bool running = QueryServiceStatus(service, out ServiceStatus status) &&
                        status.CurrentState == ServiceRunning;
                    return new(true, running, serviceName);
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
        return new(false, false, null);
    }

    private static bool HasCaptureFilter(string instanceId)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\" + instanceId, writable: false);
            object? raw = key?.GetValue("UpperFilters");
            IEnumerable<string> filters = raw switch
            {
                string[] values => values,
                string value => [value],
                _ => []
            };
            return filters.Any(value => string.Equals(value, "libusb0", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

}
