using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public static class AgentMonitorOverviewService
{
    private const int MaxRows = 250;

    public static AgentMonitorOverview Build(
        AgentSessionScanResult? scanResult,
        IEnumerable<SessionState> liveSessions,
        IEnumerable<OperationRecord> auditRecords)
    {
        var result = scanResult ?? new AgentSessionScanResult();
        var rows = new Dictionary<string, AgentMonitorOverviewRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in liveSessions)
        {
            var row = new AgentMonitorOverviewRow
            {
                Id = $"live:{session.Id}",
                AgentName = string.IsNullOrWhiteSpace(session.AgentType) ? "AgentGuard" : session.AgentType,
                SessionId = session.Id,
                Project = string.IsNullOrWhiteSpace(session.Project) ? ProjectName(session.Cwd) : session.Project,
                Cwd = session.Cwd,
                Model = session.EngineLabel ?? "",
                Status = session.Phase.ToString(),
                LastActivity = session.EndedAt ?? DateTimeOffset.Now,
                ToolCalls = session.ActiveTools.Count + (string.IsNullOrWhiteSpace(session.LastToolName) ? 0 : 1),
                Commands = session.ActiveTools.Count(t => IsCommandTool(t.ToolName)) +
                    (IsCommandTool(session.LastToolName) ? 1 : 0),
                FileAccesses = session.ActiveTools.Count(t => !IsCommandTool(t.ToolName)) +
                    (!string.IsNullOrWhiteSpace(session.LastToolName) && !IsCommandTool(session.LastToolName) ? 1 : 0),
                TotalTokens = session.Tokens.Input + session.Tokens.Output + session.Tokens.CacheRead + session.Tokens.CacheCreate,
                RateLimitText = FormatRateLimit(session.RateLimits),
                SourceFile = "live hook"
            };
            rows[row.Id] = row;
        }

        foreach (var group in result.Records.GroupBy(HistoryKey))
        {
            var first = group.First();
            var row = Upsert(rows, $"history:{group.Key}");
            row.AgentName = Coalesce(row.AgentName, first.AgentName, "Unknown");
            row.SessionId = Coalesce(row.SessionId, first.SessionId, "history");
            row.Project = Coalesce(row.Project, first.Project, ProjectName(first.TargetPath));
            row.Cwd = Coalesce(row.Cwd, first.Project);
            row.Status = Coalesce(row.Status, "History");
            row.LastActivity = Max(row.LastActivity, group.Max(item => item.Timestamp));
            row.ToolCalls += group.Count();
            row.Commands += group.Count(item => item.Operation == AgentHistoryOperation.Execute);
            row.FileAccesses += group.Count(item => IsFileAccess(item.Operation));
            row.SourceFile = Coalesce(row.SourceFile, first.SourceFile);
        }

        foreach (var tokenGroup in result.TokenUsage.GroupBy(TokenKey))
        {
            var latest = tokenGroup.OrderByDescending(item => item.Timestamp).First();
            var row = Upsert(rows, $"history:{tokenGroup.Key}");
            row.AgentName = Coalesce(row.AgentName, latest.AgentName, "Codex");
            row.SessionId = Coalesce(row.SessionId, latest.SessionId, "tokens");
            row.Project = Coalesce(row.Project, latest.Project, ProjectName(latest.Cwd));
            row.Cwd = Coalesce(row.Cwd, latest.Cwd);
            row.Model = Coalesce(row.Model, latest.Model);
            row.Status = Coalesce(row.Status, "History");
            row.LastActivity = Max(row.LastActivity, latest.Timestamp);
            row.TotalTokens += tokenGroup.Aggregate(0UL, (sum, item) => sum + item.TotalTokens);
            row.ContextUsedPercent = Math.Max(row.ContextUsedPercent, latest.ContextUsedPercent);
            row.RateLimitText = Coalesce(row.RateLimitText, FormatRateLimit(latest));
            row.SourceFile = Coalesce(row.SourceFile, latest.SourceFile);
        }

        foreach (var session in result.Sessions)
        {
            var key = $"history:{SessionKey(session.AgentName, session.Id, session.SourceFile)}";
            var row = Upsert(rows, key);
            row.AgentName = Coalesce(row.AgentName, session.AgentName, "Unknown");
            row.SessionId = Coalesce(row.SessionId, session.Id, "session");
            row.Project = Coalesce(row.Project, session.Project, ProjectName(session.Cwd));
            row.Cwd = Coalesce(row.Cwd, session.Cwd);
            row.Model = Coalesce(row.Model, session.Model);
            row.Status = Coalesce(row.Status, "History");
            row.LastActivity = Max(row.LastActivity, session.LastActivity == default ? session.StartedAt : session.LastActivity);
            row.ToolCalls = Math.Max(row.ToolCalls, session.RecordCount);
            row.SourceFile = Coalesce(row.SourceFile, session.SourceFile);
        }

        var audit = auditRecords.ToList();
        var finalRows = rows.Values
            .Where(row => !string.IsNullOrWhiteSpace(row.AgentName) ||
                          !string.IsNullOrWhiteSpace(row.SessionId) ||
                          row.ToolCalls > 0 ||
                          row.TotalTokens > 0)
            .OrderByDescending(row => row.Status is "Processing" or "WaitingApproval" or "WaitingInput")
            .ThenByDescending(row => row.LastActivity)
            .Take(MaxRows)
            .ToList();

        return new AgentMonitorOverview
        {
            GeneratedAt = DateTimeOffset.Now,
            ActiveSessionCount = liveSessions.Count(item => item.IsActive || item.NeedsAttention),
            HistoricalSessionCount = result.Sessions.Count,
            ToolCallCount = finalRows.Sum(item => item.ToolCalls),
            CommandCount = finalRows.Sum(item => item.Commands) + audit.Count(item => item.OperationType == OperationType.Execute),
            FileAccessCount = finalRows.Sum(item => item.FileAccesses) + audit.Count(item => item.OperationType != OperationType.Execute),
            TotalTokens = finalRows.Aggregate(0UL, (sum, item) => sum + item.TotalTokens),
            AverageContextPercent = AverageContext(finalRows),
            RateLimitSummary = RateSummary(result.TokenUsage, liveSessions),
            Rows = finalRows
        };
    }

    private static AgentMonitorOverviewRow Upsert(Dictionary<string, AgentMonitorOverviewRow> rows, string key)
    {
        if (rows.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new AgentMonitorOverviewRow { Id = key, LastActivity = DateTimeOffset.MinValue };
        rows[key] = created;
        return created;
    }

    private static string HistoryKey(AgentHistoryRecord record) =>
        SessionKey(record.AgentName, record.SessionId, record.SourceFile);

    private static string TokenKey(AgentTokenUsageRecord record) =>
        SessionKey(record.AgentName, record.SessionId, record.SourceFile);

    private static string SessionKey(string agentName, string sessionId, string sourceFile) =>
        $"{agentName}|{sessionId}|{sourceFile}";

    private static bool IsFileAccess(AgentHistoryOperation operation) =>
        operation is AgentHistoryOperation.Read or AgentHistoryOperation.Write or AgentHistoryOperation.Edit or
            AgentHistoryOperation.Delete or AgentHistoryOperation.Move or AgentHistoryOperation.Rename or
            AgentHistoryOperation.Search or AgentHistoryOperation.List;

    private static bool IsCommandTool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        var n = toolName.ToLowerInvariant();
        return n.Contains("bash", StringComparison.Ordinal) ||
               n.Contains("shell", StringComparison.Ordinal) ||
               n.Contains("command", StringComparison.Ordinal) ||
               n.Contains("exec", StringComparison.Ordinal) ||
               n.Contains("powershell", StringComparison.Ordinal) ||
               n.Contains("cmd", StringComparison.Ordinal);
    }

    private static string FormatRateLimit(RateLimitInfo? info)
    {
        if (info is null) return "";
        var primary = info.FiveHourUsage > 0 ? $"5h {info.FiveHourUsage:0.#}%" : "";
        var secondary = info.SevenDayUsage > 0 ? $"7d {info.SevenDayUsage:0.#}%" : "";
        return string.Join(" / ", new[] { primary, secondary }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string FormatRateLimit(AgentTokenUsageRecord record)
    {
        var primary = record.FiveHourUsagePercent > 0 ? $"5h {record.FiveHourUsagePercent:0.#}%" : "";
        var secondary = record.SevenDayUsagePercent > 0 ? $"7d {record.SevenDayUsagePercent:0.#}%" : "";
        return string.Join(" / ", new[] { primary, secondary }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string RateSummary(IReadOnlyList<AgentTokenUsageRecord> tokenUsage, IEnumerable<SessionState> liveSessions)
    {
        var latest = tokenUsage
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault(item => item.FiveHourUsagePercent > 0 || item.SevenDayUsagePercent > 0);
        if (latest is not null)
        {
            return FormatRateLimit(latest);
        }

        return liveSessions
            .Select(session => FormatRateLimit(session.RateLimits))
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "";
    }

    private static double AverageContext(IEnumerable<AgentMonitorOverviewRow> rows)
    {
        var values = rows.Select(item => item.ContextUsedPercent).Where(item => item > 0).ToList();
        return values.Count == 0 ? 0 : values.Average();
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "";

    private static string ProjectName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/');
            var parts = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "" : parts[^1];
        }
        catch
        {
            return "";
        }
    }
}
