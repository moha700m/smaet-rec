using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmaetRec.Core;

public enum VideoQuality { Hd720, FullHd1080, Uhd4K }

public sealed class AppSettings
{
    public string Language { get; set; } = "ar";
    public VideoQuality Quality { get; set; } = VideoQuality.FullHd1080;
    public int Fps { get; set; } = 30;
    public bool RecordMicrophone { get; set; } = true;
    public bool RecordSystemAudio { get; set; } = true;
    public string? OutputFolder { get; set; }
    public string ReceiverName { get; set; } = "SMAET REC";
    public string? LastProjectPath { get; set; }
    /// <summary>Whether the computer screen was showing when the app last closed (it is shown again on start).</summary>
    public bool ScreenWasShown { get; set; }

    [JsonIgnore] public int OutputWidth => Quality switch { VideoQuality.Hd720 => 1280, VideoQuality.Uhd4K => 3840, _ => 1920 };
    [JsonIgnore] public int OutputHeight => Quality switch { VideoQuality.Hd720 => 720, VideoQuality.Uhd4K => 2160, _ => 1080 };
    [JsonIgnore] public int BitrateBps => (Quality switch { VideoQuality.Hd720 => 6_000_000, VideoQuality.Uhd4K => 40_000_000, _ => 12_000_000 }) * (Fps >= 60 ? 3 : 2) / 2;

    public void Normalize()
    {
        if (Language != "ar" && Language != "en") Language = "ar";
        if (Fps != 30 && Fps != 60) Fps = 30;
        if (string.IsNullOrWhiteSpace(ReceiverName)) ReceiverName = "SMAET REC";
    }
}

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T? TryLoad<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes atomically so a crash mid-save never leaves a broken file.</summary>
    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>A saved project: the scene and nothing else. File extension .smaet (JSON).</summary>
public sealed class ProjectFile
{
    public const string Extension = ".smaet";
    public int Version { get; set; } = 1;
    public string App { get; set; } = "SMAET REC";
    public SceneData Scene { get; set; } = new();

    public static void Save(string path, Scene scene) => JsonStore.Save(path, new ProjectFile { Scene = scene.ToData() });

    public static bool TryLoad(string path, Scene scene)
    {
        var file = JsonStore.TryLoad<ProjectFile>(path);
        if (file?.Scene == null || file.App != "SMAET REC") return false;
        scene.Load(file.Scene);
        return true;
    }
}

public static class AppPaths
{
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmaetRec");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string SessionFile => Path.Combine(DataFolder, "last-session" + ProjectFile.Extension);
    public static string LogFile => Path.Combine(DataFolder, "log.txt");
    public static string DefaultVideoFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SMAET REC");

    public static string NewRecordingPath(string? folder, DateTime now)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? DefaultVideoFolder : folder;
        Directory.CreateDirectory(folder);
        string name = $"SMAET REC {now:yyyy-MM-dd HH-mm-ss}";
        string path = Path.Combine(folder, name + ".mp4");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name} ({i}).mp4");
        return path;
    }
}

public static class Log
{
    private static readonly object Lock = new();
    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > 2_000_000) file.Delete();
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public static void Error(string where, Exception e) => Write($"ERROR {where}: {e}");
}
