using System.IO;
using System.Text;

namespace SmaetRec.Services;

public static class Log
{
    private static readonly object Gate = new();
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMAET REC");
    private static readonly string FilePath = Path.Combine(Dir, "smaet-rec.log");

    public static string LogFolder => Dir;

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(level).Append("] ").Append(message);
                if (ex != null) sb.AppendLine().Append(ex);
                File.AppendAllText(FilePath, sb.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
