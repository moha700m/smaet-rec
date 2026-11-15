using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmaetRec.Core;
using SmaetRec.Media;

namespace SmaetRec.App;

internal static class DialogSetup
{
    /// <summary>Common look for all secondary windows: dark, right-to-left when Arabic, dark title bar.</summary>
    public static void Apply(Window window)
    {
        window.Style = (Style)Application.Current.FindResource("DialogWindow");
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.FlowDirection = Loc.Instance.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.SourceInitialized += (_, _) => Ui.UseDarkTitleBar(window);
    }
}

/// <summary>Small box that appears where the user clicked with the text tool.</summary>
public sealed class TextEntryWindow : Window
{
    private readonly TextBox _box = new() { Width = 300, FontSize = 18 };
    public string Value => _box.Text.Trim();

    public TextEntryWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = (Brush)Application.Current.FindResource("BarBrush");
        FlowDirection = Loc.Instance.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(_box);
        panel.Children.Add(new TextBlock { Text = Loc.T("TypeText"), Foreground = (Brush)Application.Current.FindResource("SubtleBrush"), FontSize = 12, Margin = new Thickness(2, 6, 2, 0) });
        Content = new Border { BorderBrush = (Brush)Application.Current.FindResource("AccentBrush"), BorderThickness = new Thickness(1.5), Child = panel };
        Loaded += (_, _) => _box.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = Value.Length > 0; e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };
    }
}

/// <summary>"What do you want to show?" — whole screens as big buttons, then a list of open windows.</summary>
public sealed class ScreenPickerWindow : Window
{
    public CaptureTarget? Selected { get; private set; }
    public bool HideRequested { get; private set; }

    public ScreenPickerWindow(bool screenActive)
    {
        DialogSetup.Apply(this);
        Title = Loc.T("PickScreenTitle");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        var subtle = (Brush)FindResource("SubtleBrush");
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = Loc.T("PickScreenTitle"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });

        var monitors = new WrapPanel();
        int n = 1;
        foreach (var monitor in CaptureTarget.Monitors())
        {
            var button = new Button
            {
                Style = (Style)FindResource("BigButton"), MinWidth = 150, Height = 92, Margin = new Thickness(0, 0, 10, 10),
                Content = monitor.IsPrimary ? Loc.T("WholeScreen") : Loc.T("ScreenN", n),
                ToolTip = $"{monitor.Width} × {monitor.Height}",
            };
            Ui.SetGlyph(button, "");
            button.Click += (_, _) => { Selected = monitor; DialogResult = true; };
            monitors.Children.Add(button);
            n++;
        }
        root.Children.Add(monitors);

        var windows = CaptureTarget.Windows();
        if (windows.Count > 0)
        {
            root.Children.Add(new TextBlock { Text = Loc.T("Windows"), Foreground = subtle, Margin = new Thickness(0, 8, 0, 8) });
            var list = new StackPanel();
            foreach (var w in windows.Take(40))
            {
                var button = new Button
                {
                    Style = (Style)FindResource("PlainButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 6), Content = w.Title.Length > 70 ? w.Title[..70] + "…" : w.Title,
                    FlowDirection = FlowDirection.LeftToRight,
                };
                button.Click += (_, _) => { Selected = w; DialogResult = true; };
                list.Children.Add(button);
            }
            root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (screenActive)
        {
            var hide = new Button { Style = (Style)FindResource("PlainButton"), Content = Loc.T("HideScreen") };
            hide.Click += (_, _) => { HideRequested = true; DialogResult = true; };
            actions.Children.Add(hide);
        }
        var cancel = new Button { Style = (Style)FindResource("PlainButton"), Content = Loc.T("Cancel"), IsCancel = true };
        actions.Children.Add(cancel);
        root.Children.Add(actions);
        Content = root;
    }
}
