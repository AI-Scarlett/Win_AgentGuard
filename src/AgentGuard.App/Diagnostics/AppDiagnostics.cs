using System.IO;
using System.Text;

namespace AgentGuard.App.Diagnostics;

internal static class AppDiagnostics
{
    private static readonly object Gate = new();

    public static string LogPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AgentGuard", "logs", "app.log");
        }
    }

    public static void Log(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? ".");
                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append(' ')
                    .AppendLine(message);

                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(LogPath, builder.ToString());
            }
        }
        catch
        {
            // Logging must never become the reason the app cannot start.
        }
    }
}
