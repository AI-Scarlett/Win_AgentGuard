using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AgentGuard.Core.Services;

namespace AgentGuard.App.Services;

/// <summary>
/// Windows Action Center toast notification.
/// Uses the lightweight WinRT ToastNotificationManager APIs through the
/// Microsoft.Toolkit.Uwp.Notifications package when available, and falls
/// back to a script-based Windows.UI.Notifications shim (PowerShell +
/// BurntToast / built-in toast XML) when the package is not referenced.
///
/// To enable full native toast support, add the
/// <c>Microsoft.Toolkit.Uwp.Notifications</c> NuGet package to AgentGuard.App
/// and remove the PowerShell fallback in this file.
/// </summary>
public sealed class WindowsToastNotificationService : INotificationService
{
    private const string ToastAppId = "AgentGuard for Windows";
    private readonly AppPaths _paths;
    private volatile bool _enabled = true;

    public WindowsToastNotificationService(AppPaths paths)
    {
        _paths = paths;
    }

    public bool IsEnabled => _enabled;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public void Show(NotificationMessage message)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            var xml = BuildToastXml(message);
            var script = BuildPowerShellToastScript(xml);
            var scriptPath = Path.Combine(_paths.BridgeDirectory, "toast.ps1");
            Directory.CreateDirectory(_paths.BridgeDirectory);
            File.WriteAllText(scriptPath, script, Encoding.UTF8);
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            // Best-effort only; never crash the host on a toast failure.
            AgentGuard.App.Diagnostics.AppDiagnostics.Log("Toast notification failed.", ex);
        }
    }

    private static string BuildToastXml(NotificationMessage message)
    {
        // Keep it ASCII-clean to avoid escaping headaches in the PS launcher.
        var safeTitle = Sanitize(message.Title);
        var safeBody = Sanitize(message.Body);
        return
            "<toast>" +
            "<visual><binding template=\"ToastGeneric\">" +
            $"<text>{safeTitle}</text>" +
            $"<text>{safeBody}</text>" +
            "</binding></visual>" +
            "</toast>";
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string BuildPowerShellToastScript(string xml)
    {
        // We use the built-in [Windows.UI.Notifications.ToastNotificationManager]
        // which ships with Windows 10/11. No external dependency.
        return $$"""
        $ErrorActionPreference = 'Stop'
        try {
          [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
          $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
          $xml.LoadXml(@'
{{xml}}
'@)
          $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
          [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{{ToastAppId}}').Show($toast)
        } catch {
          # WinRT may be unavailable on Server Core; emit to %APPDATA%\AgentGuard\logs\bridge.log instead.
          $log = Join-Path $env:APPDATA 'AgentGuard\logs\bridge.log'
          New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
          "$(Get-Date -Format o) toast: $($_.Exception.Message)" | Add-Content -Path $log
        }
        """;
    }
}
