using System.Text.Json;
using System.Text.RegularExpressions;
using AgentGuard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AgentGuard.Core.Services;

/// <summary>
/// Scans local AI agent session data files on disk and produces a unified
/// <see cref="AgentSessionScanResult"/> for the Agent History tab.
///
/// Supported agents (Windows paths, user profile = %USERPROFILE%):
///   • Claude Code:  .claude\projects\&lt;encoded&gt;\&lt;session&gt;.jsonl
///                   + history.jsonl
///   • Codex CLI:     .codex\sessions\**\rollout-*.jsonl
///                   + state_5.sqlite (threads) + logs_2.sqlite
///   • Cursor:        %APPDATA%\Cursor\User\workspaceStorage\*\state.vscdb
///                   + globalStorage\state.vscdb + workspace.json
///                   (reused for Trae / CodeBuddy / Windsurf / Roo Code)
///   • OpenClaw:      .openclaw\agents\*\sessions\*.jsonl
///                   + .openclaw\state\session_*.jsonl (recursive)
///   • QClaw / EasyClaw: .qclaw / .easyclaw (same shape as OpenClaw)
///   • Hermes Agent:  .hermes\sessions\*.json
///                   + .hermes\state.db (sessions table)
/// </summary>
public sealed class AgentSessionScanner
{
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;
    private const int MaxRecordsPerFile = 5000;
    private const int MaxFilesPerAgent = 200;
    private const int MaxSqliteRecords = 5000;

    private static readonly HashSet<string> ClaudeFileOpTools = new(StringComparer.Ordinal)
    {
        "Write", "Edit", "MultiEdit", "Read", "Bash", "Glob", "Grep",
        "WebFetch", "WebSearch", "NotebookEdit", "TodoWrite", "Skill",
        "Task", "KillBash", "KillShell", "ListMcpResources"
    };

    private readonly AppPaths _paths;
    private readonly string? _appDataOverride;
    private readonly SessionParsingContext _ctx = new();

    public AgentSessionScanner(AppPaths paths, string? appDataOverride = null)
    {
        _paths = paths;
        _appDataOverride = appDataOverride;
    }

