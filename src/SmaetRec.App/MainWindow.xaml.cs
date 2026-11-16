using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmaetRec.AirPlay;
using SmaetRec.Core;
using SmaetRec.Media;

namespace SmaetRec.App;

public partial class MainWindow : Window
{
    private enum Mode { Arrange, Draw, Effects }
    private enum Tool { None, Pen, Highlighter, Arrow, Ellipse, Rectangle, Text, Blur, Eraser }

    private static readonly uint[] Colors = { 0xFFFF3B30, 0xFFFF9500, 0xFFFFD60A, 0xFF30D158, 0xFF0A84FF, 0xFFFFFFFF, 0xFF1C1C1E };
    private static readonly double[] Thicknesses = { 4, 8, 14 };

    private readonly Studio _studio = App.Studio;
    private readonly MouseWatcher _mouse = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(14) };
    private Mode _mode = Mode.Arrange;
    private Tool _tool = Tool.None;
    private uint _color = Colors[0];
    private double _thickness = Thicknesses[1];
    private RecordingStatus? _recording;
    private string? _toastPath;
    private ConnectPhoneWindow? _connectWindow;

    // Pointer interaction on the preview.
    private bool _down, _moved;
    private Vec2 _downCanvas, _lastCanvas;
    private (int x, int y) _downPixel;
    private Layer? _dragLayer;
    private LayerHandle _dragHandle;
    private Annotation? _drawing;

    private Scene Scene => _studio.Scene;
    private StudioEngine Engine => _studio.Engine;

    public MainWindow()
    {
        InitializeComponent();
        BuildSwatches();
        ApplyLanguage();
        Loc.Instance.PropertyChanged += (_, _) => { ApplyLanguage(); _studio.UpdateTexts(); RefreshStatus(); };

        Preview.PointerDown += OnPointerDown;
        Preview.PointerMove += OnPointerMove;
        Preview.PointerUp += OnPointerUp;
        Preview.PointerLeave += () => Engine.SetPointer(null);
        Preview.Wheel += OnWheel;
        Preview.Repaint += Engine.Invalidate;
        Preview.PixelSizeChanged += (w, h) => Engine.ResizePreview(w, h);

        Engine.ScreenChanged += (active, error) => Dispatcher.BeginInvoke(() =>
        {
            if (!_studio.Simulated) _studio.Settings.ScreenWasShown = active;
            if (error != null) ShowToast(Loc.T("CaptureFailed", error), null, isError: true);
            RefreshStatus();
        });
        Engine.RecordingFailed += error => Dispatcher.BeginInvoke(() => ShowToast(Loc.T("RecordFailed", error), null, isError: true));
        Engine.Error += error => Log.Write("Engine error: " + error);
        _studio.PhoneStateChanged += () => Dispatcher.BeginInvoke(RefreshStatus);
        Scene.Annotations.Changed += () => Dispatcher.BeginInvoke(RefreshHistoryButtons);
        _mouse.Clicked += (x, y) => { if (Scene.Effects.ClickRipples) Engine.AddDesktopClick(x, y); };

        _clock.Tick += (_, _) => RefreshRecordingClock();
        _toastTimer.Tick += (_, _) => HideToast();

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Ui.UseDarkTitleBar(this);
        if (!App.Args.Contains("--dev-allow-self-capture")) Ui.ExcludeFromCapture(this, true);
        var (w, h) = Preview.PixelSize;
        Engine.AttachPreview(Preview.Hwnd, w, h);

        if (_studio.Simulated) Engine.UseSimulatedSources(screen: true, phone: true);
        else
        {
            _studio.StartReceiver();
            if (_studio.Settings.ScreenWasShown && CaptureTarget.PrimaryMonitor() is { } monitor) Engine.ShowScreen(monitor);
        }
        SyncEffectButtons();
        SetMode(App.ArgValue("--dev-mode") switch { "draw" => Mode.Draw, "effects" => Mode.Effects, _ => Mode.Arrange });
        RefreshStatus();
        RefreshHistoryButtons();
        UpdateMouseWatcher();

        switch (App.ArgValue("--dev-open"))
        {
            case "connect": Phone_Click(this, new RoutedEventArgs()); break;
            case "settings": Dispatcher.BeginInvoke(() => Settings_Click(this, new RoutedEventArgs())); break;
            case "screens": Dispatcher.BeginInvoke(() => Screen_Click(this, new RoutedEventArgs())); break;
            case "diag": Dispatcher.BeginInvoke(() => (new ConnectionDiagnosticsWindow(_studio) { Owner = this }).Show()); break;
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Engine.IsRecording)
        {
            e.Cancel = true;
            await StopRecording();
            Close();
            return;
        }
        _mouse.Dispose();
        Engine.DetachPreview();
    }

    private void ApplyLanguage()
    {
        FlowDirection = Loc.Instance.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    // ---------- status ----------

    private void RefreshStatus()
    {
        var state = _studio.Receiver.State;
        bool connected = _studio.PhoneConnected;
        (PhoneStatus.Text, PhoneDot.Fill) = connected
            ? (Loc.T("PhoneConnected"), (Brush)FindResource("GreenBrush"))
            : state switch
            {
                ReceiverState.Waiting => (Loc.T("PhoneWaiting"), (Brush)FindResource("AmberBrush")),
                ReceiverState.Failed => (Loc.T("PhoneError"), (Brush)FindResource("RedBrush")),
                _ => (Loc.T("PhoneOff"), (Brush)FindResource("SubtleBrush")),
            };
        bool screen = Engine.ScreenActive;
        ScreenStatus.Text = Loc.T(screen ? "ScreenOn" : "ScreenOff");
        ScreenDot.Fill = (Brush)FindResource(screen ? "GreenBrush" : "SubtleBrush");
        lock (Scene.Sync)
        {
            BtnHidePhone.IsChecked = !Scene.Phone.Visible;
            BtnHideScreen.IsChecked = !Scene.Screen.Visible;
        }
        RefreshRecordingClock();
    }

    private void RefreshRecordingClock()
    {
        bool recording = Engine.IsRecording && _recording != null;
        RecPill.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        if (!recording) return;
        var t = Engine.RecordingElapsed;
        RecTime.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        RecDot.Opacity = t.Milliseconds < 500 ? 1 : 0.35;
        var parts = new List<string>
        {
            Loc.T(_recording!.Microphone ? "MicOn" : "MicOff"),
            Loc.T(_recording.SystemAudio ? "SystemAudioOn" : "SystemAudioOff"),
        };
        if (_studio.Receiver.State == ReceiverState.Connected) parts.Add(Loc.T("PhoneAudioNote"));
        RecSound.Text = string.Join("  ·  ", parts);
    }

    private void ShowToast(string text, string? path, bool isError = false)
    {
        ToastText.Text = text;
        _toastPath = path;
        ToastButton.Content = Loc.T("ShowVideo");
        ToastButton.Visibility = path != null ? Visibility.Visible : Visibility.Collapsed;
        Toast.Background = Ui.Brush(isError ? 0xFF3A1512 : 0xFF12301C);
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    private void HideToast() { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); }

    private void ToastButton_Click(object sender, RoutedEventArgs e)
    {
        if (_toastPath != null && File.Exists(_toastPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_toastPath}\"") { UseShellExecute = true });
        HideToast();
    }

    // ---------- modes and tools ----------

    private void SetMode(Mode mode)
    {
        _mode = mode;
        BtnDraw.IsChecked = mode == Mode.Draw;
        BtnEffects.IsChecked = mode == Mode.Effects;
        ArrangeBar.Visibility = mode == Mode.Arrange ? Visibility.Visible : Visibility.Collapsed;
        DrawBar.Visibility = mode == Mode.Draw ? Visibility.Visible : Visibility.Collapsed;
        EffectsBar.Visibility = mode == Mode.Effects ? Visibility.Visible : Visibility.Collapsed;
        if (mode != Mode.Draw)
        {
            SetTool(Tool.None);
            ToolSpotlight.IsChecked = ToolMagnifier.IsChecked = false;
            Engine.Spotlight = Engine.Magnifier = false;
            Engine.Invalidate();
        }
        else if (_tool == Tool.None) SetTool(Tool.Pen);
        if (mode != Mode.Arrange) Engine.Selected = null;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        var target = ReferenceEquals(button, BtnDraw) ? Mode.Draw : Mode.Effects;
        SetMode(button.IsChecked == true ? target : Mode.Arrange);
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        foreach (var child in DrawBar.Children.OfType<ToggleButton>())
            if (Enum.TryParse<Tool>(Ui.GetKey(child), out var t)) child.IsChecked = t == tool;
        Preview.Crosshair = tool != Tool.None;
        bool styled = tool is not (Tool.None or Tool.Eraser or Tool.Blur);
        StylePanel.Visibility = styled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        SetTool(button.IsChecked == true && Enum.TryParse<Tool>(Ui.GetKey(button), out var t) ? t : Tool.None);
    }

    private void Pointer_Click(object sender, RoutedEventArgs e)
    {
        // Spotlight and magnifier exclude each other; both follow the pointer over the preview.
        if (ReferenceEquals(sender, ToolSpotlight) && ToolSpotlight.IsChecked == true) ToolMagnifier.IsChecked = false;
        if (ReferenceEquals(sender, ToolMagnifier) && ToolMagnifier.IsChecked == true) ToolSpotlight.IsChecked = false;
        Engine.Spotlight = ToolSpotlight.IsChecked == true;
        Engine.Magnifier = ToolMagnifier.IsChecked == true;
        Engine.Invalidate();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        switch (Ui.GetKey((DependencyObject)sender))
        {
            case "Undo": Scene.Annotations.Undo(); break;
            case "Redo": Scene.Annotations.Redo(); break;
            case "Clear": Scene.Annotations.Clear(); break;
        }
    }

    private void RefreshHistoryButtons()
    {
        BtnUndo.IsEnabled = Scene.Annotations.CanUndo;
        BtnRedo.IsEnabled = Scene.Annotations.CanRedo;
        BtnClear.IsEnabled = Scene.Annotations.Count > 0;
    }

    private void BuildSwatches()
    {
        foreach (var color in Colors)
        {
            var radio = new RadioButton { Style = (Style)FindResource("SwatchRadio"), Background = Ui.Brush(color), GroupName = "color", IsChecked = color == _color };
            radio.Checked += (_, _) => _color = color;
            ColorPanel.Children.Add(radio);
        }
        foreach (var thickness in Thicknesses)
        {
            var radio = new RadioButton { Style = (Style)FindResource("DotRadio"), GroupName = "thickness", IsChecked = thickness == _thickness };
            Ui.SetIconSize(radio, 4 + thickness);
            radio.Checked += (_, _) => _thickness = thickness;
            ThicknessPanel.Children.Add(radio);
        }
        for (int i = 0; i < Compositor.BackgroundCount; i++)
        {
            int index = i;
            var (from, to) = Compositor.BackgroundColors(i);
            var brush = new LinearGradientBrush(ToColor(from), ToColor(to), 45);
            var radio = new RadioButton { Style = (Style)FindResource("SwatchRadio"), Background = brush, GroupName = "background", IsChecked = Scene.Effects.BackgroundIndex == i };
            radio.Checked += (_, _) => { lock (Scene.Sync) Scene.Effects.BackgroundIndex = index; Scene.NotifyChanged(); };
            BackgroundPanel.Children.Add(radio);
        }
        static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    // ---------- arrange ----------

    private void Arrange_Click(object sender, RoutedEventArgs e)
    {
        string key = Ui.GetKey((DependencyObject)sender);
        var selected = Engine.Selected is { } kind ? Scene.Get(kind) : Scene.Phone;
        switch (key)
        {
            case "SideBySide": Scene.ApplyPreset(LayoutPreset.SideBySide); break;
            case "Pip": Scene.ApplyPreset(LayoutPreset.PictureInPicture); break;
            case "Reset": Engine.Zoom.Reset(); Scene.ResetLayout(); break;
            case "Rotate":
                lock (Scene.Sync) selected.RotationDeg = (Math.Round(selected.RotationDeg / 90) * 90 + 90) % 360;
                Scene.NotifyChanged();
                break;
            case "Orient":
                bool landscape;
                lock (Scene.Sync) landscape = !Scene.Phone.Landscape;
                Scene.SetPhoneLandscape(landscape);
                break;
            case "HidePhone":
                lock (Scene.Sync) Scene.Phone.Visible = BtnHidePhone.IsChecked != true;
                if (Engine.Selected == LayerKind.Phone) Engine.Selected = null;
                Scene.NotifyChanged();
                break;
            case "HideScreen":
                lock (Scene.Sync) Scene.Screen.Visible = BtnHideScreen.IsChecked != true;
                if (Engine.Selected == LayerKind.Screen) Engine.Selected = null;
                Scene.NotifyChanged();
                break;
        }
    }

    // ---------- effects ----------

    private void SyncEffectButtons()
    {
        FxCursor.IsChecked = Scene.Effects.CursorHighlight;
        FxClicks.IsChecked = Scene.Effects.ClickRipples;
        FxShadows.IsChecked = Scene.Effects.Shadows;
        int i = 0;
        foreach (var radio in BackgroundPanel.Children.OfType<RadioButton>()) radio.IsChecked = i++ == Scene.Effects.BackgroundIndex;
    }

    private void Effect_Click(object sender, RoutedEventArgs e)
    {
        lock (Scene.Sync)
        {
            Scene.Effects.CursorHighlight = FxCursor.IsChecked == true;
            Scene.Effects.ClickRipples = FxClicks.IsChecked == true;
            Scene.Effects.Shadows = FxShadows.IsChecked == true;
        }
        Scene.NotifyChanged();
        UpdateMouseWatcher();
    }

    /// <summary>The desktop click watcher only runs while it has something to do.</summary>
    private void UpdateMouseWatcher()
    {
        if (Scene.Effects.ClickRipples && Engine.ScreenActive) _mouse.Start(); else _mouse.Stop();
    }

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        switch (Ui.GetKey((DependencyObject)sender))
        {
            case "ZoomIn": Engine.Zoom.ZoomBy(1.5); break;
            case "ZoomOut": Engine.Zoom.ZoomBy(1 / 1.5); break;
            default: Engine.Zoom.Reset(); break;
        }
        Engine.Invalidate();
    }

    // ---------- preview pointer ----------

    private Vec2 ToOutput(int x, int y)
    {
        var (w, h) = Preview.PixelSize;
        return StudioEngine.PreviewToOutput(x, y, w, h, clamp: true)!.Value;
    }

    private Vec2 ToCanvas(int x, int y) => Engine.Zoom.OutputToCanvas(ToOutput(x, y));

    private double CanvasUnitsPerPixel()
    {
        var (w, h) = Preview.PixelSize;
        return Scene.CanvasWidth / Compositor.PreviewRect(w, h).Width / Engine.Zoom.Current;
    }

    private void OnPointerDown(int x, int y)
    {
        var (w, h) = Preview.PixelSize;
        if (StudioEngine.PreviewToOutput(x, y, w, h) == null) return;
        var p = ToCanvas(x, y);
        _down = true; _moved = false; _downCanvas = _lastCanvas = p; _downPixel = (x, y);

        if (_mode == Mode.Draw && _tool != Tool.None)
        {
            switch (_tool)
            {
                case Tool.Eraser:
                    Scene.Annotations.EraseAt(p, 10 * CanvasUnitsPerPixel());
                    break;
                case Tool.Text:
                    _down = false;
                    AskForText(x, y, p);
                    break;
                default:
                    _drawing = new Annotation { Kind = Enum.Parse<AnnotationKind>(_tool.ToString()), Color = _color, Thickness = _thickness, Points = { p } };
                    if (!_drawing.IsFreehand) _drawing.Points.Add(p);
                    Scene.Annotations.SetInProgress(_drawing);
                    break;
            }
            return;
        }
        if (_mode == Mode.Arrange)
        {
            var selected = Engine.Selected is { } kind ? Scene.Get(kind) : null;
            var (layer, handle) = Scene.HitTest(p, selected, 16 * CanvasUnitsPerPixel());
            _dragLayer = layer; _dragHandle = handle;
            Engine.Selected = layer?.Kind;
        }
    }

    private void OnPointerMove(int x, int y)
    {
        var (w, h) = Preview.PixelSize;
        Engine.SetPointer(StudioEngine.PreviewToOutput(x, y, w, h));
        if (!_down) return;
        var p = ToCanvas(x, y);
        if (Math.Abs(x - _downPixel.x) + Math.Abs(y - _downPixel.y) > 4) _moved = true;

        if (_drawing != null)
        {
            if (_drawing.IsFreehand)
            {
                if ((p - _drawing.Points[^1]).Length >= 1.5 * CanvasUnitsPerPixel()) _drawing.Points.Add(p);
            }
            else _drawing.Points[1] = p;
            Scene.Annotations.SetInProgress(_drawing);
        }
        else if (_mode == Mode.Draw && _tool == Tool.Eraser)
        {
            Scene.Annotations.EraseAt(p, 10 * CanvasUnitsPerPixel());
        }
        else if (_dragLayer != null && _moved)
        {
            lock (Scene.Sync)
            {
                switch (_dragHandle)
                {
                    case LayerHandle.Body: _dragLayer.MoveBy(p - _lastCanvas); break;
                    case LayerHandle.Rotate: _dragLayer.RotateTo(p); break;
                    case LayerHandle.None: break;
                    default: _dragLayer.ResizeTo(p); break;
                }
            }
            Scene.NotifyChanged();
        }
        _lastCanvas = p;
    }

    private void OnPointerUp(int x, int y)
    {
        if (!_down) return;
        _down = false;
        if (_drawing != null)
        {
            Scene.Annotations.Add(_drawing);
            _drawing = null;
        }
        else if (!_moved && Scene.Effects.ClickRipples && (_mode == Mode.Effects || (_mode == Mode.Draw && _tool == Tool.None)))
        {
            // Outside arranging, a plain click on the picture shows a touch ripple there (useful for pointing at the iPhone).
            Engine.AddRipple(_downCanvas);
        }
        _dragLayer = null;
    }

    private void OnWheel(int x, int y, int delta)
    {
        var (w, h) = Preview.PixelSize;
        if (StudioEngine.PreviewToOutput(x, y, w, h) == null) return;
        Engine.Zoom.ZoomBy(delta > 0 ? 1.25 : 0.8, ToCanvas(x, y));
        Engine.Invalidate();
    }

    private void AskForText(int x, int y, Vec2 canvasPoint)
    {
        var (sx, sy) = Preview.ToScreen(x, y);
        var dpi = VisualTreeHelper.GetDpi(this);
        var dialog = new TextEntryWindow { Owner = this, Left = sx / dpi.DpiScaleX - (Loc.Instance.IsRtl ? 300 : 0), Top = sy / dpi.DpiScaleY - 10 };
        if (dialog.ShowDialog() == true)
            Scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Text, Text = dialog.Value, Color = _color, Thickness = _thickness, Points = { canvasPoint } });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Z) { Scene.Annotations.Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Scene.Annotations.Redo(); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            if (_drawing != null) { _drawing = null; _down = false; Scene.Annotations.SetInProgress(null); }
            else if (_mode != Mode.Arrange) SetMode(Mode.Arrange);
            else Engine.Selected = null;
            e.Handled = true;
        }
    }

    // ---------- main actions ----------

    private void Phone_Click(object sender, RoutedEventArgs e)
    {
        // Opening the connect flow should always reveal the iPhone frame, even if a previous
        // session left the layer hidden.
        lock (Scene.Sync) Scene.Phone.Visible = true;
        Scene.NotifyChanged();
        if (_connectWindow is { IsLoaded: true }) { _connectWindow.Activate(); return; }
        _connectWindow = new ConnectPhoneWindow(_studio) { Owner = this };
        _connectWindow.Show();
    }

    private void Screen_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ScreenPickerWindow(Engine.ScreenActive) { Owner = this };
        if (picker.ShowDialog() != true) return;
        if (picker.HideRequested) Engine.HideScreen();
        else if (picker.Selected != null) ShowCapturedWindow(picker.Selected);
        Dispatcher.BeginInvoke(UpdateMouseWatcher, DispatcherPriority.Background);
    }

    /// <summary>
    /// Routes an externally selected window (including 3uTools Realtime Screen)
    /// into the same live scene, compositor, effects, and recorder as any other source.
    /// </summary>
    public void ShowCapturedWindow(CaptureTarget target, bool fullCanvas = false)
    {
        if (target.Kind != CaptureKind.Window || !target.StillExists)
        {
            ShowToast(Loc.T("CaptureWindowClosed"), null, isError: true);
            return;
        }

        if (fullCanvas)
        {
            int width = Math.Max(1, target.Width);
            int height = Math.Max(1, target.Height);
            double aspect = width / (double)height;
            const double margin = 40;
            double scale = Math.Min(
                (Scene.CanvasWidth - margin * 2) / width,
                (Scene.CanvasHeight - margin * 2) / height);

            lock (Scene.Sync)
            {
                Scene.Phone.Visible = false;
                Scene.Screen.Visible = true;
                Scene.Screen.Aspect = aspect;
                Scene.Screen.Width = width * scale;
                Scene.Screen.CenterX = Scene.CanvasWidth / 2;
                Scene.Screen.CenterY = Scene.CanvasHeight / 2;
                Scene.Screen.RotationDeg = 0;
            }
        }
        else
        {
            lock (Scene.Sync) Scene.Screen.Visible = true;
        }

        Scene.NotifyChanged();
        Engine.ShowScreen(target);
        Dispatcher.BeginInvoke(() =>
        {
            UpdateMouseWatcher();
            RefreshStatus();
        }, DispatcherPriority.Background);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_studio) { Owner = this };
        window.ShowDialog();
        if (window.ProjectOpened) { SyncEffectButtons(); RefreshStatus(); }
        if (!Engine.IsRecording)
        {
            Engine.Fps = _studio.Settings.Fps;
            Engine.SetOutputSize(_studio.Settings.OutputWidth, _studio.Settings.OutputHeight);
        }
        _studio.SaveSettings();
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (Engine.IsRecording) return;
        if (!Engine.ScreenActive && !_studio.PhoneConnected)
        {
            ShowToast(Loc.T("NothingToRecord"), null, isError: true);
            return;
        }
        HideToast();
        _recording = Engine.StartRecording(_studio.Settings, out var error);
        if (_recording == null)
        {
            ShowToast(Loc.T("RecordFailed", error ?? ""), null, isError: true);
            return;
        }
        BtnRecord.IsEnabled = false;
        BtnStop.IsEnabled = true;
        BtnSettings.IsEnabled = false;
        _clock.Start();
        RefreshRecordingClock();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopRecording();

    private async Task StopRecording()
    {
        if (!Engine.IsRecording) return;
        BtnStop.IsEnabled = false;
        var result = await Engine.StopRecordingAsync();
        _clock.Stop();
        _recording = null;
        BtnRecord.IsEnabled = true;
        BtnSettings.IsEnabled = true;
        RefreshRecordingClock();
        if (result == null) return;
        if (result.Error != null) ShowToast(Loc.T("RecordFailed", result.Error), File.Exists(result.Path) ? result.Path : null, isError: true);
        else ShowToast(Loc.T("Saved"), result.Path);
    }
}
