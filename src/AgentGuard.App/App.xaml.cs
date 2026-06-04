using System.Windows;
using AgentGuard.App.Diagnostics;

namespace AgentGuard.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDiagnostics.Log("AgentGuard starting.");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppDiagnostics.Log("Unhandled app-domain exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.Log("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.Log("Dispatcher exception.", args.Exception);
            args.Handled = true;
            MessageBox.Show(
                $"AgentGuard hit an error but will stay open.\n\nLog: {AppDiagnostics.LogPath}\n\n{args.Exception.Message}",
                "AgentGuard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        base.OnStartup(e);
    }
}
