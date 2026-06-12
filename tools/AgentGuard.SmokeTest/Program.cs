using System.Text.Json;
using AgentGuard.Core.Models;
using AgentGuard.Core.Services;

if (args.Contains("--install-hooks", StringComparer.OrdinalIgnoreCase))
{
    var agent = ArgValue(args, "--install-hooks") ?? "all";
    var bridge = ArgValue(args, "--bridge");
    var paths = new AppPaths();
    var installer = new HookInstaller(paths);
    var registry = new AgentRegistryService(paths, installer);

    if (agent.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        await registry.InstallAllAvailableHooksAsync(bridge);
        Console.WriteLine("Installed hooks for available agents.");
    }
    else
    {
        await registry.InstallHooksAsync(agent, bridge);
        Console.WriteLine($"Installed hooks for {agent}.");
    }

    return 0;
}

var tempRoot = Path.Combine(Path.GetTempPath(), "AgentGuardSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var paths = new AppPaths(tempRoot, tempRoot);
    paths.EnsureCreated();
    var store = new JsonFileStore();
    var analyzer = new GuardAnalyzer(store, paths);
    await analyzer.LoadAsync();

    // ===== Core round-trip =====
    var payload = HookPayload.Parse("""
    {"event":"PermissionRequest","session_id":"smoke","agent":"Codex","cwd":"C:\\Work\\Demo","tool":"Write","file_path":"C:\\Work\\Demo\\app.cs","tool_input":"demo write","diff":"demo change","options":["allow","deny"]}
    """);

    if (payload.EventName != "PermissionRequest" || payload.SessionId != "smoke" || payload.ToolName != "Write")
    {
        throw new InvalidOperationException("Hook payload parsing failed.");
    }

    var record = HookAuditMapper.ToAuditRecord(payload, 1)
        ?? throw new InvalidOperationException("Audit mapping failed.");
    if (!record.TargetPath.EndsWith("app.cs", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Audit target extraction failed.");
    }

    var commandResult = analyzer.RecordObservedCommand("rm -rf C:\\Work\\Demo", "Codex");
    if (!commandResult.IsBlocked)
    {
        throw new InvalidOperationException("Command blacklist check failed.");
    }

    var sessionStore = new SessionStore(store, paths);
    var missingSessionPayload = HookPayload.Parse("""
    {"event":"PermissionRequest","agent":"Codex","cwd":"C:\\Work\\Demo","tool":"Write","tool_input":"demo"}
    """);
    var seededSession = sessionStore.ApplyEvent(missingSessionPayload)
        ?? throw new InvalidOperationException("Missing session approval did not seed a session.");
    if (string.IsNullOrWhiteSpace(seededSession.Id) || seededSession.PendingPermission is null)
    {
        throw new InvalidOperationException("Missing session approval state was not normalized.");
    }

    sessionStore.SetPendingPermission(seededSession.Id, null);
    if (seededSession.PendingPermission is not null)
    {
        throw new InvalidOperationException("Pending permission cleanup failed.");
    }

    var rawEventsPath = Path.Combine(tempRoot, "raw-events.jsonl");
    await store.AppendLineWithRotationAsync(rawEventsPath, "first", maxBytes: 20, maxArchiveCount: 2);
    await store.AppendLineWithRotationAsync(rawEventsPath, new string('x', 30), maxBytes: 20, maxArchiveCount: 2);
    if (!File.Exists(rawEventsPath + ".1"))
    {
        throw new InvalidOperationException("Raw event rotation failed.");
    }

    var compact = JsonSerializer.Serialize(new { ok = true }, JsonFileStore.CompactOptions);
    if (compact.Contains('\n') || compact.Contains('\r'))
    {
        throw new InvalidOperationException("Hook response JSON must stay single-line.");
    }

    var settingsPath = Path.Combine(tempRoot, "theme-settings.json");
    await store.WriteAsync(settingsPath, new AgentSettings
    {
        ColorPalette = "aurora",
        AppearanceMode = "dark"
    });
    var themeSettings = await store.ReadAsync<AgentSettings>(settingsPath);
    if (themeSettings?.ColorPalette != "aurora" || themeSettings.AppearanceMode != "dark")
    {
        throw new InvalidOperationException("Theme settings persistence failed.");
    }

    var yamlPath = Path.Combine(tempRoot, "agent.yaml");
    await store.WriteTextAsync(yamlPath, """
    keep: true
    # [AGENTGUARD-START]
    hooks:
      PermissionRequest:
        - command: "agentguard-bridge"
    # [AGENTGUARD-END]
    after: true
    """);
    var installer = new HookInstaller(paths, store);
    await installer.RemoveAsync(new AgentIntegrationProfile
    {
        Id = "yaml-smoke",
        DisplayName = "YAML Smoke",
        InstallationKind = InstallationKind.YamlHooks,
        ConfigurationPath = "agent.yaml"
    });
    var yaml = await File.ReadAllTextAsync(yamlPath);
    if (yaml.Contains("[AGENTGUARD-START]", StringComparison.Ordinal) ||
        !yaml.Contains("keep: true", StringComparison.Ordinal) ||
        !yaml.Contains("after: true", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("YAML sentinel removal failed.");
    }

    await analyzer.SaveAsync();

    // ===== Per-agent parser smoke tests =====
    await RunParserSmokeTestsAsync(tempRoot);

    Console.WriteLine("AgentGuard smoke checks passed.");
    return 0;
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Ignore temp cleanup failures.
    }
}

static async Task RunParserSmokeTestsAsync(string tempRoot)
{
    var profile = Path.Combine(tempRoot, "user");
    Directory.CreateDirectory(profile);

    // Each parser gets its own isolated fake user profile via a wrapper.
    await ClaudeSmoke(tempRoot, profile);
    await CodexSmoke(tempRoot, profile);
    await HermesSmoke(tempRoot, profile);
    await OpenClawSmoke(tempRoot, profile);
    await CursorSmoke(tempRoot, profile);
    await FileChangesSmoke(tempRoot, profile);
}

static async Task ClaudeSmoke(string tempRoot, string profile)
{
    var dir = Path.Combine(profile, ".claude", "projects", "C--Users-smoke", "demo-session");
    Directory.CreateDirectory(dir);
    var jsonl = Path.Combine(dir, "session.jsonl");
    await File.WriteAllTextAsync(jsonl, string.Join("\n", new[]
    {
        """{"type":"session_meta","cwd":"C:\\Users\\smoke\\DemoProject","sessionId":"claude-1","timestamp":"2026-05-10T10:00:00Z"}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"add a hello function"}]},"timestamp":"2026-05-10T10:00:05Z"}""",
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"sure"},{"type":"tool_use","name":"Write","input":{"file_path":"C:\\Work\\app.py","content":"print('hi')"}}]},"timestamp":"2026-05-10T10:00:08Z"}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"thanks"}]},"timestamp":"2026-05-10T10:01:00Z"}""",
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"np"},{"type":"tool_use","name":"Bash","input":{"command":"python app.py"}}]},"timestamp":"2026-05-10T10:01:10Z"}"""
    }));

    var scanner = new AgentSessionScanner(new AppPaths(tempRoot, profile));
    var result = await scanner.ScanAsync();
    var claudeRecords = result.Records.Where(r => r.AgentName == "Claude Code").ToList();
    if (claudeRecords.Count < 2)
    {
        throw new InvalidOperationException($"Claude: expected ≥2 records, got {claudeRecords.Count}. Errors: {string.Join(" | ", result.Errors)}");
    }
    var write = claudeRecords.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Write);
    if (write is null || !write.TargetPath.EndsWith("app.py", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Claude: Write event missing or wrong path. Got: {write?.TargetPath}");
    }
    var exec = claudeRecords.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Execute);
    if (exec is null || !exec.Detail.Contains("python app.py", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Claude: Bash event missing or wrong detail. Got: {exec?.Detail}");
    }

    // history.jsonl
    var history = Path.Combine(profile, ".claude", "history.jsonl");
    Directory.CreateDirectory(Path.GetDirectoryName(history)!);
    await File.WriteAllTextAsync(history,
        """{"timestamp":"2026-05-10T10:00:00Z","project":"C:\\Users\\smoke\\DemoProject","message":"add a hello function"}""" + "\n" +
        """{"timestamp":"2026-05-10T10:00:30Z","project":"C:\\Users\\smoke\\DemoProject","message":"thanks"}""");
    var scanner2 = new AgentSessionScanner(new AppPaths(tempRoot, profile));
    var result2 = await scanner2.ScanAsync();
    var historyRecs = result2.Records.Where(r => r.SessionId == "history").ToList();
    if (historyRecs.Count < 2)
    {
        throw new InvalidOperationException($"Claude history: expected ≥2 records, got {historyRecs.Count}");
    }

    await Task.CompletedTask;
}

