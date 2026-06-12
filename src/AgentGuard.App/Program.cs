using System.Windows;
using AgentGuard.App.Diagnostics;

namespace AgentGuard.App;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        try
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Fatal failure before WPF startup.", ex);
            MessageBox.Show(
                $"AgentGuard could not initialize.\n\nLog: {AppDiagnostics.LogPath}\n\n{ex}",
                "AgentGuard startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return -1;
        }
    }
}
