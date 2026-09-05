using System.Windows;
using System.Windows.Threading;
using Bitirim.Clothing.Desktop.Host;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Desktop;

public partial class App : Application
{
    private AppSession? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _session = new AppSession();
        _session.Log.Info($"Bitirim Clothing Creator starting "
                          + $"({(Paths.IsPortable ? "portable" : "installed")}, data at {Paths.Root})");
        Paths.CleanTemp();

        // A crash must produce a readable message and a log entry, never a
        // silent disappearance or a raw .NET dialog.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                _session?.Log.Error("Unhandled background exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _session?.Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var startupFile = e.Args.FirstOrDefault(a => !a.StartsWith('-'));

        var window = new MainWindow(_session, startupFile);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var reference = _session?.Log.Error("Unhandled UI exception", e.Exception) ?? "unknown";
        MessageBox.Show(
            "Something went wrong and the action could not be completed.\n\n"
            + $"Reference: {reference}\n"
            + $"Details were written to:\n{Path.Combine(Paths.Logs, "errors.log")}",
            "Bitirim Clothing Creator",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _session?.Log.Info("Shutting down.");
            _session?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        catch { /* shutdown must not throw */ }

        base.OnExit(e);
    }
}
