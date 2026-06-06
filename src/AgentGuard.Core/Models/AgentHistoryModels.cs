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
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivity { get; set; }
    public int RecordCount { get; set; }
    public long TotalSize { get; set; }
}

public sealed class AgentSessionScanResult
{
    public List<AgentSessionSummary> Sessions { get; set; } = [];
    public List<AgentHistoryRecord> Records { get; set; } = [];
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
