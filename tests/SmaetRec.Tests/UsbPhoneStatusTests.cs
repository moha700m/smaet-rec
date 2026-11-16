using SmaetRec.Media;

namespace SmaetRec.Tests;

public sealed class UsbPhoneStatusTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    public void AppleSupportInstalled_RequiresBothAppleComponents(
        bool phonePluggedIn, bool appleUsbDriverInstalled,
        bool serviceRunning, bool expected)
    {
        var status = new UsbPhoneStatus(phonePluggedIn, appleUsbDriverInstalled, serviceRunning);
        Assert.Equal(expected, status.AppleSupportInstalled);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void Ready_OnlyRequiresPhysicalPhonePresence(
        bool phonePluggedIn, bool appleUsbDriverInstalled,
        bool serviceRunning, bool expected)
    {
        var status = new UsbPhoneStatus(phonePluggedIn, appleUsbDriverInstalled, serviceRunning);
        Assert.Equal(expected, status.Ready);
    }
}
