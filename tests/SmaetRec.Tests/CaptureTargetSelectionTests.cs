using SmaetRec.Media;

namespace SmaetRec.Tests;

public sealed class CaptureTargetSelectionTests
{
    [Theory]
    [InlineData("3uTools", "3uTools", true)]
    [InlineData("Realtime Screen", "OtherViewer", true)]
    [InlineData("Real-time Screen", "OtherViewer", true)]
    [InlineData("iPhone", "3uTools", true)]
    [InlineData("Chrome", "chrome", false)]
    [InlineData("SMAET REC", "SmaetRec", false)]
    public void ThreeUToolsWindowDetectionUsesTitleOrOwningProcess(
        string title, string processName, bool expected)
    {
        var target = new CaptureTarget(
            CaptureKind.Window, new IntPtr(123), title, 0, 0, 800, 600, false)
        {
            ProcessName = processName
        };

        Assert.Equal(expected, target.IsLikely3uToolsWindow);
    }
}
