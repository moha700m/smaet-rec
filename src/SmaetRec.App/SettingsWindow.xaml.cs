using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using SmaetRec.Core;

namespace SmaetRec.App;

/// <summary>The one small place for everything that is not needed every day.</summary>
public partial class SettingsWindow : Window
{
    private readonly Studio _studio;
    private bool _loading = true;
    public bool ProjectOpened { get; private set; }

    public SettingsWindow(Studio studio)
    {
        _studio = studio;
        DialogSetup.Apply(this);
        InitializeComponent();
        var s = studio.Settings;
        LangAr.IsChecked = s.Language == "ar"; LangEn.IsChecked = s.Language == "en";
        Q720.IsChecked = s.Quality == VideoQuality.Hd720; Q1080.IsChecked = s.Quality == VideoQuality.FullHd1080; Q4K.IsChecked = s.Quality == VideoQuality.Uhd4K;
        Fps30.IsChecked = s.Fps == 30; Fps60.IsChecked = s.Fps == 60;
        Mic.IsChecked = s.RecordMicrophone; SystemAudio.IsChecked = s.RecordSystemAudio;
        PhoneName.Text = s.ReceiverName;
        ShowFolder();
        _loading = false;
    }

    private string Folder => string.IsNullOrWhiteSpace(_studio.Settings.OutputFolder) ? AppPaths.DefaultVideoFolder : _studio.Settings.OutputFolder!;

    private void ShowFolder() { FolderText.Text = Folder; FolderText.ToolTip = Folder; }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _studio.Settings;
        s.Quality = Q720.IsChecked == true ? VideoQuality.Hd720 : Q4K.IsChecked == true ? VideoQuality.Uhd4K : VideoQuality.FullHd1080;
        s.Fps = Fps60.IsChecked == true ? 60 : 30;
        s.RecordMicrophone = Mic.IsChecked == true;
        s.RecordSystemAudio = SystemAudio.IsChecked == true;
        string language = LangEn.IsChecked == true ? "en" : "ar";
        if (language != s.Language)
        {
            s.Language = language;
            Loc.Instance.Language = language;
            FlowDirection = Loc.Instance.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
        _studio.SaveSettings();
    }

    private void PhoneName_LostFocus(object sender, RoutedEventArgs e) => ApplyPhoneName();

    private void ApplyPhoneName()
    {
        string name = PhoneName.Text.Trim();
        if (name.Length == 0 || name == _studio.Settings.ReceiverName) return;
        _studio.Settings.ReceiverName = name;
        _studio.SaveSettings();
        if (!_studio.Simulated) _studio.StartReceiver();
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(Folder) ? Folder : null };
        if (dialog.ShowDialog(this) != true) return;
        _studio.Settings.OutputFolder = dialog.FolderName;
        _studio.SaveSettings();
        ShowFolder();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Say(ex.Message, error: true); }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = $"{Loc.T("ProjectFiles")}|*{ProjectFile.Extension}", DefaultExt = ProjectFile.Extension, AddExtension = true,
            FileName = Path.GetFileName(_studio.Settings.LastProjectPath) is { Length: > 0 } name ? name : "SMAET REC",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ProjectFile.Save(dialog.FileName, _studio.Scene);
            _studio.Settings.LastProjectPath = dialog.FileName;
            _studio.SaveSettings();
            Say(Loc.T("ProjectSaved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say(ex.Message, error: true); }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = $"{Loc.T("ProjectFiles")}|*{ProjectFile.Extension}" };
        if (dialog.ShowDialog(this) != true) return;
        if (ProjectFile.TryLoad(dialog.FileName, _studio.Scene))
        {
            _studio.Settings.LastProjectPath = dialog.FileName;
            _studio.SaveSettings();
            ProjectOpened = true;
            Close();
        }
        else Say(Loc.T("ProjectOpenFailed"), error: true);
    }

    private void Say(string text, bool error = false)
    {
        Message.Text = text;
        Message.Foreground = (System.Windows.Media.Brush)FindResource(error ? "RedBrush" : "GreenBrush");
        Message.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        ApplyPhoneName();
        Close();
    }
}