    private string GetAppDataRoot()
    {
        if (!string.IsNullOrEmpty(_appDataOverride)) return _appDataOverride!;
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public Task<AgentSessionScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(cancellationToken), cancellationToken);
    }

    private AgentSessionScanResult Scan(CancellationToken cancellationToken)
    {
        var result = new AgentSessionScanResult();
        var profile = _paths.UserProfile;
        if (string.IsNullOrWhiteSpace(profile) || !Directory.Exists(profile))
        {
            result.Errors.Add("user profile not found");
            return result;
        }

        try { ScanClaudeCode(result, cancellationToken); }
        catch (Exception ex) { result.Errors.Add($"claude scope: {ex.Message}"); }

        try { ScanCodex(result, cancellationToken); }
        catch (Exception ex) { result.Errors.Add($"codex scope: {ex.Message}"); }

        try { ScanHermes(result, cancellationToken); }
        catch (Exception ex) { result.Errors.Add($"hermes scope: {ex.Message}"); }

        try { ScanOpenClawLike(result, cancellationToken, ".openclaw", "OpenClaw", "agents", "state"); }
        catch (Exception ex) { result.Errors.Add($"openclaw scope: {ex.Message}"); }

        try { ScanOpenClawLike(result, cancellationToken, ".qclaw", "QClaw", "agents", "state"); }
        catch (Exception ex) { result.Errors.Add($"qclaw scope: {ex.Message}"); }

        try { ScanOpenClawLike(result, cancellationToken, ".easyclaw", "EasyClaw", "agents", "state"); }
        catch (Exception ex) { result.Errors.Add($"easyclaw scope: {ex.Message}"); }

        try { ScanCursorLike(result, cancellationToken, "Cursor", "Cursor", "User"); ; }
        catch (Exception ex) { result.Errors.Add($"cursor scope: {ex.Message}"); }

        try { ScanCursorLike(result, cancellationToken, "Trae", "Trae", "User"); ; }
        catch (Exception ex) { result.Errors.Add($"trae scope: {ex.Message}"); }

        try { ScanCursorLike(result, cancellationToken, "CodeBuddy", "CodeBuddy", "User"); ; }
        catch (Exception ex) { result.Errors.Add($"codebuddy scope: {ex.Message}"); }

        try { ScanCursorLike(result, cancellationToken, "Windsurf", "Windsurf", "User"); ; }
        catch (Exception ex) { result.Errors.Add($"windsurf scope: {ex.Message}"); }

        try { ScanCursorLike(result, cancellationToken, "Code", "VS Code", "User"); ; }
        catch (Exception ex) { result.Errors.Add($"vscode scope: {ex.Message}"); }

        try { ScanVSCodeFileChanges(result, cancellationToken); }
        catch (Exception ex) { result.Errors.Add($"file-changes scope: {ex.Message}"); }

        // Sort newest first so the UI shows recent activity at the top.
        result.Records = result.Records
            .OrderByDescending(item => item.Timestamp)
            .ToList();
        result.Sessions = result.Sessions
            .OrderByDescending(item => item.LastActivity)
            .ToList();
        return result;
    }

    // ============================================================
    // Claude Code
    // ============================================================
    private void ScanClaudeCode(AgentSessionScanResult result, CancellationToken cancellationToken)
    {
        // ~/.claude/projects/<encoded-path>/*.jsonl
        var projectsRoot = Path.Combine(_paths.UserProfile, ".claude", "projects");
        ScanJsonlAgent(result, cancellationToken, projectsRoot, "Claude Code", "*.jsonl", (root, file, sessionId, res) =>
        {
            // Reset cwd per file; carry sessionId from filename.
            _ctx.CwdByFile[file] = "";
            _ctx.SessionIdByFile[file] = sessionId;
            ParseClaudeLine(root, file, res);
        });

        // ~/.claude/history.jsonl (user prompts only, no tool_use)
        var historyPath = Path.Combine(_paths.UserProfile, ".claude", "history.jsonl");
        if (File.Exists(historyPath))
        {
            try
            {
                result.ScannedFileCount++;
                foreach (var line in ReadLinesSafe(historyPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var message = ReadString(root, "message") ?? ReadString(root, "display") ?? "";
                    var project = ReadString(root, "project") ?? "";
                    var ts = ReadTimestamp(root);
                    if (string.IsNullOrWhiteSpace(message)) continue;
                    result.Records.Add(new AgentHistoryRecord
                    {
                        AgentName = "Claude Code",
                        SessionId = "history",
                        Timestamp = ts,
                        Operation = AgentHistoryOperation.Message,
                        TargetPath = "",
                        Detail = message.Length > 400 ? message[..400] : message,
                        SourceFile = historyPath,
                        Project = project
                    });
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"claude history {historyPath}: {ex.Message}");
            }
        }
    }

    private void ParseClaudeLine(JsonElement root, string file, AgentSessionScanResult result)
    {
        // Reset cwd from each session_meta if present.
        if (ReadString(root, "type") is "session_meta" or null)
        {
            var sessionMetaCwd = ReadString(root, "cwd");
            if (!string.IsNullOrEmpty(sessionMetaCwd)) _ctx.CwdByFile[file] = sessionMetaCwd;
        }

        // Track sessionId and cwd from the line itself if present.
        var sid = ReadString(root, "sessionId");
        if (!string.IsNullOrEmpty(sid)) _ctx.SessionIdByFile[file] = sid;
        var cwdInline = ReadString(root, "cwd");
        if (!string.IsNullOrEmpty(cwdInline)) _ctx.CwdByFile[file] = cwdInline;

        var type = ReadString(root, "type");
        if (string.IsNullOrEmpty(type)) return;
        if (type is not ("assistant" or "user")) return;

        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return;
        if (!message.TryGetProperty("content", out var content)) return;

        var ts = ReadTimestamp(root);
        var sessionId = _ctx.SessionIdByFile.TryGetValue(file, out var cachedSid) ? cachedSid : Path.GetFileNameWithoutExtension(file);
        var cwd = _ctx.CwdByFile.TryGetValue(file, out var cachedCwd) ? cachedCwd : "";
        var project = ExtractProjectName(cwd);

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var blockType = ReadString(block, "type");
                if (blockType == "tool_use")
                {
                    var name = ReadString(block, "name") ?? "";
                    if (!ClaudeFileOpTools.Contains(name)) continue;
                    var input = block.TryGetProperty("input", out var inputVal) ? inputVal : default;
                    AppendRecord(result, "Claude Code", sessionId, file, ts, name, input, cwd, project);
                }
            }
        }
    }

    // ============================================================
    // Codex CLI
    // ============================================================
    private void ScanCodex(AgentSessionScanResult result, CancellationToken cancellationToken)
    {
        // ~/.codex/sessions/**/*.jsonl
        var sessionsRoot = Path.Combine(_paths.UserProfile, ".codex", "sessions");
        ScanJsonlAgent(result, cancellationToken, sessionsRoot, "Codex", "*.jsonl", (root, file, sessionId, res) =>
        {
            _ctx.CwdByFile[file] = "";
            _ctx.SessionIdByFile[file] = sessionId;
            ParseCodexLine(root, file, res);
        });

        // ~/.codex/state_5.sqlite
        var stateDb = Path.Combine(_paths.UserProfile, ".codex", "state_5.sqlite");
        if (File.Exists(stateDb))
        {
            try
            {
                ParseCodexStateDb(stateDb, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"codex state.db: {ex.Message}");
            }
        }

        // ~/.codex/logs_2.sqlite
        var logsDb = Path.Combine(_paths.UserProfile, ".codex", "logs_2.sqlite");
        if (File.Exists(logsDb))
        {
            try
            {
                ParseCodexLogsDb(logsDb, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"codex logs.db: {ex.Message}");
            }
        }
    }

    private void ParseCodexLine(JsonElement root, string file, AgentSessionScanResult result)
    {
        var type = ReadString(root, "type") ?? "";
        var ts = ReadTimestamp(root);

        // session_meta / turn_context: capture cwd.
        if (type is "session_meta" or "turn_context")
        {
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                var payloadCwd = ReadString(payload, "cwd");
                if (!string.IsNullOrEmpty(payloadCwd)) _ctx.CwdByFile[file] = payloadCwd;
                var model = ReadString(payload, "model") ?? ReadString(payload, "model_provider") ?? "";
                if (!string.IsNullOrEmpty(model)) _ctx.ModelByFile[file] = model;
                var metaSessionId = ReadString(payload, "id") ?? ReadString(payload, "session_id") ?? ReadString(payload, "sessionId");
                if (!string.IsNullOrEmpty(metaSessionId)) _ctx.SessionIdByFile[file] = metaSessionId;
            }
            return;
        }

        if (type == "event_msg")
        {
            ParseCodexEventMessage(root, file, result);
            return;
        }

        if (type != "response_item") return;
        if (!root.TryGetProperty("payload", out var payloadNode) || payloadNode.ValueKind != JsonValueKind.Object) return;

        var payloadType = ReadString(payloadNode, "type") ?? "";
        var sessionId = _ctx.SessionIdByFile.TryGetValue(file, out var sid) ? sid : Path.GetFileNameWithoutExtension(file);
        var cwd = _ctx.CwdByFile.TryGetValue(file, out var c) ? c : "";
        var project = ExtractProjectName(cwd);

        if (payloadType is "function_call" or "custom_tool_call")
        {
            var name = ReadString(payloadNode, "name") ?? "codex";
            var argsRaw = ReadString(payloadNode, "arguments") ?? "";
            var input = TryParseJsonElement(argsRaw);
            AppendRecord(result, "Codex", sessionId, file, ts, name, input, cwd, project);
        }
    }

    private void ParseCodexEventMessage(JsonElement root, string file, AgentSessionScanResult result)
    {
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var payloadType = ReadString(payload, "type") ?? "";
        if (!payloadType.Equals("token_count", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetObject(payload, "info", out var info))
        {
            info = payload;
        }

        var totalUsage = TryGetObject(info, "total_token_usage", out var totalNode)
            ? TokenUsageValues.Read(totalNode)
            : TokenUsageValues.Empty;
        var lastUsage = TryGetObject(info, "last_token_usage", out var lastNode)
            ? TokenUsageValues.Read(lastNode)
            : TokenUsageValues.Empty;

        var delta = TokenUsageValues.Empty;
        if (totalUsage.TotalOrSum > 0 &&
            _ctx.LastTotalUsageByFile.TryGetValue(file, out var previous) &&
            totalUsage.TotalOrSum >= previous.TotalOrSum)
        {
            delta = totalUsage.Minus(previous);
        }
        else if (lastUsage.TotalOrSum > 0)
        {
            delta = lastUsage;
        }
        else if (totalUsage.TotalOrSum > 0 && !_ctx.LastTotalUsageByFile.ContainsKey(file))
        {
            delta = totalUsage;
        }

        if (totalUsage.TotalOrSum > 0)
        {
            _ctx.LastTotalUsageByFile[file] = totalUsage;
        }

        if (delta.TotalOrSum == 0)
        {
            return;
        }

        var ts = ReadTimestamp(root);
        var sessionId = _ctx.SessionIdByFile.TryGetValue(file, out var sid) ? sid : Path.GetFileNameWithoutExtension(file);
        var cwd = _ctx.CwdByFile.TryGetValue(file, out var cachedCwd) ? cachedCwd : "";
        var model = _ctx.ModelByFile.TryGetValue(file, out var cachedModel) ? cachedModel : "";
        var contextWindow = ReadUnsigned(info, "model_context_window", "context_window", "contextWindow", "context_window_size", "contextWindowSize");
        var cumulativeTotal = totalUsage.TotalOrSum > 0 ? totalUsage : delta;
        var contextUsed = contextWindow > 0
            ? Math.Round(cumulativeTotal.TotalOrSum * 100.0 / contextWindow, 1)
            : 0;

        var rateLimits = payload.TryGetProperty("rate_limits", out var rateNode) && rateNode.ValueKind == JsonValueKind.Object
            ? rateNode
            : (payload.TryGetProperty("rateLimits", out rateNode) && rateNode.ValueKind == JsonValueKind.Object ? rateNode : default);
        var primary = TryGetObject(rateLimits, "primary", out var primaryNode)
            ? primaryNode
            : (TryGetObject(rateLimits, "fiveHour", out primaryNode) ? primaryNode : default);
        var secondary = TryGetObject(rateLimits, "secondary", out var secondaryNode)
            ? secondaryNode
            : (TryGetObject(rateLimits, "sevenDay", out secondaryNode) ? secondaryNode : default);

        result.TokenUsage.Add(new AgentTokenUsageRecord
        {
            Id = $"codex-token:{sessionId}:{ts.UtcTicks}:{delta.TotalOrSum}:{Guid.NewGuid():N}",
            Timestamp = ts,
            AgentName = "Codex",
            SessionId = sessionId,
            Project = ExtractProjectName(cwd),
            Cwd = cwd,
            Model = model,
            SourceFile = file,
            InputTokens = delta.Input,
            OutputTokens = delta.Output,
            CachedInputTokens = delta.CachedInput,
            CacheCreationTokens = delta.CacheCreation,
            ReasoningTokens = delta.Reasoning,
            TotalTokens = delta.TotalOrSum,
            CumulativeInputTokens = cumulativeTotal.Input,
            CumulativeOutputTokens = cumulativeTotal.Output,
            CumulativeCachedInputTokens = cumulativeTotal.CachedInput,
            CumulativeReasoningTokens = cumulativeTotal.Reasoning,
            CumulativeTotalTokens = cumulativeTotal.TotalOrSum,
            ContextWindow = contextWindow,
            ContextUsedPercent = contextUsed,
            FiveHourUsagePercent = ReadDouble(primary, "used_percent", "usedPercent"),
            FiveHourRemaining = ReadString(primary, "remaining") ?? ReadResetLabel(primary),
            SevenDayUsagePercent = ReadDouble(secondary, "used_percent", "usedPercent"),
            SevenDayRemaining = ReadString(secondary, "remaining") ?? ReadResetLabel(secondary),
            PlanType = ReadString(rateLimits, "plan_type") ?? ReadString(rateLimits, "planType") ?? ""
        });
    }

    private void ParseCodexStateDb(string path, AgentSessionScanResult result)
    {
        result.ScannedFileCount++;
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
        conn.Open();
        HashSet<string> tables;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }
        if (!tables.Contains("threads") && !tables.Contains("sessions"))
        {
            return;
        }
        var tableName = tables.Contains("threads") ? "threads" : "sessions";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT id, created_at, cwd, title, model_provider FROM {tableName} ORDER BY created_at DESC LIMIT {MaxSqliteRecords}";
            using var dataReader = cmd.ExecuteReader();
            while (dataReader.Read())
            {
                var id = SafeString(dataReader, 0);
                var createdAt = SafeLong(dataReader, 1);
                var cwd = SafeString(dataReader, 2);
                var title = SafeString(dataReader, 3);
                var modelProvider = SafeString(dataReader, 4);
                var ts = createdAt > 1_000_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(createdAt)
                    : (createdAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(createdAt) : DateTimeOffset.Now);
                result.Sessions.Add(new AgentSessionSummary
                {
                    Id = id,
                    AgentName = "Codex",
                    SourceFile = path,
                    Cwd = cwd,
                    Project = ExtractProjectName(cwd),
                    Model = modelProvider,
                    StartedAt = ts,
                    LastActivity = ts,
                    RecordCount = 0,
                    TotalSize = SafeFileSize(path)
                });
                if (!string.IsNullOrEmpty(title))
                {
                    result.Records.Add(new AgentHistoryRecord
                    {
                        AgentName = "Codex",
                        SessionId = id,
                        Timestamp = ts,
                        Operation = AgentHistoryOperation.Message,
                        TargetPath = cwd,
                        Detail = title,
                        SourceFile = path,
                        Project = ExtractProjectName(cwd)
                    });
                }
                if (!string.IsNullOrEmpty(modelProvider))
                {
                    result.Records.Add(new AgentHistoryRecord
                    {
                        AgentName = "Codex",
                        SessionId = id,
                        Timestamp = ts,
                        Operation = AgentHistoryOperation.Message,
                        TargetPath = "",
                        Detail = $"model_provider={modelProvider}",
                        SourceFile = path,
                        Project = ExtractProjectName(cwd)
                    });
                }
            }
        }
    }

    private void ParseCodexLogsDb(string path, AgentSessionScanResult result)
    {
        result.ScannedFileCount++;
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
        conn.Open();
        HashSet<string> tables;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }
        var tableName = tables.Contains("logs") ? "logs" :
                         tables.Contains("events") ? "events" : null;
        if (tableName is null) return;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT ts, feedback_log_body, thread_id FROM {tableName} WHERE feedback_log_body LIKE '%codex.op=%' OR feedback_log_body LIKE '%function_call%' OR feedback_log_body LIKE '%tool_call%' ORDER BY ts DESC LIMIT {MaxSqliteRecords}";
            using var dataReader = cmd.ExecuteReader();
            while (dataReader.Read())
            {
                var ts = SafeLong(dataReader, 0);
                var body = SafeString(dataReader, 1);
                var threadId = SafeString(dataReader, 2);
                if (string.IsNullOrEmpty(body)) continue;
                var date = ts > 1_000_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ts)
                    : (ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts) : DateTimeOffset.Now);
                var (name, args) = ExtractCodexToolFromLogBody(body);
                var input = TryParseJsonElement(args);
                result.Records.Add(new AgentHistoryRecord
                {
                    AgentName = "Codex",
                    SessionId = string.IsNullOrEmpty(threadId) ? "log" : threadId,
                    Timestamp = date,
                    Operation = ClassifyTool(name),
                    TargetPath = ExtractPath(name, input),
                    Detail = body.Length > 400 ? body[..400] : body,
                    SourceFile = path
                });
            }
        }
    }

    private static (string name, string args) ExtractCodexToolFromLogBody(string body)
    {
        // Look for patterns like `codex.op=function_call`, `codex.op=tool_call`,
        // or quoted JSON payloads.
        var nameMatch = Regex.Match(body, "codex\\.op=([a-zA-Z0-9_]+)");
        var name = nameMatch.Success ? nameMatch.Groups[1].Value : "codex";
        var argsMatch = Regex.Match(body, "\"arguments\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!argsMatch.Success)
        {
            argsMatch = Regex.Match(body, "\"input\"\\s*:\\s*(\\{.*?\\})\\s*[,}]", RegexOptions.Singleline);
        }
        var args = argsMatch.Success ? argsMatch.Groups[1].Value : "";
        return (name, args);
    }

    // ============================================================
    // OpenClaw / QClaw / EasyClaw
    // ============================================================
    private void ScanOpenClawLike(AgentSessionScanResult result, CancellationToken cancellationToken,
        string folder, string agentName, string agentsSub, string stateSub)
    {
        var root = Path.Combine(_paths.UserProfile, folder);
        if (!Directory.Exists(root)) return;

        // Per-agent session files — recurse fully into agents/<x>/sessions/**/file.jsonl
        var agentsDir = Path.Combine(root, agentsSub);
        if (Directory.Exists(agentsDir))
        {
            foreach (var file in EnumerateSafe(agentsDir, "*.jsonl", MaxFilesPerAgent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseOpenClawFile(file, agentName, result);
            }
        }

        // State directory sessions (flat and nested)
        var stateDir = Path.Combine(root, stateSub);
        if (Directory.Exists(stateDir))
        {
            foreach (var file in EnumerateSafe(stateDir, "*.jsonl", MaxFilesPerAgent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseOpenClawFile(file, agentName, result);
            }
        }
    }

    private void ParseOpenClawFile(string file, string agentName, AgentSessionScanResult result)
    {
        try
        {
            result.ScannedFileCount++;
            if (new FileInfo(file).Length > MaxFileSizeBytes) return;
            var sessionId = Path.GetFileNameWithoutExtension(file);
            _ctx.SessionIdByFile[file] = sessionId;
            _ctx.CwdByFile[file] = "";
            var count = 0;
            using var stream = File.OpenRead(file);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (count++ > MaxRecordsPerFile) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    ParseOpenClawLine(doc.RootElement, file, agentName, result);
                }
                catch
                {
                    // Skip malformed lines.
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{agentName} {file}: {ex.Message}");
        }
    }

    private void ParseOpenClawLine(JsonElement root, string file, string agentName, AgentSessionScanResult result)
    {
        var ts = ReadTimestamp(root);
        var sid = ReadString(root, "session_id") ?? _ctx.SessionIdByFile[file];
        if (!string.IsNullOrEmpty(sid)) _ctx.SessionIdByFile[file] = sid;

        // content array of blocks
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var blockType = ReadString(block, "type") ?? "";
                if (blockType is "tool_use" or "tool_call")
                {
                    var name = ReadString(block, "name") ?? "";
                    if (string.IsNullOrEmpty(name)) continue;
                    var input = block.TryGetProperty("input", out var inputVal) ? inputVal : default;
                    AppendRecord(result, agentName, _ctx.SessionIdByFile[file], file, ts, name, input,
                        _ctx.CwdByFile[file], "");
                }
            }
        }

        // tool_calls array
        if (root.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                if (tc.ValueKind != JsonValueKind.Object) continue;
                if (!tc.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object) continue;
                var name = ReadString(fn, "name") ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                var argsStr = ReadString(fn, "arguments") ?? "";
                var input = TryParseJsonElement(argsStr);
                AppendRecord(result, agentName, _ctx.SessionIdByFile[file], file, ts, name, input,
                    _ctx.CwdByFile[file], "");
            }
        }
    }

    // ============================================================
    // Hermes Agent
    // ============================================================
    private void ScanHermes(AgentSessionScanResult result, CancellationToken cancellationToken)
    {
        var hermesRoot = Path.Combine(_paths.UserProfile, ".hermes");
        if (!Directory.Exists(hermesRoot)) return;

        // ~/.hermes/sessions/*.json (skip request_dump_*)
        var sessionsDir = Path.Combine(hermesRoot, "sessions");
        if (Directory.Exists(sessionsDir))
        {
            foreach (var file in EnumerateSafe(sessionsDir, "*.json", MaxFilesPerAgent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filename = Path.GetFileName(file);
                if (filename.StartsWith("request_dump_", StringComparison.OrdinalIgnoreCase)) continue;
                ParseHermesSessionFile(file, result);
            }
        }

        // ~/.hermes/state.db (or similar) — SQLite sessions table
        foreach (var dbFile in new[] { "state.db", "sessions.db", "hermes.db" })
        {
            var path = Path.Combine(hermesRoot, dbFile);
            if (File.Exists(path))
            {
                try
                {
                    ParseHermesStateDb(path, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"hermes {dbFile}: {ex.Message}");
                }
            }
        }
    }

    private void ParseHermesSessionFile(string file, AgentSessionScanResult result)
    {
        try
        {
            result.ScannedFileCount++;
            if (new FileInfo(file).Length > MaxFileSizeBytes) return;
            using var stream = File.OpenRead(file);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            var sessionId = ReadString(doc.RootElement, "session_id") ?? Path.GetFileNameWithoutExtension(file);
            var model = ReadString(doc.RootElement, "model") ?? "";
            var platform = ReadString(doc.RootElement, "platform") ?? "";
            var sessionStart = ReadString(doc.RootElement, "session_start") ?? ReadString(doc.RootElement, "created_at") ?? "";
            var baseTs = DateTimeOffset.TryParse(sessionStart, out var parsed) ? parsed : DateTimeOffset.Now;
            var cwd = ReadString(doc.RootElement, "cwd") ?? "";

            if (doc.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var msg in messages.EnumerateArray())
                {
                    if (msg.ValueKind != JsonValueKind.Object) continue;
                    var role = ReadString(msg, "role") ?? "";
                    if (role != "assistant") continue;

                    if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            if (tc.ValueKind != JsonValueKind.Object) continue;
                            if (!tc.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object) continue;
                            var name = ReadString(fn, "name") ?? "";
                            if (string.IsNullOrEmpty(name)) continue;
                            var argsStr = ReadString(fn, "arguments") ?? "";
                            var input = TryParseJsonElement(argsStr);
                            AppendRecord(result, "Hermes", sessionId, file, baseTs, name, input, cwd,
                                ExtractProjectName(cwd));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"hermes {file}: {ex.Message}");
        }
    }

    private void ParseHermesStateDb(string path, AgentSessionScanResult result)
    {
        result.ScannedFileCount++;
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
        conn.Open();
        HashSet<string> tables;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }
        if (!tables.Contains("sessions")) return;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT session_id, title, model, platform, started_at, ended_at FROM sessions ORDER BY started_at DESC LIMIT " + MaxSqliteRecords;
            using var dataReader = cmd.ExecuteReader();
            while (dataReader.Read())
            {
                var sid = SafeString(dataReader, 0);
                var title = SafeString(dataReader, 1);
                var model = SafeString(dataReader, 2);
                var platform = SafeString(dataReader, 3);
                var startedAt = SafeLong(dataReader, 4);
                var ts = startedAt > 1_000_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(startedAt)
                    : (startedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(startedAt) : DateTimeOffset.Now);
                result.Sessions.Add(new AgentSessionSummary
                {
                    Id = sid,
                    AgentName = "Hermes",
                    SourceFile = path,
                    StartedAt = ts,
                    LastActivity = ts,
                    RecordCount = 0
                });
                if (!string.IsNullOrEmpty(title))
                {
                    result.Records.Add(new AgentHistoryRecord
                    {
                        AgentName = "Hermes",
                        SessionId = sid,
                        Timestamp = ts,
                        Operation = AgentHistoryOperation.Message,
                        Detail = title + (string.IsNullOrEmpty(model) ? "" : $" [model={model}]"),
                        SourceFile = path
                    });
                }
            }
        }
    }

    // ============================================================
    // Cursor (and Cursor-based agents)
    // ============================================================
    private void ScanCursorLike(AgentSessionScanResult result, CancellationToken cancellationToken,
        string agentFolder, string agentName, string storageRoot)
    {
        // Storage root is different for VSCode-based agents (User/) vs Cursor (User/ but under %APPDATA%).
        var appData = GetAppDataRoot();
        if (string.IsNullOrWhiteSpace(appData)) return;
        var basePath = Path.Combine(appData, agentFolder, storageRoot);
        if (!Directory.Exists(basePath))
        {
            // Don't error: not having Cursor installed is normal.
            return;
        }

        ScanCursorWorkspaceStorageUnder(result, Path.Combine(basePath, "workspaceStorage"), agentName, cancellationToken);
        ScanCursorGlobalStorageUnder(result, Path.Combine(basePath, "globalStorage"), agentName, cancellationToken);
    }

    private void ScanCursorWorkspaceStorageUnder(AgentSessionScanResult result, string workspaceStorage,
        string agentName, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspaceStorage)) return;
        List<string> wsDirs;
        try
        {
            wsDirs = Directory.EnumerateDirectories(workspaceStorage).Take(MaxFilesPerAgent).ToList();
        }
        catch
        {
            wsDirs = [];
        }
        if (wsDirs.Count == 0) return;
        foreach (var wsDir in wsDirs)
        {
            var vscdbPath = Path.Combine(wsDir, "state.vscdb");
            if (File.Exists(vscdbPath))
            {
                try
                {
                    ParseCursorWorkspaceVscdb(vscdbPath, agentName, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{agentName} vscdb {vscdbPath}: {ex.Message}");
                }
            }
        }
    }

    private void ScanCursorGlobalStorageUnder(AgentSessionScanResult result, string globalStorage,
        string agentName, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(globalStorage)) return;
        var globalVscdb = Path.Combine(globalStorage, "state.vscdb");
        if (!File.Exists(globalVscdb)) return;
        try
        {
            ParseCursorGlobalVscdb(globalVscdb, agentName, result);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{agentName} global vscdb: {ex.Message}");
        }
    }

    private void ParseCursorWorkspaceVscdb(string path, string agentName, AgentSessionScanResult result)
    {
        result.ScannedFileCount++;
        var workspaceFolder = ReadCursorWorkspaceFolder(Path.GetDirectoryName(path) ?? "");

        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
        conn.Open();

        // ItemTable holds most cursor data
        try
        {
            if (TableExists(conn, "ItemTable"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM ItemTable";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = SafeString(reader, 0);
                    var value = SafeString(reader, 1);
                    if (string.IsNullOrEmpty(value)) continue;
                    ProcessCursorItemTableKey(agentName, key, value, workspaceFolder, result);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{agentName} ItemTable {path}: {ex.Message}");
        }
    }

    private void ParseCursorGlobalVscdb(string path, string agentName, AgentSessionScanResult result)
    {
        result.ScannedFileCount++;
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
        conn.Open();
        if (TableExists(conn, "cursorDiskKV"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM cursorDiskKV WHERE key LIKE 'bubbleId:%' LIMIT " + MaxSqliteRecords;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = SafeString(reader, 0);
                var value = SafeString(reader, 1);
                if (string.IsNullOrEmpty(value)) continue;
                if (!TryParseJson(value, out var bubble)) continue;
                if (bubble.ValueKind != JsonValueKind.Object) continue;
                var type = ReadString(bubble, "type") ?? "";
                if (type != "1" && type != "2") continue;
                var text = ReadString(bubble, "text") ?? "";
                var createdAt = ReadTimestamp(bubble);
                var isAgentic = bubble.TryGetProperty("isAgentic", out var ia) && ia.ValueKind == JsonValueKind.True;
                // key: bubbleId:<composerId>:<bubbleId>
                var parts = key.Split(':');
                var composerId = parts.Length > 1 ? parts[1] : "global";
                result.Records.Add(new AgentHistoryRecord
                {
                    AgentName = agentName,
                    SessionId = composerId,
                    Timestamp = createdAt,
                    Operation = type == "1" ? AgentHistoryOperation.Message : AgentHistoryOperation.Unknown,
                    Detail = text.Length > 400 ? text[..400] : text,
                    SourceFile = path
                });
                if (bubble.TryGetProperty("toolResults", out var toolResults) &&
                    toolResults.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tr in toolResults.EnumerateArray())
                    {
                        if (tr.ValueKind != JsonValueKind.Object) continue;
                        var toolName = ReadString(tr, "name") ?? ReadString(tr, "toolName") ?? "tool";
                        var input = tr.TryGetProperty("args", out var a) ? a :
                                    tr.TryGetProperty("input", out var i) ? i : default;
                        var detail = tr.GetRawText();
                        AppendRecord(result, agentName, composerId, path, createdAt, toolName, input, "", "", false, detail);
                    }
                }
            }
        }
    }

    private void ProcessCursorItemTableKey(string agentName, string key, string value,
        string workspaceFolder, AgentSessionScanResult result)
    {
        // Composer session index
        if (key.EndsWith("ChatSessionStore.index", StringComparison.Ordinal) || key.EndsWith("ChatStore", StringComparison.Ordinal))
        {
            if (!TryParseJson(value, out var json) || json.ValueKind != JsonValueKind.Object) return;
            if (json.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in entries.EnumerateObject())
                {
                    var sessionId = prop.Name;
                    var entry = prop.Value;
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    var title = ReadString(entry, "title") ?? "";
                    var lastMessage = ReadString(entry, "lastMessage") ?? "";
                    var ts = ReadTimestamp(entry);
                    result.Sessions.Add(new AgentSessionSummary
                    {
                        Id = sessionId,
                        AgentName = agentName,
                        SourceFile = workspaceFolder,
                        Cwd = workspaceFolder,
                        Project = ExtractProjectName(workspaceFolder),
                        StartedAt = ts,
                        LastActivity = ts
                    });
                    if (!string.IsNullOrEmpty(title))
                    {
                        result.Records.Add(new AgentHistoryRecord
                        {
                            AgentName = agentName,
                            SessionId = sessionId,
                            Timestamp = ts,
                            Operation = AgentHistoryOperation.Message,
                            TargetPath = workspaceFolder,
                            Detail = title.Length > 400 ? title[..400] : title,
                            SourceFile = workspaceFolder,
                            Project = ExtractProjectName(workspaceFolder)
                        });
                    }
                }
            }
        }

        // Per-session input history
        if (key.Contains("icube-ai-agent-storage-input-history") && !key.Contains("query"))
        {
            if (!TryParseJson(value, out var json) || json.ValueKind != JsonValueKind.Array) return;
            foreach (var item in json.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var inputText = ReadString(item, "inputText") ?? "";
                if (string.IsNullOrEmpty(inputText)) continue;
                var ts = ReadTimestamp(item);
                result.Records.Add(new AgentHistoryRecord
                {
                    AgentName = agentName,
                    SessionId = "input_history",
                    Timestamp = ts,
                    Operation = AgentHistoryOperation.Message,
                    TargetPath = workspaceFolder,
                    Detail = inputText.Length > 400 ? inputText[..400] : inputText,
                    SourceFile = workspaceFolder,
                    Project = ExtractProjectName(workspaceFolder)
                });
            }
        }

        // Session model map
        if (key.Contains("sessionRelation:modelMap"))
        {
            if (!TryParseJson(value, out var json) || json.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in json.EnumerateObject())
            {
                var detail = prop.Value.ValueKind == JsonValueKind.Object
                    ? prop.Value.GetRawText()
                    : prop.Value.ToString();
                result.Records.Add(new AgentHistoryRecord
                {
                    AgentName = agentName,
                    SessionId = prop.Name,
                    Timestamp = DateTimeOffset.Now,
                    Operation = AgentHistoryOperation.Message,
                    TargetPath = workspaceFolder,
                    Detail = $"model={detail}",
                    SourceFile = workspaceFolder,
                    Project = ExtractProjectName(workspaceFolder)
                });
            }
        }

        // Agent-type map (Cursor's icube extension)
        if (key == "icube_session_agent_map")
        {
            if (!TryParseJson(value, out var json) || json.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in json.EnumerateObject())
            {
                result.Records.Add(new AgentHistoryRecord
                {
                    AgentName = agentName,
                    SessionId = prop.Name,
                    Timestamp = DateTimeOffset.Now,
                    Operation = AgentHistoryOperation.Message,
                    TargetPath = workspaceFolder,
                    Detail = $"agent_type={prop.Value}",
                    SourceFile = workspaceFolder,
                    Project = ExtractProjectName(workspaceFolder)
                });
            }
        }
    }

    private void ScanVSCodeFileChanges(AgentSessionScanResult result, CancellationToken cancellationToken)
    {
        var appData = GetAppDataRoot();
        if (string.IsNullOrWhiteSpace(appData)) return;
        var roots = new[] { "Code", "Cursor", "Trae", "CodeBuddy", "Windsurf" };
        foreach (var folder in roots)
        {
            var root = Path.Combine(appData, folder, "User", "workspaceStorage");
            if (!Directory.Exists(root)) continue;
            var agentName = folder;
            foreach (var file in EnumerateSafe(root, "file-changes-*.json", MaxFilesPerAgent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseVSCodeFileChanges(file, agentName, result);
            }
        }
    }

    private void ParseVSCodeFileChanges(string file, string agentName, AgentSessionScanResult result)
    {
        try
        {
            result.ScannedFileCount++;
            if (new FileInfo(file).Length > MaxFileSizeBytes) return;
            using var stream = File.OpenRead(file);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            var sessionId = Path.GetFileNameWithoutExtension(file);
            foreach (var change in doc.RootElement.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object) continue;
                var path = ReadString(change, "path") ?? ReadString(change, "file") ?? ReadString(change, "uri") ?? "";
                var type = (ReadString(change, "type") ?? "modify").ToLowerInvariant();
                var op = type switch
                {
                    "create" or "created" or "new" => AgentHistoryOperation.Write,
                    "delete" or "deleted" or "remove" or "removed" => AgentHistoryOperation.Delete,
                    "rename" or "renamed" or "move" or "moved" => AgentHistoryOperation.Rename,
                    _ => AgentHistoryOperation.Edit
                };
                result.Records.Add(new AgentHistoryRecord
                {
                    AgentName = agentName,
                    SessionId = sessionId,
                    Timestamp = ReadTimestamp(change),
                    Operation = op,
                    TargetPath = path,
                    Detail = Truncate(change.GetRawText(), 400),
                    SourceFile = file,
                    FileSize = ReadLong(change, "size")
                });
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{agentName} file-changes {file}: {ex.Message}");
        }
    }

    // ============================================================
    // Helpers
    // ============================================================
    private void ScanJsonlAgent(AgentSessionScanResult result, CancellationToken cancellationToken,
        string root, string agentName, string pattern, Action<JsonElement, string, string, AgentSessionScanResult> parser)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in EnumerateSafe(root, pattern, MaxFilesPerAgent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = Path.GetFileNameWithoutExtension(file);
            try
            {
                result.ScannedFileCount++;
                if (new FileInfo(file).Length > MaxFileSizeBytes) continue;
                var count = 0;
                using var stream = File.OpenRead(file);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (count++ > MaxRecordsPerFile) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        parser(doc.RootElement, file, sessionId, result);
                    }
                    catch
                    {
                        // Skip malformed lines.
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{agentName} {file}: {ex.Message}");
            }
        }
    }

    private void AppendRecord(AgentSessionScanResult result, string agentName, string sessionId,
        string sourceFile, DateTimeOffset ts, string toolName, JsonElement input,
        string cwd, string project, bool overwriteProject = true, string? detailOverride = null)
    {
        var op = ClassifyTool(toolName);
        var targetPath = ExtractPath(toolName, input);
        var detail = detailOverride;
        if (detail is null)
        {
            if (input.ValueKind == JsonValueKind.Object)
            {
                if (toolName.Equals("Bash", StringComparison.OrdinalIgnoreCase) ||
                    toolName.Equals("Shell", StringComparison.OrdinalIgnoreCase) ||
                    toolName.Equals("Command", StringComparison.OrdinalIgnoreCase) ||
                    toolName.Equals("run_command", StringComparison.OrdinalIgnoreCase) ||
                    toolName.Equals("exec", StringComparison.OrdinalIgnoreCase))
                {
                    var cmd = ReadString(input, "command");
                    if (!string.IsNullOrEmpty(cmd)) detail = cmd;
                }
                if (string.IsNullOrEmpty(detail))
                {
                    detail = Truncate(input.GetRawText(), 400);
                }
            }
            else
            {
                detail = string.Empty;
            }
        }
        if (string.IsNullOrEmpty(detail)) detail = string.Empty;

        var resolvedProject = !string.IsNullOrEmpty(project) || !overwriteProject
            ? project
            : ExtractProjectName(cwd);
        result.Records.Add(new AgentHistoryRecord
        {
            AgentName = agentName,
            SessionId = sessionId,
            Timestamp = ts,
            Operation = op,
            TargetPath = targetPath,
            Detail = detail,
            SourceFile = sourceFile,
            Project = resolvedProject
        });
    }

    private static IEnumerable<string> EnumerateSafe(string root, string pattern, int max)
    {
        var count = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }
        foreach (var file in files)
        {
            if (count++ >= max) yield break;
            yield return file;
        }
    }

    private static IEnumerable<string> ReadLinesSafe(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            yield return line;
        }
    }

    private static string DetectAgentFromFile(string file)
    {
        if (file.Contains(".claude", StringComparison.OrdinalIgnoreCase)) return "Claude Code";
        if (file.Contains(".codex", StringComparison.OrdinalIgnoreCase)) return "Codex";
        if (file.Contains(".hermes", StringComparison.OrdinalIgnoreCase)) return "Hermes";
        if (file.Contains(".openclaw", StringComparison.OrdinalIgnoreCase)) return "OpenClaw";
        if (file.Contains(".qclaw", StringComparison.OrdinalIgnoreCase)) return "QClaw";
        if (file.Contains(".easyclaw", StringComparison.OrdinalIgnoreCase)) return "EasyClaw";
        if (file.Contains(".cursor", StringComparison.OrdinalIgnoreCase)) return "Cursor";
        if (file.Contains(".trae", StringComparison.OrdinalIgnoreCase)) return "Trae";
        if (file.Contains(".codebuddy", StringComparison.OrdinalIgnoreCase)) return "CodeBuddy";
        if (file.Contains(".windsurf", StringComparison.OrdinalIgnoreCase)) return "Windsurf";
        return "Unknown";
    }

    private static AgentHistoryOperation ClassifyTool(string name)
    {
        var n = name.ToLowerInvariant();
        return n switch
        {
            "read" or "cat" or "view" or "file_read" or "fileread" or "open_file" or "read_file" or "notebookread" => AgentHistoryOperation.Read,
            "write" or "create" or "create_file" or "file_write" or "filewrite" or "save_file" or "notebookedit" or "write_file" => AgentHistoryOperation.Write,
            "edit" or "edit_file" or "file_edit" or "fileedit" or "str_replace" or "str_replace_based_edit_tool" or "multiedit" or "patch" or "apply_patch" or "modify" => AgentHistoryOperation.Edit,
            "delete" or "remove" or "delete_file" => AgentHistoryOperation.Delete,
            "bash" or "shell" or "command" or "run_command" or "exec" or "execute_command" or "run_shell" or "killbash" or "killshell" => AgentHistoryOperation.Execute,
            "search" or "grep" or "find" or "glob" or "rg" => AgentHistoryOperation.Search,
            "webfetch" or "fetch" or "web_search" or "search_web" or "browser" or "websearch" => AgentHistoryOperation.Fetch,
            "list" or "list_dir" or "list_directory" or "ls" or "listmcpresources" => AgentHistoryOperation.List,
            "plan" or "create_plan" or "todowrite" or "task" or "skill" => AgentHistoryOperation.Plan,
            _ => AgentHistoryOperation.Unknown
        };
    }

    private static string ExtractPath(string toolName, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return string.Empty;
        var candidates = toolName.ToLowerInvariant() switch
        {
            "bash" or "shell" or "command" or "run_command" or "exec" or "execute_command" or "run_shell" => new[] { "file_path", "path", "cwd", "command" },
            "grep" or "glob" or "search" => new[] { "pattern", "file_path", "path", "directory" },
            _ => new[] { "file_path", "filepath", "path", "file", "notebook_path" }
        };
        foreach (var key in candidates)
        {
            if (input.TryGetProperty(key, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var s = value.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        return string.Empty;
    }

    private static string ExtractProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return string.Empty;
        try
        {
            var trimmed = cwd!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/');
            var parts = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[^1];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTimeOffset ReadTimestamp(JsonElement element)
    {
        foreach (var key in new[] { "timestamp", "ts", "time", "created_at", "createdAt", "started_at", "date" })
        {
            if (element.TryGetProperty(key, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var s = value.GetString();
                    if (DateTimeOffset.TryParse(s, out var parsed)) return parsed;
                }
                else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
                {
                    return n > 1_000_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds(n)
                        : DateTimeOffset.FromUnixTimeSeconds(n);
                }
            }
        }
        return DateTimeOffset.Now;
    }

    private static string? ReadString(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString();
        }
        return null;
    }

    private static long ReadLong(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (element.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        }
        return 0;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private static bool TryParseJson(string text, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            element = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonElement TryParseJsonElement(string text)
    {
        return TryParseJson(text, out var element) ? element : default;
    }

    private static string ReadCursorWorkspaceFolder(string workspaceDir)
    {
        var workspaceJson = Path.Combine(workspaceDir, "workspace.json");
        if (!File.Exists(workspaceJson)) return workspaceDir;
        try
        {
            using var stream = File.OpenRead(workspaceJson);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("folder", out var folder))
            {
                var raw = folder.GetString() ?? "";
                var cleaned = raw.Replace("file:///", "").Replace("file://", "");
                try
                {
                    cleaned = Uri.UnescapeDataString(cleaned);
                }
                catch
                {
                    // best effort
                }
                if (Path.DirectorySeparatorChar == '\\' && cleaned.StartsWith('/'))
                {
                    cleaned = cleaned.TrimStart('/');
                }
                return cleaned;
            }
        }
        catch
        {
            // ignore
        }
        return workspaceDir;
    }

    private static bool TableExists(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = name;
        cmd.Parameters.Add(p);
        var result = cmd.ExecuteScalar();
        return result != null;
    }

    private static string SafeString(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return string.Empty;
        return reader.GetValue(ordinal)?.ToString() ?? string.Empty;
    }

    private static long SafeLong(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return 0;
        try
        {
            return reader.GetInt64(ordinal);
        }
        catch
        {
            if (long.TryParse(reader.GetValue(ordinal)?.ToString(), out var n)) return n;
            return 0;
        }
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private sealed class SessionParsingContext
    {
        public Dictionary<string, string> CwdByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SessionIdByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ModelByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TokenUsageValues> LastTotalUsageByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct TokenUsageValues(
        ulong Input,
        ulong Output,
        ulong CachedInput,
        ulong CacheCreation,
        ulong Reasoning,
        ulong Total)
    {
        public static TokenUsageValues Empty => new(0, 0, 0, 0, 0, 0);

        public ulong TotalOrSum => Total > 0
            ? Total
            : Input + Output + CacheCreation + Reasoning;

        public TokenUsageValues Minus(TokenUsageValues previous) =>
            new(
                Subtract(Input, previous.Input),
                Subtract(Output, previous.Output),
                Subtract(CachedInput, previous.CachedInput),
                Subtract(CacheCreation, previous.CacheCreation),
                Subtract(Reasoning, previous.Reasoning),
                Subtract(Total, previous.Total));

        public static TokenUsageValues Read(JsonElement element) =>
            new(
                ReadUnsigned(element, "input_tokens", "inputTokens", "prompt_tokens", "promptTokens"),
                ReadUnsigned(element, "output_tokens", "outputTokens", "completion_tokens", "completionTokens"),
                ReadUnsigned(element, "cached_input_tokens", "cachedInputTokens", "cache_read", "cacheRead", "cache_read_tokens", "cacheReadTokens"),
                ReadUnsigned(element, "cache_creation_input_tokens", "cacheCreationInputTokens", "cache_create", "cacheCreate", "cache_write_tokens", "cacheWriteTokens"),
                ReadUnsigned(element, "reasoning_output_tokens", "reasoningOutputTokens", "reasoning_tokens", "reasoningTokens"),
                ReadUnsigned(element, "total_tokens", "totalTokens"));

        private static ulong Subtract(ulong value, ulong previous) => value >= previous ? value - previous : value;
    }

    private static bool TryGetObject(JsonElement element, string key, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(key, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }

    private static ulong ReadUnsigned(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static double ReadDouble(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static string ReadResetLabel(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        var resetSeconds = ReadUnsigned(element, "resets_at", "resetsAt");
        if (resetSeconds == 0) return "";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)resetSeconds).LocalDateTime.ToString("g");
        }
        catch
        {
            return "";
        }
    }
}
