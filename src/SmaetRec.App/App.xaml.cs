using System.Windows;
using SmaetRec.Core;

namespace SmaetRec.App;

public partial class App : Application
{
    public static Studio Studio { get; private set; } = null!;
    /// <summary>Command-line switches, lower-cased. Development switches start with "--dev-".</summary>
    public static string[] Args { get; private set; } = Array.Empty<string>();

    public static string? ArgValue(string name)
    {
        int i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled", args.Exception);
            MessageBox.Show(args.Exception.Message, "SMAET REC", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write("FATAL " + args.ExceptionObject);

        Log.Write($"SMAET REC {typeof(App).Assembly.GetName().Version} starting on {Environment.OSVersion}");
        Studio = new Studio(simulated: Args.Contains("--dev-simulated-source"));
        if (ArgValue("--lang") is { } lang) Loc.Instance.Language = lang;
        Studio.UpdateTexts();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Studio?.Dispose();
        Log.Write("SMAET REC closed");
        base.OnExit(e);
    }
}
