namespace AgentGuard.Core.Models;

public enum AgentHistoryOperation
{
    Unknown,
    Read,
    Write,
    Edit,
    Delete,
    Move,
    Rename,
    Search,
    Execute,
    Fetch,
    List,
    Submit,
    Plan,
    Message
}

public sealed class AgentHistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string AgentName { get; set; } = "";
    public string SessionId { get; set; } = "";
    public AgentHistoryOperation Operation { get; set; } = AgentHistoryOperation.Unknown;
    public string TargetPath { get; set; } = "";
    public string Detail { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public long FileSize { get; set; }
    public string Project { get; set; } = "";
}

public sealed class AgentSessionSummary
{
    public string Id { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Project { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivity { get; set; }
    public int RecordCount { get; set; }
    public long TotalSize { get; set; }
}

public sealed class AgentTokenUsageRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string AgentName { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Project { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Model { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public ulong InputTokens { get; set; }
    public ulong OutputTokens { get; set; }
    public ulong CachedInputTokens { get; set; }
    public ulong CacheCreationTokens { get; set; }
    public ulong ReasoningTokens { get; set; }
    public ulong TotalTokens { get; set; }
    public ulong CumulativeInputTokens { get; set; }
    public ulong CumulativeOutputTokens { get; set; }
    public ulong CumulativeCachedInputTokens { get; set; }
    public ulong CumulativeReasoningTokens { get; set; }
    public ulong CumulativeTotalTokens { get; set; }
    public ulong ContextWindow { get; set; }
    public double ContextUsedPercent { get; set; }
    public double FiveHourUsagePercent { get; set; }
    public string FiveHourRemaining { get; set; } = "";
    public double SevenDayUsagePercent { get; set; }
    public string SevenDayRemaining { get; set; } = "";
    public string PlanType { get; set; } = "";
}

public sealed class AgentSessionScanResult
{
    public List<AgentSessionSummary> Sessions { get; set; } = [];
    public List<AgentHistoryRecord> Records { get; set; } = [];
    public List<AgentTokenUsageRecord> TokenUsage { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.Now;
    public int ScannedFileCount { get; set; }

    public Dictionary<string, int> OperationBreakdown =>
        Records.GroupBy(item => item.Operation.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> AgentBreakdown =>
        Records.GroupBy(item => string.IsNullOrWhiteSpace(item.AgentName) ? "Unknown" : item.AgentName)
            .ToDictionary(g => g.Key, g => g.Count());
}

public sealed class AgentMonitorOverview
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public int ActiveSessionCount { get; set; }
    public int HistoricalSessionCount { get; set; }
    public int ToolCallCount { get; set; }
    public int CommandCount { get; set; }
    public int FileAccessCount { get; set; }
    public ulong TotalTokens { get; set; }
    public double AverageContextPercent { get; set; }
    public string RateLimitSummary { get; set; } = "";
    public List<AgentMonitorOverviewRow> Rows { get; set; } = [];
}

public sealed class AgentMonitorOverviewRow
{
    public string Id { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Project { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.Now;
    public int ToolCalls { get; set; }
    public int Commands { get; set; }
    public int FileAccesses { get; set; }
    public ulong TotalTokens { get; set; }
    public double ContextUsedPercent { get; set; }
    public string RateLimitText { get; set; } = "";
    public string SourceFile { get; set; } = "";
}
