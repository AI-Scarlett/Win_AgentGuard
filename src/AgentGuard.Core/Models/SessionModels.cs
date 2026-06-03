using AgentGuard.Core.Localization;

namespace AgentGuard.Core.Models;

public enum SessionPhase
{
    Ready,
    Idle,
    Processing,
    WaitingApproval,
    WaitingInput,
    Compacting,
    Done,
    Error,
    Interrupted
}

public enum PendingRequestType
{
    Permission,
    Question,
    Plan
}

public sealed class TokenUsage
{
    public ulong Input { get; set; }
    public ulong Output { get; set; }
    public ulong CacheRead { get; set; }
    public ulong CacheCreate { get; set; }
}

public sealed class RateLimitInfo
{
    public double FiveHourUsage { get; set; }
    public string FiveHourRemaining { get; set; } = "";
    public double SevenDayUsage { get; set; }
    public string SevenDayRemaining { get; set; } = "";
    public string? Provider { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class PendingPermission
{
    public string? ToolUseId { get; set; }
    public string ToolName { get; set; } = "";
    public string ToolInput { get; set; } = "";
    public string? Diff { get; set; }
    public List<string> Options { get; set; } = [];
    public string? Source { get; set; }
    public string? SourceLabel { get; set; }
}

public sealed class PendingQuestion
{
    public string Question { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public List<string> Descriptions { get; set; } = [];
    public string? Header { get; set; }
    public bool MultiSelect { get; set; }
    public string? ResponseMode { get; set; }
}

public sealed class PendingPlan
{
    public string Title { get; set; } = "Plan";
    public string Content { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
}

public sealed class ApprovalHistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool IsPending => ResolvedAt is null;
}

public sealed class ToolResult
{
    public string ToolUseId { get; set; } = Guid.NewGuid().ToString("N");
    public string ToolName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public string? ToolInput { get; set; }
}

public sealed class SessionState
{
    public string Id { get; set; } = "";
    public string AgentType { get; set; } = CoreText.AgentCenter;
    public string? EngineLabel { get; set; }
    public string Project { get; set; } = CoreText.AgentSession;
    public string Cwd { get; set; } = "";
    public string Terminal { get; set; } = "";
    public SessionPhase Phase { get; set; } = SessionPhase.Ready;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? EndedAt { get; set; }
    public TokenUsage Tokens { get; set; } = new();
    public RateLimitInfo? RateLimits { get; set; }
    public string? StatusLineText { get; set; }
    public PendingPermission? PendingPermission { get; set; }
    public PendingQuestion? PendingQuestion { get; set; }
    public PendingPlan? PendingPlan { get; set; }
    public List<ApprovalHistoryRecord> ApprovalHistory { get; set; } = [];
    public string? LastToolName { get; set; }
    public string? LastToolTarget { get; set; }
    public string? LastToolStatus { get; set; }
    public string? Description { get; set; }
    public string? SessionTitle { get; set; }
    public string? LastUserMessage { get; set; }
    public string? LastResponse { get; set; }
    public string? LastThought { get; set; }
    public List<ToolResult> ActiveTools { get; set; } = [];

    public bool NeedsAttention => Phase is SessionPhase.WaitingApproval or SessionPhase.WaitingInput or SessionPhase.Error;
    public bool IsActive => Phase is SessionPhase.Processing or SessionPhase.WaitingApproval or SessionPhase.WaitingInput or SessionPhase.Compacting;
}

public sealed class RawHookEvent
{
    public ulong Sequence { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string SessionId { get; set; } = "";
    public string? Agent { get; set; }
    public string EventName { get; set; } = "";
    public string Raw { get; set; } = "";
}

public sealed class PendingHookRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public PendingRequestType Type { get; set; }
    public string SessionId { get; set; } = "";
    public string AgentName { get; set; } = CoreText.AgentCenter;
    public string Project { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string ToolInput { get; set; } = "";
    public string? Diff { get; set; }
    public List<string> Options { get; set; } = [];
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class HookUserResponse
{
    public string Decision { get; set; } = "allow";
    public string? Reason { get; set; }
    public bool? Always { get; set; }
    public string? Answer { get; set; }
    public string? Mode { get; set; }
    public string? Message { get; set; }
}
