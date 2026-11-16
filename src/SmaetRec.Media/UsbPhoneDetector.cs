using System.Runtime.InteropServices;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

/// <summary>Read-only Apple USB readiness and phone presence information.</summary>
public sealed record UsbPhoneStatus(
    bool PhonePluggedIn,
    bool AppleUsbDriverInstalled,
    bool AppleMobileDeviceServiceRunning,
    string? ParentDriverService = null,
    string? DeviceId = null)
{
    // iTunes/Lockdown needs both the actual Apple USB driver package and the
    // Apple Mobile Device service. A photo-transfer/PTP device alone is not enough.
    public bool AppleSupportInstalled => AppleUsbDriverInstalled && AppleMobileDeviceServiceRunning;
    public bool ParentCompositeDriverReady =>
        !PhonePluggedIn || string.Equals(ParentDriverService, "usbccgp", StringComparison.OrdinalIgnoreCase);
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
        bool driverInstalled = AppleUsbDriverInstalled();
        bool serviceRunning = AppleMobileDeviceServiceRunning();
        string? parentService = device is null ? null : ReadParentDriverService(device.InstanceId);
        return new UsbPhoneStatus(device != null, driverInstalled, serviceRunning, parentService, device?.Serial);
    }

    /// <summary>Find a present Apple mobile USB parent and return its PnP instance serial.</summary>
    private sealed record ApplePnpDevice(string Serial, string InstanceId);

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
                if (!id.Contains("VID_05AC&PID_12", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) continue;
                int slash = id.LastIndexOf('\\');
                if (slash < 0 || slash == id.Length - 1) continue;
                string serial = id[(slash + 1)..].Trim();
                if (serial.Length >= 16 && serial.Length <= 48 &&
                    serial.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                    return new ApplePnpDevice(serial, id);
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

    private static bool AppleUsbDriverInstalled()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string driverStore = Path.Combine(windows, "System32", "DriverStore", "FileRepository");
        string[] infNames = ["appleusb.inf", "usbaapl64.inf", "usbaapl.inf"];
        try
        {
            if (Directory.Exists(driverStore))
            {
                foreach (string inf in infNames)
                {
                    foreach (string directory in Directory.EnumerateDirectories(
                                 driverStore, inf + "_*", SearchOption.TopDirectoryOnly))
                    {
                        if (File.Exists(Path.Combine(directory, inf))) return true;
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
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
                if (File.Exists(Path.Combine(legacyDrivers, inf))) return true;
            }
        }
        return false;
    }

    private static bool AppleMobileDeviceServiceRunning()
    {
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) return false;
        try
        {
            foreach (string serviceName in new[] { "Apple Mobile Device Service", "AppleMobileDeviceService" })
            {
                IntPtr service = OpenService(manager, serviceName, ServiceQueryStatus);
                if (service == IntPtr.Zero) continue;
                try
                {
                    if (QueryServiceStatus(service, out ServiceStatus status) &&
                        status.CurrentState == ServiceRunning)
                        return true;
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
        return false;
    }
}