static async Task CodexSmoke(string tempRoot, string profile)
{
    var dir = Path.Combine(profile, ".codex", "sessions", "2026", "05", "10");
    Directory.CreateDirectory(dir);
    var jsonl = Path.Combine(dir, "rollout-2026-05-10T10-00-00-uuid.jsonl");
    await File.WriteAllTextAsync(jsonl, string.Join("\n", new[]
    {
        """{"type":"session_meta","timestamp":"2026-05-10T10:00:00Z","payload":{"cwd":"C:\\Users\\smoke\\CodexProj","model":"gpt-5-codex","model_provider":"openai"}}""",
        """{"type":"turn_context","timestamp":"2026-05-10T10:00:05Z","payload":{"cwd":"C:\\Users\\smoke\\CodexProj","model":"gpt-5-codex"}}""",
        """{"type":"event_msg","timestamp":"2026-05-10T10:00:06Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":400,"output_tokens":100,"reasoning_output_tokens":20,"total_tokens":1100},"last_token_usage":{"input_tokens":1000,"cached_input_tokens":400,"output_tokens":100,"reasoning_output_tokens":20,"total_tokens":1100},"model_context_window":100000},"rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300},"secondary":{"used_percent":2.0,"window_minutes":10080},"plan_type":"plus"}}}""",
        """{"type":"event_msg","timestamp":"2026-05-10T10:00:07Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1500,"cached_input_tokens":700,"output_tokens":150,"reasoning_output_tokens":40,"total_tokens":1650},"last_token_usage":{"input_tokens":500,"cached_input_tokens":300,"output_tokens":50,"reasoning_output_tokens":20,"total_tokens":550},"model_context_window":100000},"rate_limits":{"primary":{"used_percent":13.0,"window_minutes":300},"secondary":{"used_percent":2.0,"window_minutes":10080},"plan_type":"plus"}}}""",
        """{"type":"response_item","timestamp":"2026-05-10T10:00:10Z","payload":{"type":"function_call","name":"write_file","arguments":"{\"file_path\":\"C:\\\\Users\\\\smoke\\\\CodexProj\\\\main.go\",\"content\":\"package main\"}","call_id":"call_1"}}""",
        """{"type":"response_item","timestamp":"2026-05-10T10:00:30Z","payload":{"type":"custom_tool_call","name":"shell","arguments":"{\"command\":\"go run main.go\"}","call_id":"call_2"}}""",
        """{"type":"response_item","timestamp":"2026-05-10T10:01:00Z","payload":{"type":"message","role":"assistant","content":[{"type":"text","text":"done"}]}}"""
    }));

    // Codex state_5.sqlite
    var stateDb = Path.Combine(profile, ".codex", "state_5.sqlite");
    var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={stateDb}");
    conn.Open();
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE threads (id TEXT, created_at INTEGER, cwd TEXT, title TEXT, model_provider TEXT)";
        cmd.ExecuteNonQuery();
    }
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO threads VALUES ($id, $ts, $cwd, $title, $mp)";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "$id"; p1.Value = "thread-1"; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "$ts"; p2.Value = 1747070400000L; cmd.Parameters.Add(p2);
        var p3 = cmd.CreateParameter(); p3.ParameterName = "$cwd"; p3.Value = "C:\\Users\\smoke\\CodexProj"; cmd.Parameters.Add(p3);
        var p4 = cmd.CreateParameter(); p4.ParameterName = "$title"; p4.Value = "Refactor auth"; cmd.Parameters.Add(p4);
        var p5 = cmd.CreateParameter(); p5.ParameterName = "$mp"; p5.Value = "openai"; cmd.Parameters.Add(p5);
        cmd.ExecuteNonQuery();
    }
    conn.Close();

    var scanner = new AgentSessionScanner(new AppPaths(tempRoot, profile));
    var result = await scanner.ScanAsync();
    var codexRecs = result.Records.Where(r => r.AgentName == "Codex").ToList();
    if (codexRecs.Count < 3)
    {
        throw new InvalidOperationException($"Codex: expected ≥3 records, got {codexRecs.Count}. Errors: {string.Join(" | ", result.Errors)}");
    }
    var write = codexRecs.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Write);
    if (write is null || !write.TargetPath.EndsWith("main.go", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Codex: write_file event missing. Got: {write?.TargetPath}");
    }
    var shell = codexRecs.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Execute);
    if (shell is null || !shell.Detail.Contains("go run main.go", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Codex: shell event missing. Got: {shell?.Detail}");
    }
    var session = result.Sessions.FirstOrDefault(s => s.AgentName == "Codex" && s.Id == "thread-1");
    if (session is null || !session.Cwd.Contains("CodexProj", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Codex: SQLite session metadata not picked up.");
    }
    var tokenTotal = result.TokenUsage.Where(r => r.AgentName == "Codex").Aggregate(0UL, (sum, item) => sum + item.TotalTokens);
    if (tokenTotal != 1650)
    {
        throw new InvalidOperationException($"Codex: expected token delta total 1650, got {tokenTotal}.");
    }
    var latestToken = result.TokenUsage.OrderByDescending(r => r.Timestamp).FirstOrDefault();
    if (latestToken is null ||
        latestToken.ContextWindow != 100000 ||
        latestToken.ContextUsedPercent <= 0 ||
        Math.Abs(latestToken.FiveHourUsagePercent - 13.0) > 0.01)
    {
        throw new InvalidOperationException("Codex: token context/rate-limit metadata not picked up.");
    }

    await Task.CompletedTask;
}

static async Task HermesSmoke(string tempRoot, string profile)
{
    var dir = Path.Combine(profile, ".hermes", "sessions");
    Directory.CreateDirectory(dir);
    var session = Path.Combine(dir, "session-uuid-1.json");
    await File.WriteAllTextAsync(session, """
    {
      "session_id": "hermes-uuid-1",
      "model": "claude-sonnet-4.6",
      "platform": "telegram",
      "session_start": "2026-05-10T10:00:00Z",
      "cwd": "C:\\Users\\smoke\\HermesProj",
      "messages": [
        {"role": "user", "content": "list files"},
        {"role": "assistant", "content": "ok", "tool_calls": [
          {"function": {"name": "list_directory", "arguments": "{\"directory\":\"C:\\\\Users\\\\smoke\\\\HermesProj\"}"}}
        ]},
        {"role": "assistant", "content": "", "tool_calls": [
          {"function": {"name": "write_file", "arguments": "{\"file_path\":\"C:\\\\Users\\\\smoke\\\\HermesProj\\\\new.py\",\"content\":\"print()\"}"}}
        ]}
      ]
    }
    """);
    // Skip request_dump_*.json
    await File.WriteAllTextAsync(Path.Combine(dir, "request_dump_xyz.json"), "{}");

    // Hermes state.db
    var stateDb = Path.Combine(profile, ".hermes", "state.db");
    var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={stateDb}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE sessions (session_id TEXT, title TEXT, model TEXT, platform TEXT, started_at INTEGER, ended_at INTEGER)";
    cmd.ExecuteNonQuery();
    cmd.CommandText = "INSERT INTO sessions VALUES ($s,$t,$m,$p,$s2,$e)";
    var p1 = cmd.CreateParameter(); p1.ParameterName = "$s"; p1.Value = "hermes-uuid-1"; cmd.Parameters.Add(p1);
    var p2 = cmd.CreateParameter(); p2.ParameterName = "$t"; p2.Value = "Refactor auth"; cmd.Parameters.Add(p2);
    var p3 = cmd.CreateParameter(); p3.ParameterName = "$m"; p3.Value = "claude-sonnet-4.6"; cmd.Parameters.Add(p3);
    var p4 = cmd.CreateParameter(); p4.ParameterName = "$p"; p4.Value = "telegram"; cmd.Parameters.Add(p4);
    var p5 = cmd.CreateParameter(); p5.ParameterName = "$s2"; p5.Value = 1747070400000L; cmd.Parameters.Add(p5);
    var p6 = cmd.CreateParameter(); p6.ParameterName = "$e"; p6.Value = 0L; cmd.Parameters.Add(p6);
    cmd.ExecuteNonQuery();
    conn.Close();

    var scanner = new AgentSessionScanner(new AppPaths(tempRoot, profile));
    var result = await scanner.ScanAsync();
    var hermesRecs = result.Records.Where(r => r.AgentName == "Hermes").ToList();
    if (hermesRecs.Count < 2)
    {
        throw new InvalidOperationException($"Hermes: expected ≥2 records, got {hermesRecs.Count}. Errors: {string.Join(" | ", result.Errors)}");
    }
    var write = hermesRecs.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Write);
    if (write is null || !write.TargetPath.EndsWith("new.py", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Hermes: write_file event missing. Got: {write?.TargetPath}");
    }
    var sessions = result.Sessions.Where(s => s.AgentName == "Hermes").ToList();
    if (sessions.Count < 1)
    {
        throw new InvalidOperationException("Hermes: state.db session not picked up.");
    }
    var summaryRecord = hermesRecs.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Message && r.Detail.Contains("Refactor auth", StringComparison.Ordinal));
    if (summaryRecord is null)
    {
        throw new InvalidOperationException("Hermes: state.db title not surfaced as a record.");
    }

    await Task.CompletedTask;
}

