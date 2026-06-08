using AgentGuard.Core.Localization;

namespace AgentGuard.Core.Models;

public enum AlertType
{
    BatchDelete,
    BatchModify,
    SensitiveFile,
    SensitiveContent,
    ProtectedDirectory,
    ProcessLaunch,
    ProcessExit,
    CommandBlocked
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum OperationType
{
    Create,
    Modify,
    Delete,
    Read,
    Move,
    Rename,
    Execute
}

public enum CommandListType
{
    Blacklist,
    Whitelist,
    Unclassified
}

public sealed class GuardAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public AlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsRead { get; set; }
}

public sealed class AlertRule
{
    public int BatchDeleteThreshold { get; set; } = 5;
    public int BatchModifyThreshold { get; set; } = 10;
    public double BatchTimeWindowSeconds { get; set; } = 30.0;
    public bool SensitiveFileDetectionEnabled { get; set; } = true;
    public bool SensitiveContentDetectionEnabled { get; set; } = true;
    public bool ProcessLaunchAlertEnabled { get; set; } = true;
    public bool ProtectedDirectoryAlertEnabled { get; set; } = true;
    public bool NotificationEnabled { get; set; } = true;
    public double AlertCooldownSeconds { get; set; } = 60.0;
    public bool CommandGuardEnabled { get; set; } = true;
}

public sealed class CommandRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Pattern { get; set; } = "";
    public bool IsRegex { get; set; }
    public CommandListType ListType { get; set; } = CommandListType.Unclassified;
    public string Description { get; set; } = "";
    public string Consequence { get; set; } = "";
    public string Source { get; set; } = "custom";
    public int TotalCallCount { get; set; }
    public int TodayCallCount { get; set; }
    public string LastCalledBy { get; set; } = "";
    /// <summary>
    /// True when the rule was auto-discovered from the audit log
    /// rather than authored by the user. v2.1.2 (Command guard
    /// three-category with auto-discovery).
    /// </summary>
    public bool AutoDiscovered { get; set; }
    public string? DiscoveredFromAgent { get; set; }
    public DateTimeOffset? DiscoveredAt { get; set; }

    public static List<CommandRule> DefaultRules() =>
    [
        new()
        {
            Pattern = "rm -rf",
            ListType = CommandListType.Blacklist,
            Description = CoreText.RecursiveDeleteDescription,
            Consequence = CoreText.RecursiveDeleteConsequence,
            Source = "default"
        },
        new()
        {
            Pattern = "del /s /q",
            ListType = CommandListType.Blacklist,
            Description = CoreText.RecursiveWindowsDeleteDescription,
            Consequence = CoreText.RecursiveWindowsDeleteConsequence,
            Source = "default"
        },
        new()
        {
            Pattern = "Remove-Item",
            ListType = CommandListType.Unclassified,
            Description = CoreText.PowerShellDeletionDescription,
            Consequence = CoreText.PowerShellDeletionConsequence,
            Source = "default"
        },
        new()
        {
            Pattern = "git status",
            ListType = CommandListType.Whitelist,
            Description = CoreText.InspectRepositoryState,
            Source = "default"
        },
        new()
        {
            Pattern = "dotnet build",
            ListType = CommandListType.Whitelist,
            Description = CoreText.BuildDotNetProject,
            Source = "default"
        }
    ];
}

public sealed class CommandCheckResult
{
    public bool IsBlocked { get; init; }
    public bool IsAllowed { get; init; }
    public CommandRule? Rule { get; init; }
    public string Message { get; init; } = "";
}

public sealed class OperationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string AgentName { get; set; } = CoreText.AgentCenter;
    public OperationType OperationType { get; set; } = OperationType.Modify;
    public string TargetPath { get; set; } = "";
    public string Detail { get; set; } = "";
    public long FileSize { get; set; }
    public string ProcessName { get; set; } = "";
    public string? ToolInfo { get; set; }
}

public sealed class ProcessLifecycleEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string EventType { get; set; } = "";
    public int ProcessId { get; set; }
    public int ParentProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string AgentName { get; set; } = "";
}

public sealed class HourlyStats
{
    public string Id { get; set; } = "";
    public DateTimeOffset Hour { get; set; }
    public int CreateCount { get; set; }
    public int ModifyCount { get; set; }
    public int DeleteCount { get; set; }
    public int ReadCount { get; set; }
    public int MoveCount { get; set; }
    public int RenameCount { get; set; }
    public int ExecuteCount { get; set; }
}

public sealed class AuditReport
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int TotalOperations { get; set; }
    public Dictionary<string, int> AgentBreakdown { get; set; } = [];
    public Dictionary<string, int> OperationTypeBreakdown { get; set; } = [];
    public int AlertCount { get; set; }
    public int CriticalAlertCount { get; set; }
    public List<string> TopTargetPaths { get; set; } = [];
    public string Summary { get; set; } = "";
}
