using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    public ScreenPickerWindow(
        bool screenActive,
        IReadOnlyList<CaptureTarget>? windowTargets = null,
        string? pickerTitle = null,
        string? windowsTitle = null,
        bool showMonitors = true)
    {
        DialogSetup.Apply(this);
        Title = pickerTitle ?? Loc.T("PickScreenTitle");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        var subtle = (Brush)FindResource("SubtleBrush");
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = pickerTitle ?? Loc.T("PickScreenTitle"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });

        var monitors = new WrapPanel();
        int n = 1;
        foreach (var monitor in showMonitors ? CaptureTarget.Monitors() : [])
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
        if (showMonitors) root.Children.Add(monitors);

        var windows = windowTargets ?? CaptureTarget.Windows();
        if (windows.Count > 0)
        {
            root.Children.Add(new TextBlock { Text = windowsTitle ?? Loc.T("Windows"), Foreground = subtle, Margin = new Thickness(0, 8, 0, 8) });
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
        else if (windowTargets is not null)
        {
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("ThreeUToolsWindowMissing"),
                Foreground = subtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8)
            });
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


/// <summary>
/// A refreshable picker for the external 3uTools mirror window. The selected
/// HWND is passed to the existing Windows.Graphics.Capture pipeline.
/// </summary>
public sealed class ThreeUToolsWindowPicker : Window
{
    private readonly StackPanel _items = new();
    private readonly TextBlock _status;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private bool _refreshing;
    private IntPtr[] _lastHandles = [];

    public CaptureTarget? Selected { get; private set; }

    public ThreeUToolsWindowPicker()
    {
        DialogSetup.Apply(this);
        Title = Loc.T("ThreeUToolsPickerTitle");
        Width = 560;
        MinWidth = 460;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("ThreeUToolsPickerTitle"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("ThreeUToolsPickerInstructions"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SubtleBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        });

        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SubtleBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer
        {
            Content = _items,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var refresh = new Button
        {
            Style = (Style)FindResource("AccentButton"),
            Content = Loc.T("ThreeUToolsRefresh"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        refresh.Click += (_, _) => RefreshWindows(force: true);
        actions.Children.Add(refresh);

        var allWindows = new Button
        {
            Style = (Style)FindResource("PlainButton"),
            Content = Loc.T("ThreeUToolsAllWindows"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        allWindows.Click += (_, _) =>
        {
            var picker = new ScreenPickerWindow(false) { Owner = this };
            if (picker.ShowDialog() == true && picker.Selected is { Kind: CaptureKind.Window } target)
            {
                Selected = target;
                DialogResult = true;
            }
        };
        actions.Children.Add(allWindows);

        var cancel = new Button
        {
            Style = (Style)FindResource("PlainButton"),
            Content = Loc.T("Cancel"),
            IsCancel = true
        };
        actions.Children.Add(cancel);
        root.Children.Add(actions);
        Content = root;

        Loaded += (_, _) =>
        {
            RefreshWindows(force: true);
            _timer.Tick += (_, _) => RefreshWindows();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void RefreshWindows(bool force = false)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var windows = CaptureTarget.ThreeUToolsWindows()
                .Where(window => window.StillExists)
                .ToArray();
            var handles = windows.Select(window => window.Handle).ToArray();
            if (!force && handles.SequenceEqual(_lastHandles)) return;

            _lastHandles = handles;
            _items.Children.Clear();
            if (windows.Length == 0)
            {
                _status.Text = Loc.T("ThreeUToolsNoWindow");
                return;
            }

            _status.Text = Loc.T("ThreeUToolsWindowCount", windows.Length);
            foreach (var window in windows)
            {
                string caption = string.IsNullOrWhiteSpace(window.Title)
                    ? window.ProcessName
                    : window.Title;
                var button = new Button
                {
                    Style = (Style)FindResource("PlainButton"),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 6),
                    FlowDirection = FlowDirection.LeftToRight,
                    Content = $"{caption}    {window.Width} × {window.Height}",
                    ToolTip = $"{window.ProcessName}.exe · {window.Width} × {window.Height}"
                };
                button.Click += (_, _) =>
                {
                    if (!window.StillExists)
                    {
                        _status.Text = Loc.T("ThreeUToolsWindowClosed");
                        RefreshWindows(force: true);
                        return;
                    }
                    Selected = window;
                    DialogResult = true;
                };
                _items.Children.Add(button);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            _status.Text = Loc.T("ThreeUToolsScanFailed", error.Message);
        }
        finally { _refreshing = false; }
    }
}
