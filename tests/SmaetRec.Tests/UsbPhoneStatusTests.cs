using SmaetRec.Media;

namespace SmaetRec.Tests;

public sealed class UsbPhoneStatusTests
{
    [Theory]
    [InlineData(true, true, true, false, false, true)]
    [InlineData(true, false, false, true, true, true)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(false, true, true, true, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, false, false, false, false)]
    public void AppleSupport_RequiresAppleUsbDriverAndUsableMuxOrService(
        bool appleUsbDriverInstalled,
        bool serviceInstalled,
        bool serviceRunning,
        bool muxAvailable,
        bool muxPortOccupied,
        bool expected)
    {
        var status = new UsbPhoneStatus(
            PhonePluggedIn: true,
            AppleUsbDriverInstalled: appleUsbDriverInstalled,
            AppleMobileDeviceServiceRunning: serviceRunning,
            AppleMobileDeviceServiceInstalled: serviceInstalled,
            AppleMuxAvailable: muxAvailable,
            AppleMuxPortOccupied: muxPortOccupied);
        Assert.Equal(expected, status.AppleSupportInstalled);
    }

    [Fact]
    public void IncompatibleProcessOnAppleMuxPortIsAConflictEvenWhenServiceClaimsToRun()
    {
        var status = new UsbPhoneStatus(
            true, true, true,
            AppleMobileDeviceServiceInstalled: true,
            AppleMuxAvailable: false,
            AppleMuxPortOccupied: true,
            AppleMuxListener: "unrelated.exe (PID 1234)");

        Assert.True(status.HasAppleMuxConflict);
        Assert.False(status.AppleSupportInstalled);
        Assert.Equal("unrelated.exe (PID 1234)", status.AppleMuxListener);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void Ready_OnlyMeansThePhysicalPhoneIsPresent(
        bool phonePluggedIn, bool appleUsbDriverInstalled,
        bool serviceRunning, bool expected)
    {
        var status = new UsbPhoneStatus(phonePluggedIn, appleUsbDriverInstalled, serviceRunning);
        Assert.Equal(expected, status.Ready);
    }

    [Theory]
    [InlineData(@"USB\VID_05AC&PID_12A8\00008030-001C2D3E4F5A6789", true)]
    [InlineData(@"USB\VID_05AC&PID_12AC\00008030-001C2D3E4F5A6789", true)]
    [InlineData(@"USB\VID_05AC&PID_1290\00008030-001C2D3E4F5A6789", true)]
    [InlineData(@"USB\VID_05AD&PID_12A8\00008030-001C2D3E4F5A6789", false)]
    [InlineData(@"USB\VID_05AC&PID_110A\00008030-001C2D3E4F5A6789", false)]
    [InlineData(@"USB\VID_05AC&PID_12FF\00008030-001C2D3E4F5A6789", false)]
    [InlineData(@"USB\VID_05AC&PID_12A8&MI_00\00008030-001C2D3E4F5A6789", false)]
    [InlineData(@"USB\VID_05AC&PID_12A8\short", false)]
    [InlineData(@"USB\VID_05AC&PID_12A8\00008030-001C2D3E4F5A6789\child", false)]
    [InlineData(@"USB\VID_05AC&PID_ZZ99\00008030-001C2D3E4F5A6789", false)]
    public void AppleUsbParentParserRejectsUnrelatedOrMalformedPnpIds(string instanceId, bool expected)
    {
        Assert.Equal(expected, UsbPhoneDetector.IsAppleMobileCaptureParentInstanceId(instanceId));
        Assert.Equal(expected ? instanceId[(instanceId.LastIndexOf('\\') + 1)..] : null,
            UsbPhoneDetector.ParseAppleMobileCaptureParentSerial(instanceId));
    }
}
