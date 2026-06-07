using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AgentGuard.Core.Localization;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class HookServer : IAsyncDisposable
{
    private const long MaxRawEventFileBytes = 50L * 1024 * 1024;
    private const int MaxRawEventArchiveCount = 3;

    private readonly SessionStore _sessionStore;
    private readonly AuditLogService _auditLog;
    private readonly GuardAnalyzer _analyzer;
    private readonly JsonFileStore _store;
    private readonly AppPaths _paths;
    private readonly ConcurrentDictionary<string, PendingWaiter> _pending = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;
    private long _sequence;

    public bool IsRunning => _serverTask is { IsCompleted: false };

    public event EventHandler? PendingRequestsChanged;
    public event EventHandler<string>? ServerMessage;

    public HookServer(SessionStore sessionStore, AuditLogService auditLog, GuardAnalyzer analyzer, JsonFileStore store, AppPaths paths)
    {
        _sessionStore = sessionStore;
        _auditLog = auditLog;
        _analyzer = analyzer;
        _store = store;
        _paths = paths;
    }

    public IReadOnlyList<PendingHookRequest> PendingRequests =>
        _pending.Values.Select(item => item.Request)
            .OrderByDescending(item => item.RequestedAt)
            .ToList();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _serverTask = Task.Run(() => RunAsync(_serverCts.Token), CancellationToken.None);
        ServerMessage?.Invoke(this, CoreText.HookServerListening(HookInstaller.PipeName));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_serverCts is null)
        {
            return;
        }

        _serverCts.Cancel();
        foreach (var waiter in _pending.Values)
        {
            ClearSessionPending(waiter.Request);
            waiter.Completion.TrySetResult(BuildResponse(waiter.Request, new HookUserResponse
            {
                Decision = "deny",
                Reason = CoreText.AgentGuardStopped,
                Mode = "cancel",
                Message = CoreText.AgentGuardStopped
            }));
        }
        _pending.Clear();
        PendingRequestsChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            if (_serverTask is not null)
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            // Expected when cancellation interrupts WaitForConnectionAsync.
        }

        _serverCts.Dispose();
        _serverCts = null;
        _serverTask = null;
        ServerMessage?.Invoke(this, CoreText.HookServerStopped);
    }

    public Task<bool> RespondAsync(string requestId, HookUserResponse response)
    {
        if (!_pending.TryRemove(requestId, out var waiter))
        {
            return Task.FromResult(false);
        }

        ClearSessionPending(waiter.Request);

        waiter.Completion.TrySetResult(BuildResponse(waiter.Request, response));
        PendingRequestsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    HookInstaller.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(pipe, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ServerMessage?.Invoke(this, CoreText.HookServerError(ex.Message));
                await Task.Delay(1000, cancellationToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using var connectedPipe = pipe;
        using var reader = new StreamReader(connectedPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(connectedPipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested && connectedPipe.IsConnected)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                var response = await ProcessRawLineAsync(line, cancellationToken);
                if (response is not null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonFileStore.CompactOptions));
                }
            }
            catch (Exception ex)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { error = ex.Message }, JsonFileStore.CompactOptions));
                ServerMessage?.Invoke(this, CoreText.FailedToProcessHookEvent(ex.Message));
            }
        }
    }

    private async Task<object?> ProcessRawLineAsync(string line, CancellationToken cancellationToken)
    {
        var payload = HookPayload.Parse(line);
        var sequence = unchecked((ulong)Interlocked.Increment(ref _sequence));
        var session = _sessionStore.ApplyEvent(payload);
        var sessionId = session?.Id ?? payload.SessionId;
        await StoreRawEventAsync(payload, line, sequence, sessionId, cancellationToken);

        if (HookAuditMapper.ToAuditRecord(payload, sequence) is { } record)
        {
            _auditLog.Ingest(record);
        }

        _analyzer.RecordObservedCommand(ExtractCommand(payload), payload.Agent);

        return payload.EventName switch
        {
            "PermissionRequest" or "permission_request" => await WaitForResponseAsync(CreatePermissionRequest(payload, sessionId), cancellationToken),
            "AskQuestion" or "ask_question" => await WaitForResponseAsync(CreateQuestionRequest(payload, sessionId), cancellationToken),
            "PlanApproval" or "plan_approval" => await WaitForResponseAsync(CreatePlanRequest(payload, sessionId), cancellationToken),
            _ => new { ok = true }
        };
    }

    private async Task StoreRawEventAsync(HookPayload payload, string raw, ulong sequence, string sessionId, CancellationToken cancellationToken)
    {
        var rawEvent = new RawHookEvent
        {
            Sequence = sequence,
            Timestamp = DateTimeOffset.Now,
            SessionId = sessionId,
            Agent = payload.Agent,
            EventName = payload.EventName,
            Raw = raw
        };
        await _store.AppendLineWithRotationAsync(
            _paths.RawEventsPath,
            JsonSerializer.Serialize(rawEvent, JsonFileStore.CompactOptions),
            MaxRawEventFileBytes,
            MaxRawEventArchiveCount,
            cancellationToken);
    }

    private async Task<object> WaitForResponseAsync(PendingHookRequest request, CancellationToken cancellationToken)
    {
        var waiter = new PendingWaiter(request);
        _pending[request.Id] = waiter;
        PendingRequestsChanged?.Invoke(this, EventArgs.Empty);
        ServerMessage?.Invoke(this, CoreText.WaitingForResponse(request.Type, request.Title));

        using var registration = cancellationToken.Register(() =>
            waiter.Completion.TrySetResult(BuildResponse(request, new HookUserResponse
            {
                Decision = "deny",
                Reason = CoreText.AgentGuardStopped,
                Mode = "cancel",
                Message = CoreText.AgentGuardStopped
            })));

        return await waiter.Completion.Task;
    }

    private void ClearSessionPending(PendingHookRequest request)
    {
        switch (request.Type)
        {
            case PendingRequestType.Permission:
                _sessionStore.SetPendingPermission(request.SessionId, null);
                break;
            case PendingRequestType.Question:
                _sessionStore.SetPendingQuestion(request.SessionId, null);
                break;
            case PendingRequestType.Plan:
                _sessionStore.SetPendingPlan(request.SessionId, null);
                break;
        }
    }

    private static PendingHookRequest CreatePermissionRequest(HookPayload payload, string sessionId) =>
        new()
        {
            Type = PendingRequestType.Permission,
            SessionId = sessionId,
            AgentName = payload.Agent,
            Project = payload.Project,
            Cwd = payload.Cwd,
            ToolName = payload.ToolName,
            Title = payload.ToolName,
            Detail = string.IsNullOrWhiteSpace(payload.Diff) ? payload.ToolInput : payload.Diff,
            ToolInput = payload.ToolInput,
            Diff = payload.NullableString(["diff"]),
            Options = payload.Options
        };

    private static PendingHookRequest CreateQuestionRequest(HookPayload payload, string sessionId) =>
        new()
        {
            Type = PendingRequestType.Question,
            SessionId = sessionId,
            AgentName = payload.Agent,
            Project = payload.Project,
            Cwd = payload.Cwd,
            ToolName = CoreText.Question,
            Title = string.IsNullOrWhiteSpace(payload.Header) ? CoreText.Question : payload.Header,
            Detail = payload.Question,
            Options = payload.Options
        };

    private static PendingHookRequest CreatePlanRequest(HookPayload payload, string sessionId) =>
        new()
        {
            Type = PendingRequestType.Plan,
            SessionId = sessionId,
            AgentName = payload.Agent,
            Project = payload.Project,
            Cwd = payload.Cwd,
            ToolName = CoreText.Plan,
            Title = payload.PlanTitle,
            Detail = payload.PlanContent,
            Options = payload.RequestedPermissions
        };

    private static object BuildResponse(PendingHookRequest request, HookUserResponse response)
    {
        return request.Type switch
        {
            PendingRequestType.Question => new
            {
                answer = response.Answer ?? response.Message ?? ""
            },
            PendingRequestType.Plan => new
            {
                mode = string.IsNullOrWhiteSpace(response.Mode) ? "accept" : response.Mode,
                message = response.Message
            },
            _ => PermissionResponse(response)
        };
    }

    private static object PermissionResponse(HookUserResponse response)
    {
        var decision = response.Decision.Equals("deny", StringComparison.OrdinalIgnoreCase) ? "deny" : "allow";
        return new
        {
            decision,
            reason = response.Reason,
            always = response.Always ?? false,
            hookSpecificOutput = new
            {
                hookEventName = "PermissionRequest",
                decision = new
                {
                    behavior = decision,
                    message = decision == "deny" ? response.Reason : null,
                    updatedPermissions = (object?)null
                }
            },
            permissionDecision = decision,
            permissionDecisionReason = response.Reason
        };
    }

    private static string ExtractCommand(HookPayload payload)
    {
        var direct = payload.String(["command"]);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var toolName = payload.ToolName.ToLowerInvariant();
        if (toolName.Contains("shell", StringComparison.Ordinal) ||
            toolName.Contains("bash", StringComparison.Ordinal) ||
            toolName.Contains("exec", StringComparison.Ordinal) ||
            toolName.Contains("command", StringComparison.Ordinal))
        {
            return payload.ToolInput;
        }

        return "";
    }

    private sealed class PendingWaiter
    {
        public PendingWaiter(PendingHookRequest request)
        {
            Request = request;
        }

        public PendingHookRequest Request { get; }
        public TaskCompletionSource<object> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
