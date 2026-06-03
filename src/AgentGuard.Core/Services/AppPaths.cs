namespace AgentGuard.Core.Services;

public sealed class AppPaths
{
    public string AppDataRoot { get; }
    public string UserProfile { get; }
    public string BridgeDirectory { get; }
    public string LogsDirectory { get; }
    public string SessionsPath => Path.Combine(AppDataRoot, "sessions.json");
    public string RawEventsPath => Path.Combine(AppDataRoot, "raw-events.jsonl");
    public string AuditRecordsPath => Path.Combine(AppDataRoot, "audit-records.json");
    public string AlertsPath => Path.Combine(AppDataRoot, "alerts.json");
    public string AlertRulePath => Path.Combine(AppDataRoot, "alert-rule.json");
    public string CommandRulesPath => Path.Combine(AppDataRoot, "command-rules.json");
    public string ProtectedDirectoriesPath => Path.Combine(AppDataRoot, "protected-directories.json");
    public string HourlyStatsPath => Path.Combine(AppDataRoot, "hourly-stats.json");
    public string BridgeScriptPath => Path.Combine(BridgeDirectory, "agentguard-bridge.ps1");
    public string BridgeLogPath => Path.Combine(LogsDirectory, "bridge.log");

    public AppPaths(string? appDataRoot = null, string? userProfile = null)
    {
        UserProfile = userProfile
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? "";

        AppDataRoot = appDataRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentGuard");

        BridgeDirectory = Path.Combine(AppDataRoot, "bin");
        LogsDirectory = Path.Combine(AppDataRoot, "logs");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(BridgeDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public string ExpandUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path == "~")
        {
            return UserProfile;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(UserProfile, path[2..].Replace('/', Path.DirectorySeparatorChar));
        }

        return Environment.ExpandEnvironmentVariables(path);
    }
}
