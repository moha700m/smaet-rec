using System.Windows;
using System.Windows.Threading;

namespace SmaetRec;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Services.Log.Error("Unhandled domain exception", args.ExceptionObject as Exception);
    }

    private static void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Services.Log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            "حدث خطأ غير متوقع. التفاصيل محفوظة في سجل التطبيق.\n\n" + e.Exception.Message,
            "SMAET REC", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
