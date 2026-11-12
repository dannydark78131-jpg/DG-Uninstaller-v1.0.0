using System.Windows;
using System.Windows.Threading;
using DG.Uninstaller.Services;

namespace DG.Uninstaller;

public partial class App : Application
{
    public static LogService Log { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Initialize();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled domain exception: " + args.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception: " + args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception: " + e.Exception);
        MessageBox.Show(
            "An unexpected error occurred.\n\n" + e.Exception.Message +
            "\n\nDetails were written to the log.",
            "DG Uninstaller & Cleaner",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
