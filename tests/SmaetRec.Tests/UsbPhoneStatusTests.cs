using SmaetRec.Media;

namespace SmaetRec.Tests;

public sealed class UsbPhoneStatusTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Ready_RequiresPhonePresence_NotAppleSupportRegistry(
        bool phonePluggedIn, bool appleSupportInstalled, bool expected)
    {
        var status = new UsbPhoneStatus(phonePluggedIn, appleSupportInstalled, null);
        Assert.Equal(expected, status.Ready);
    }
}