static async Task OpenClawSmoke(string tempRoot, string profile)
{
    var dir = Path.Combine(profile, ".openclaw", "agents", "coder", "sessions");
    Directory.CreateDirectory(dir);
    var jsonl = Path.Combine(dir, "sess-1.jsonl");
    await File.WriteAllTextAsync(jsonl, string.Join("\n", new[]
    {
        """{"timestamp":"2026-05-10T10:00:00Z","session_id":"openclaw-1","content":[{"type":"text","text":"echo hi"}]}""",
        """{"timestamp":"2026-05-10T10:00:10Z","session_id":"openclaw-1","content":[{"type":"tool_use","name":"write_file","input":{"file_path":"C:\\Users\\smoke\\ClawProj\\app.js","content":"x"}}]}""",
        """{"timestamp":"2026-05-10T10:00:30Z","session_id":"openclaw-1","tool_calls":[{"function":{"name":"run_shell","arguments":"{\"command\":\"node app.js\"}"}}]}""",
        """{"timestamp":"2026-05-10T10:01:00Z","session_id":"openclaw-1","content":[{"type":"text","text":"ok"}],"tool_calls":[]}"""
    }));

    // Also test the flat state file
    var stateDir = Path.Combine(profile, ".openclaw", "state");
    Directory.CreateDirectory(stateDir);
    await File.WriteAllTextAsync(Path.Combine(stateDir, "session_auto.jsonl"),
        """{"timestamp":"2026-05-10T11:00:00Z","session_id":"openclaw-auto","content":[{"type":"tool_call","name":"list_dir","input":{"path":"C:\\Users\\smoke\\ClawProj"}}]}""");

    var scanner = new AgentSessionScanner(new AppPaths(tempRoot, profile));
    var result = await scanner.ScanAsync();
    var clawRecs = result.Records.Where(r => r.AgentName == "OpenClaw").ToList();
    if (clawRecs.Count < 3)
    {
        throw new InvalidOperationException($"OpenClaw: expected ≥3 records, got {clawRecs.Count}. Errors: {string.Join(" | ", result.Errors)}");
    }
    var write = clawRecs.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Write);
    if (write is null || !write.TargetPath.EndsWith("app.js", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"OpenClaw: write_file event missing. Got: {write?.TargetPath}");
    }
    var shell = clawRecs.FirstOrDefault(r => r.Operation == AgentHistoryOperation.Execute);
    if (shell is null || !shell.Detail.Contains("node app.js", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"OpenClaw: run_shell event missing. Got: {shell?.Detail}");
    }

    await Task.CompletedTask;
}

