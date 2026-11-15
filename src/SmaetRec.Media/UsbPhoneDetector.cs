using System.Net.NetworkInformation;
using System.Net.Sockets;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

/// <summary>What Windows currently knows about an iPhone on a USB cable.</summary>
public sealed record UsbPhoneStatus(bool PhonePluggedIn, bool AppleDriverInstalled, string? NetworkAddress)
{
    public bool Ready => PhonePluggedIn && AppleDriverInstalled && NetworkAddress != null;
}

/// <summary>
/// Checks the three things the cable method needs: an Apple phone on USB, Apple's device driver,
/// and the phone's USB network adapter being up (it appears when Personal Hotspot is on).
/// </summary>
public static class UsbPhoneDetector
{
    public static UsbPhoneStatus Check()
    {
        bool plugged = AppleMobileDevicePresent();
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
        return new UsbPhoneStatus(plugged, driver, address);
    }

    /// <summary>True when a present USB device has Apple's vendor id and an iPhone/iPad product id (0x12xx).</summary>
    private static bool AppleMobileDevicePresent()
    {
        const uint filterEnumerator = 0x1, filterPresent = 0x100;
        try
        {
            if (CM_Get_Device_ID_List_Size(out uint length, "USB", filterEnumerator | filterPresent) != 0 || length == 0) return false;
            var buffer = new char[length];
            if (CM_Get_Device_ID_List("USB", buffer, length, filterEnumerator | filterPresent) != 0) return false;
            foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                if (id.Contains("VID_05AC&PID_12", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return false;
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
