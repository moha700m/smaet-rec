using SmaetRec.Media;

namespace SmaetRec.Tests;

public sealed class CaptureTargetTests
{
    [Theory]
    [InlineData("3uTools", "iDevice", true)]
    [InlineData("3uTools", "Realtime Screen", true)]
    [InlineData("3uToolsService", "iDevice", false)]
    [InlineData("3uPlayer", "Viewer", false)]
    [InlineData("OtherApp", "Realtime Screen", true)]
    [InlineData("OtherApp", "Real-time Screen", true)]
    [InlineData("OtherApp", "SMAET REC", false)]
    [InlineData("notepad", "Untitled - Notepad", false)]
    public void ThreeUToolsWindowClassificationUsesProcessAndSpecificViewerTitles(
        string processName, string title, bool expected)
    {
        var target = new CaptureTarget(
            CaptureKind.Window, new IntPtr(1), title,
            0, 0, 640, 480, false)
        {
            ProcessName = processName
        };

        Assert.Equal(expected, target.IsLikely3uToolsWindow);
    }

    [Fact]
    public void MonitorIsNeverTreatedAsA3uToolsViewer()
    {
        var target = new CaptureTarget(
            CaptureKind.Monitor, new IntPtr(1), "3uTools Realtime Screen",
            0, 0, 1920, 1080, true)
        {
            ProcessName = "3uTools"
        };

        Assert.False(target.IsLikely3uToolsWindow);
    }
}
