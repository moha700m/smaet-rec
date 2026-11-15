using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SmaetRec.Services;

namespace SmaetRec.UI;

public partial class MainWindow : Window
{
    private bool _isMaximized;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowStyle();
        StateChanged += (_, _) => UpdateMaximizeIcon();
        Loc.I.LanguageChanged += ApplyFlow;
        ApplyFlow();
        Loaded += (_, _) =>
        {
            SetStatus(Loc.I["Status.Ready"], (Brush)FindResource("Accent2"));
        };
    }

    private void ApplyFlow()
    {
        RootLayout.FlowDirection = Loc.I.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void SetStatus(string text, Brush dot)
    {
        StatusText.Text = text;
        StatusDot.Fill = dot;
    }

    private void ApplyWindowStyle()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        int round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore */ }
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateMaximizeIcon()
    {
        MaxIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M8,8 H16 V16 H8 Z M6,6 H14"
            : "M6,6 H18 V18 H6 Z");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Filled in by the settings module.
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Screen_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Draw_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Effects_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
    }
}
