using System.Net.NetworkInformation;
using System.Net.Sockets;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

/// <summary>What Windows currently knows about an iPhone on a USB cable.</summary>
public sealed record UsbPhoneStatus(bool PhonePluggedIn, bool AppleDriverInstalled, string? NetworkAddress, string? DeviceId = null)
{
    // Apple Mobile Device Support and the capture filter are separate components.
    // Presence of the phone is enough to attempt capture; the native host performs
    // the real backend/driver preflight and reports its exact failure stage.
    public bool Ready => PhonePluggedIn;
}

/// <summary>
/// Checks the three things the cable method needs: an Apple phone on USB, Apple's device driver,
/// and optionally reports the USB network adapter for legacy AirPlay diagnostics. Direct USB does not use it.
/// </summary>
public static class UsbPhoneDetector
{
    public static UsbPhoneStatus Check()
    {
        string? deviceId = AppleMobileDeviceId();
        bool plugged = deviceId != null;
        string? address = null;
        bool adapterSeen = false;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!nic.Description.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase)) continue;
                adapterSeen = true;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                address = nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            }
        }
        catch (NetworkInformationException) { }
        bool driver = adapterSeen || AppleDriverInstalled();
        return new UsbPhoneStatus(plugged, driver, address, deviceId);
    }

    /// <summary>True when a present USB device has Apple's vendor id and an iPhone/iPad product id (0x12xx).</summary>
    private static string? AppleMobileDeviceId()
    {
        const uint filterEnumerator = 0x1, filterPresent = 0x100;
        try
        {
            if (CM_Get_Device_ID_List_Size(out uint length, "USB", filterEnumerator | filterPresent) != 0 || length == 0) return null;
            var buffer = new char[length];
            if (CM_Get_Device_ID_List("USB", buffer, length, filterEnumerator | filterPresent) != 0) return null;
            foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!id.Contains("VID_05AC&PID_12", StringComparison.OrdinalIgnoreCase)) continue;
                int slash = id.LastIndexOf('\\');
                if (slash < 0 || slash == id.Length - 1) continue;
                string serial = id[(slash + 1)..].Trim();
                if (serial.Length >= 16 && serial.Length <= 48 && serial.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return serial;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return null;
    }
    private static bool AppleDriverInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Apple Mobile Device Service");
            if (key != null) return true;
            using var netKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Netaapl");
            return netKey != null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
}
