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
