using System.Windows;
using AgentGuard.App.Diagnostics;
using AgentGuard.App.Services;
using AgentGuard.Core.Services;

namespace AgentGuard.App;

public partial class App : Application
{
    public static INotificationService Notifications { get; private set; } = new NullNotificationService();

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

        try
        {
            Notifications = new WindowsToastNotificationService(new AppPaths());
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Toast notification service init failed; using null service.", ex);
            Notifications = new NullNotificationService();
        }

        base.OnStartup(e);
    }
}