static async Task CursorSmoke(string tempRoot, string profile)
{
    // Cursor uses %APPDATA%\Cursor. The scanner accepts a custom APPDATA
    // override via constructor so the smoke test can run on macOS without
    // the env var.
    var appData = Path.Combine(tempRoot, "appdata");
    Directory.CreateDirectory(appData);

    try
    {
        var wsRoot = Path.Combine(appData, "Cursor", "User", "workspaceStorage", "ws-hash-1");
        Directory.CreateDirectory(wsRoot);
        await File.WriteAllTextAsync(Path.Combine(wsRoot, "workspace.json"),
            """{"folder":"file:///C%3A/Users/smoke/CursorProj"}""");
        var dbPath = Path.Combine(wsRoot, "state.vscdb");
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE ItemTable (key TEXT PRIMARY KEY, value TEXT)";
            cmd.ExecuteNonQuery();
        }
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ItemTable VALUES ($k, $v)";
            var p1 = cmd.CreateParameter(); p1.ParameterName = "$k"; p1.Value = "cursorChat.ChatSessionStore.index"; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "$v";
            p2.Value = """{"entries":{"sess-1":{"title":"Refactor auth","lastMessage":"thanks","createdAt":1747070400000,"lastMessageDate":1747070500000},"sess-2":{"title":"Add tests","lastMessage":"ok","createdAt":1747071000000,"lastMessageDate":1747071100000}}}""";
            cmd.Parameters.Add(p2);
            cmd.ExecuteNonQuery();
        }
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ItemTable VALUES ($k, $v)";
            var p3 = cmd.CreateParameter(); p3.ParameterName = "$k"; p3.Value = "ai-chat:sessionRelation:modelMap"; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "$v";
            p4.Value = """{"sess-1":{"name":"claude-sonnet-4.6","provider":"anthropic"}}""";
            cmd.Parameters.Add(p4);
            cmd.ExecuteNonQuery();
        }
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ItemTable VALUES ($k, $v)";
            var p5 = cmd.CreateParameter(); p5.ParameterName = "$k"; p5.Value = "icube-ai-agent-storage-input-history"; cmd.Parameters.Add(p5);
            var p6 = cmd.CreateParameter(); p6.ParameterName = "$v";
            p6.Value = """[{"inputText":"add auth","timestamp":1747070400000},{"inputText":"thanks","timestamp":1747071000000}]""";
            cmd.Parameters.Add(p6);
            cmd.ExecuteNonQuery();
        }
        conn.Close();

        var scanner = new AgentSessionScanner(new AppPaths(tempRoot, profile), appData);
        var result = await scanner.ScanAsync();
        var cursorRecs = result.Records.Where(r => r.AgentName == "Cursor").ToList();
        if (cursorRecs.Count < 2)
        {
            throw new InvalidOperationException($"Cursor: expected ≥2 records, got {cursorRecs.Count}. Errors: {string.Join(" | ", result.Errors)}");
        }
        var sessionSummary = result.Sessions.FirstOrDefault(s => s.AgentName == "Cursor" && s.Id == "sess-1");
        if (sessionSummary is null)
        {
            throw new InvalidOperationException("Cursor: ChatSessionStore sessions not picked up.");
        }
        var historyRec = cursorRecs.FirstOrDefault(r => r.SessionId == "input_history" && r.Detail.Contains("add auth", StringComparison.Ordinal));
        if (historyRec is null)
        {
            throw new InvalidOperationException("Cursor: input history with 'add auth' not extracted.");
        }
        var modelRec = cursorRecs.FirstOrDefault(r => r.Detail.Contains("claude-sonnet-4.6", StringComparison.Ordinal));
        if (modelRec is null)
        {
            throw new InvalidOperationException("Cursor: sessionRelation:modelMap not extracted.");
        }
    }
    finally
    {
    }

    await Task.CompletedTask;
}

