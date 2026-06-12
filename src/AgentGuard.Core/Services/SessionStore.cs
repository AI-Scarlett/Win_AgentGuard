using System.Collections.Concurrent;
using System.Text.Json;
using AgentGuard.Core.Localization;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonFileStore _store;
    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public event EventHandler? Changed;

    public SessionStore(JsonFileStore store, AppPaths paths)
    {
        _store = store;
        _paths = paths;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _store.ReadAsync<List<SessionState>>(_paths.SessionsPath, cancellationToken) ?? [];
        lock (_gate)
        {
            _sessions.Clear();
            foreach (var session in sessions)
            {
                _sessions[session.Id] = session;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot();
        return _store.WriteAsync(_paths.SessionsPath, snapshot, cancellationToken);
    }

    public List<SessionState> Snapshot()
    {
        lock (_gate)
        {
            return _sessions.Values.OrderByDescending(item => item.StartedAt).ToList();
        }
    }

    public List<SessionState> ActiveSnapshot()
    {
        lock (_gate)
        {
            return _sessions.Values.Where(item => item.IsActive || item.NeedsAttention)
                .OrderByDescending(item => item.StartedAt)
                .ToList();
        }
    }

    public SessionState SeedPendingSession(HookPayload payload)
    {
        SessionState session;
        lock (_gate)
        {
            session = SeedPendingSessionNoNotify(payload);
        }

        Notify();
        return session;
    }

    private SessionState SeedPendingSessionNoNotify(HookPayload payload)
    {
        var sessionId = string.IsNullOrWhiteSpace(payload.SessionId)
            ? $"session-{Guid.NewGuid():N}"
            : payload.SessionId;

        var session = _sessions.GetOrAdd(sessionId, _ => new SessionState
        {
            Id = sessionId,
            AgentType = payload.Agent,
            Project = payload.Project,
            Cwd = payload.Cwd,
            Terminal = payload.Terminal,
            StartedAt = DateTimeOffset.Now
        });

        session.AgentType = string.IsNullOrWhiteSpace(payload.Agent) ? session.AgentType : payload.Agent;
        if (!string.IsNullOrWhiteSpace(payload.Project)) session.Project = payload.Project;
        if (!string.IsNullOrWhiteSpace(payload.Cwd)) session.Cwd = payload.Cwd;
        if (!string.IsNullOrWhiteSpace(payload.Terminal)) session.Terminal = payload.Terminal;
        return session;
    }

    public SessionState? ApplyEvent(HookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.SessionId) && !IsApprovalEvent(payload.EventName))
        {
            return null;
        }

        SessionState session;
        lock (_gate)
        {
            session = SeedPendingSessionNoNotify(payload);
            var eventName = payload.EventName;

            switch (eventName)
            {
                case "SessionStart":
                case "session_start":
                    session.Phase = SessionPhase.Ready;
                    break;
                case "SessionEnd":
                case "session_end":
                case "Stop":
                    session.Phase = SessionPhase.Done;
                    session.EndedAt = DateTimeOffset.Now;
                    break;
                case "PreToolUse":
                case "pre_tool_use":
                case "ShellExecutionStart":
                case "shell_execution_start":
                case "MCPExecutionStart":
                case "mcp_execution_start":
                    session.LastToolName = payload.ToolName;
                    session.LastToolTarget = HookAuditMapper.AuditTarget(payload);
                    session.LastToolStatus = "running";
                    session.Phase = SessionPhase.Processing;
                    session.ActiveTools.Insert(0, new ToolResult
                    {
                        ToolName = payload.ToolName,
                        Status = "running",
                        ToolInput = payload.ToolInput
                    });
                    Trim(session.ActiveTools, 50);
                    break;
                case "PostToolUse":
                case "post_tool_use":
                case "ShellExecutionEnd":
                case "shell_execution_end":
                case "MCPExecutionEnd":
                case "mcp_execution_end":
                    session.LastToolName = payload.ToolName;
                    session.LastToolTarget = HookAuditMapper.AuditTarget(payload);
                    session.LastToolStatus = string.IsNullOrWhiteSpace(payload.Status) ? "completed" : payload.Status;
                    if (session.Phase == SessionPhase.Processing)
                    {
                        session.Phase = SessionPhase.Idle;
                    }
                    break;
                case "PermissionRequest":
                case "permission_request":
                    SetPendingPermissionNoNotify(session.Id, new PendingPermission
                    {
                        ToolName = payload.ToolName,
                        ToolInput = payload.ToolInput,
                        Diff = payload.NullableString(["diff"]),
                        Options = payload.Options,
                        Source = payload.NullableString(["source"]),
                        SourceLabel = payload.NullableString(["source_label", "sourceLabel"])
                    });
                    break;
                case "AskQuestion":
                case "ask_question":
                    SetPendingQuestionNoNotify(session.Id, new PendingQuestion
                    {
                        Question = payload.Question,
                        Options = payload.Options,
                        Descriptions = payload.Descriptions,
                        Header = payload.NullableString(["header", "title"]),
                        MultiSelect = payload.MultiSelect,
                        ResponseMode = payload.NullableString(["response_mode", "responseMode"])
                    });
                    break;
                case "PlanApproval":
                case "plan_approval":
                    SetPendingPlanNoNotify(session.Id, new PendingPlan
                    {
                        Title = payload.PlanTitle,
                        Content = payload.PlanContent,
                        Permissions = payload.RequestedPermissions
                    });
                    break;
                case "TokenUsage":
                case "token_usage":
                    session.Tokens.Input += ParseUnsigned(payload.String(["input", "input_tokens", "inputTokens"]));
                    session.Tokens.Output += ParseUnsigned(payload.String(["output", "output_tokens", "outputTokens"]));
                    session.Tokens.CacheRead += ParseUnsigned(payload.String(["cache_read", "cacheRead"]));
                    session.Tokens.CacheCreate += ParseUnsigned(payload.String(["cache_create", "cacheCreate"]));
                    break;
                case "RateLimitsUpdate":
                case "rate_limits_update":
                case "StatusLineUpdate":
                case "status_line_update":
                    session.RateLimits = BuildRateLimitInfo(payload);
                    session.StatusLineText = payload.String(["status_line_text", "statusLineText", "message"]);
                    break;
                case "Notification":
                case "notification":
                    session.Description = payload.String(["message", "notification"]);
                    break;
            }
        }

        Notify();
        return session;
    }

    public void SetPendingPermission(string sessionId, PendingPermission? permission)
    {
        lock (_gate)
        {
            SetPendingPermissionNoNotify(sessionId, permission);
        }

        Notify();
    }

    public void SetPendingQuestion(string sessionId, PendingQuestion? question)
    {
        lock (_gate)
        {
            SetPendingQuestionNoNotify(sessionId, question);
        }

        Notify();
    }

    public void SetPendingPlan(string sessionId, PendingPlan? plan)
    {
        lock (_gate)
        {
            SetPendingPlanNoNotify(sessionId, plan);
        }

        Notify();
    }

    private void SetPendingPermissionNoNotify(string sessionId, PendingPermission? permission)
    {
        var session = EnsureSession(sessionId);
        session.PendingPermission = permission;
        if (permission is null)
        {
            ResolveLatestHistory(session, "permission");
            if (session.Phase == SessionPhase.WaitingApproval) session.Phase = SessionPhase.Idle;
        }
        else
        {
            session.Phase = SessionPhase.WaitingApproval;
            AddHistory(session, "permission", permission.ToolName, permission.Diff ?? permission.ToolInput);
        }
    }

    private void SetPendingQuestionNoNotify(string sessionId, PendingQuestion? question)
    {
        var session = EnsureSession(sessionId);
        session.PendingQuestion = question;
        if (question is null)
        {
            ResolveLatestHistory(session, "question");
            if (session.Phase == SessionPhase.WaitingInput) session.Phase = SessionPhase.Idle;
        }
        else
        {
            session.Phase = SessionPhase.WaitingInput;
            AddHistory(session, "question", question.Header ?? question.Question, question.Question);
        }
    }

    private void SetPendingPlanNoNotify(string sessionId, PendingPlan? plan)
    {
        var session = EnsureSession(sessionId);
        session.PendingPlan = plan;
        if (plan is null)
        {
            ResolveLatestHistory(session, "plan");
            if (session.Phase == SessionPhase.WaitingApproval) session.Phase = SessionPhase.Idle;
        }
        else
        {
            session.Phase = SessionPhase.WaitingApproval;
            AddHistory(session, "plan", plan.Title, plan.Content);
        }
    }

    private SessionState EnsureSession(string sessionId)
    {
        var id = string.IsNullOrWhiteSpace(sessionId) ? $"session-{Guid.NewGuid():N}" : sessionId;
        return _sessions.GetOrAdd(id, _ => new SessionState
        {
            Id = id,
            AgentType = CoreText.AgentCenter,
            Project = CoreText.ApprovalRequest,
            StartedAt = DateTimeOffset.Now
        });
    }

    private static void AddHistory(SessionState session, string kind, string title, string detail)
    {
        if (session.ApprovalHistory.Any(item =>
                item.IsPending &&
                item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                item.Title.Equals(title, StringComparison.OrdinalIgnoreCase) &&
                item.Detail.Equals(detail, StringComparison.Ordinal)))
        {
            return;
        }

        session.ApprovalHistory.Insert(0, new ApprovalHistoryRecord
        {
            Kind = kind,
            Title = title,
            Detail = detail,
            RequestedAt = DateTimeOffset.Now
        });
        Trim(session.ApprovalHistory, 100);
    }

    private static void ResolveLatestHistory(SessionState session, string kind)
    {
        var history = session.ApprovalHistory.FirstOrDefault(item =>
            item.IsPending && item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
        if (history is not null)
        {
            history.ResolvedAt = DateTimeOffset.Now;
        }
    }

    private void Notify()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _ = Task.Run(() => SaveAsync());
    }

    private static void Trim<T>(List<T> list, int max)
    {
        if (list.Count > max)
        {
            list.RemoveRange(max, list.Count - max);
        }
    }

    private static ulong ParseUnsigned(string value) =>
        ulong.TryParse(value, out var parsed) ? parsed : 0;

    private static double ParseDouble(string value) =>
        double.TryParse(value, out var parsed) ? parsed : 0;

    private static RateLimitInfo BuildRateLimitInfo(HookPayload payload)
    {
        var info = new RateLimitInfo
        {
            FiveHourUsage = ParseDouble(payload.String(["five_hour_usage", "fiveHourUsage"])),
            FiveHourRemaining = payload.String(["five_hour_remaining", "fiveHourRemaining"]),
            SevenDayUsage = ParseDouble(payload.String(["seven_day_usage", "sevenDayUsage"])),
            SevenDayRemaining = payload.String(["seven_day_remaining", "sevenDayRemaining"]),
            Provider = payload.String(["provider"]),
            Source = payload.Agent,
            UpdatedAt = DateTimeOffset.Now
        };

        var raw = payload.String(["rateLimits", "rate_limits"]);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return info;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (TryObject(root, "primary", out var primary) || TryObject(root, "fiveHour", out primary) || TryObject(root, "five_hour", out primary))
            {
                info.FiveHourUsage = ReadDouble(primary, "used_percent", "usedPercent");
                info.FiveHourRemaining = ReadString(primary, "remaining", "remaining_label", "remainingLabel");
            }

            if (TryObject(root, "secondary", out var secondary) || TryObject(root, "sevenDay", out secondary) || TryObject(root, "seven_day", out secondary))
            {
                info.SevenDayUsage = ReadDouble(secondary, "used_percent", "usedPercent");
                info.SevenDayRemaining = ReadString(secondary, "remaining", "remaining_label", "remainingLabel");
            }

            var provider = ReadString(root, "limit_id", "provider", "limit_name");
            if (!string.IsNullOrWhiteSpace(provider))
            {
                info.Provider = provider;
            }
        }
        catch
        {
            // Keep top-level fields if a vendor sends a partial/non-JSON value.
        }

        return info;
    }

    private static bool TryObject(JsonElement element, string key, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(key, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }

    private static double ReadDouble(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static string ReadString(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        }
        return "";
    }

    private static bool IsApprovalEvent(string eventName) =>
        eventName is "PermissionRequest" or "permission_request" or
            "AskQuestion" or "ask_question" or
            "PlanApproval" or "plan_approval";
}