static async Task FileChangesSmoke(string tempRoot, string profile)
{
    var appData = Path.Combine(tempRoot, "appdata-fc");
    Directory.CreateDirectory(appData);
    try
    {
        var wsRoot = Path.Combine(appData, "Cursor", "User", "workspaceStorage", "ws-fc-1");
        Directory.CreateDirectory(wsRoot);
        var fc = Path.Combine(wsRoot, "file-changes-2026-05-10.json");
        await File.WriteAllTextAsync(fc, """
        [
          {"path":"C:\\Work\\Foo.cs","type":"created","size":1234,"timestamp":1747070400000},
          {"path":"C:\\Work\\Foo.cs","type":"modified","timestamp":1747071000000},
          {"path":"C:\\Work\\Old.cs","type":"deleted","timestamp":1747072000000}
        ]
        """);

        var scanner = new AgentSessionScanner(new AppPaths(tempRoot, profile), appData);
        var result = await scanner.ScanAsync();
        var fcRecs = result.Records.Where(r => r.SourceFile == fc).ToList();
        if (fcRecs.Count < 3)
        {
            throw new InvalidOperationException($"file-changes: expected ≥3 records, got {fcRecs.Count}. Errors: {string.Join(" | ", result.Errors)}");
        }
        if (!fcRecs.Any(r => r.Operation == AgentHistoryOperation.Write && r.TargetPath.EndsWith("Foo.cs", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("file-changes: created op not detected.");
        }
        if (!fcRecs.Any(r => r.Operation == AgentHistoryOperation.Edit))
        {
            throw new InvalidOperationException("file-changes: modified op not detected.");
        }
        if (!fcRecs.Any(r => r.Operation == AgentHistoryOperation.Delete && r.TargetPath.EndsWith("Old.cs", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("file-changes: deleted op not detected.");
        }
    }
    finally
    {
    }

    await Task.CompletedTask;
}

static string? ArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }
    }

    return null;
}
