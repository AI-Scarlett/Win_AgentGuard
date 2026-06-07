using System.Text.Json;
using System.Text.Json.Nodes;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class HookInstaller
{
    public const string PipeName = "agentguard-hook";
    private const string BlockStart = "# [AGENTGUARD-START]";
    private const string BlockEnd = "# [AGENTGUARD-END]";
    private const string Marker = "agentguard-bridge";
    private readonly AppPaths _paths;
    private readonly JsonFileStore _store;

    public HookInstaller(AppPaths paths, JsonFileStore? store = null)
    {
        _paths = paths;
        _store = store ?? new JsonFileStore();
    }

    public bool CheckHealth(AgentIntegrationProfile profile)
    {
        try
        {
            var configPath = profile.FullConfigurationPath(_paths.UserProfile);
            if (profile.InstallationKind == InstallationKind.PluginDirectory)
            {
                return Directory.Exists(configPath) &&
                       Directory.EnumerateFiles(configPath, "*", SearchOption.AllDirectories)
                           .Any(file => Path.GetFileName(file).Contains(Marker, StringComparison.OrdinalIgnoreCase));
            }

            if (!File.Exists(configPath))
            {
                return false;
            }

            var content = File.ReadAllText(configPath);
            return content.Contains(Marker, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task InstallAsync(AgentIntegrationProfile profile, string? bridgeExecutablePath = null, CancellationToken cancellationToken = default)
    {
        if (!profile.SupportsHookInstall)
        {
            throw new InvalidOperationException($"{profile.DisplayName} does not support hook installation yet.");
        }

        _paths.EnsureCreated();
        var command = await HookCommandAsync(bridgeExecutablePath, cancellationToken);
        var configPath = profile.FullConfigurationPath(_paths.UserProfile);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? _paths.UserProfile);

        switch (profile.InstallationKind)
        {
            case InstallationKind.JsonHooks:
                await InjectJsonHooksAsync(profile, configPath, command, cancellationToken);
                break;
            case InstallationKind.YamlHooks:
                await InjectYamlHooksAsync(profile, configPath, command, cancellationToken);
                break;
            case InstallationKind.PluginDirectory:
                await InstallPluginDirectoryAsync(profile, configPath, command, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"{profile.DisplayName} has unsupported installation kind: {profile.InstallationKind}");
        }
    }

    public async Task RemoveAsync(AgentIntegrationProfile profile, CancellationToken cancellationToken = default)
    {
        var configPath = profile.FullConfigurationPath(_paths.UserProfile);
        if (profile.InstallationKind == InstallationKind.PluginDirectory)
        {
            if (Directory.Exists(configPath))
            {
                foreach (var file in Directory.EnumerateFiles(configPath, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file).Contains(Marker, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                    }
                }
            }

            return;
        }

        if (!File.Exists(configPath))
        {
            return;
        }

        if (profile.InstallationKind == InstallationKind.JsonHooks)
        {
            var text = await File.ReadAllTextAsync(configPath, cancellationToken);
            var root = JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as JsonObject ?? new JsonObject();
            if (root["hooks"] is JsonObject hooks)
            {
                foreach (var property in hooks.ToList())
                {
                    if (property.Value is JsonArray entries)
                    {
                        RemoveAgentGuardEntries(entries);
                        if (entries.Count == 0)
                        {
                            hooks.Remove(property.Key);
                        }
                    }
                }

                if (hooks.Count == 0)
                {
                    root.Remove("hooks");
                }
            }

            await WriteJsonAsync(configPath, root, cancellationToken);
            return;
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        await _store.WriteTextAsync(configPath, StripSentinelBlock(content), cancellationToken);
    }

    private async Task<string> HookCommandAsync(string? bridgeExecutablePath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(bridgeExecutablePath) && File.Exists(bridgeExecutablePath))
        {
            return Quote(bridgeExecutablePath);
        }

        var localBridge = Path.Combine(AppContext.BaseDirectory, "agentguard-bridge.exe");
        if (File.Exists(localBridge))
        {
            return Quote(localBridge);
        }

        var scriptPath = await EnsurePowerShellBridgeAsync(cancellationToken);
        return $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)}";
    }

    private async Task<string> EnsurePowerShellBridgeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.BridgeDirectory);
        var script = $$"""
        # AgentGuard fallback bridge v1
        $ErrorActionPreference = "Stop"
        $payload = [Console]::In.ReadToEnd()
        if ([string]::IsNullOrWhiteSpace($payload)) { exit 0 }
        try {
          $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "{{PipeName}}", [System.IO.Pipes.PipeDirection]::InOut)
          $pipe.Connect(2000)
          $writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
          $writer.AutoFlush = $true
          $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
          $writer.WriteLine($payload.Trim())
          $response = $reader.ReadLine()
          if ($response) { [Console]::Out.WriteLine($response) }
          $reader.Dispose()
          $writer.Dispose()
          $pipe.Dispose()
        }
        catch {
          New-Item -ItemType Directory -Force -Path "{{_paths.LogsDirectory}}" | Out-Null
          "$(Get-Date -Format o) AgentGuard bridge failed: $($_.Exception.Message)" | Add-Content -Path "{{_paths.BridgeLogPath}}"
          exit 1
        }
        """;
        await _store.WriteTextAsync(_paths.BridgeScriptPath, script, cancellationToken);
        return _paths.BridgeScriptPath;
    }

    private async Task InjectJsonHooksAsync(AgentIntegrationProfile profile, string configPath, string command, CancellationToken cancellationToken)
    {
        var root = new JsonObject();
        if (File.Exists(configPath))
        {
            var text = await File.ReadAllTextAsync(configPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                root = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
        }

        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;

        foreach (var hookEvent in profile.Events)
        {
            var entries = hooks[hookEvent.Name] as JsonArray ?? new JsonArray();
            RemoveAgentGuardEntries(entries);
            entries.Add(CreateJsonHookEntry(profile, hookEvent, command));
            hooks[hookEvent.Name] = entries;
        }

        await WriteJsonAsync(configPath, root, cancellationToken);
    }

    private static JsonObject CreateJsonHookEntry(AgentIntegrationProfile profile, HookEventDescriptor hookEvent, string command)
    {
        if (profile.UsesNestedCommandHooks || !string.IsNullOrWhiteSpace(hookEvent.Matcher))
        {
            var commandHook = new JsonObject
            {
                ["type"] = "command",
                ["command"] = command
            };
            if (hookEvent.TimeoutSeconds is { } timeout)
            {
                commandHook["timeout"] = timeout;
            }

            return new JsonObject
            {
                ["matcher"] = hookEvent.Matcher ?? "*",
                ["hooks"] = new JsonArray(commandHook)
            };
        }

        var entry = new JsonObject
        {
            ["command"] = command
        };
        if (profile.UsesTypedCommandHook)
        {
            entry["type"] = "command";
        }

        if (hookEvent.TimeoutSeconds is { } flatTimeout)
        {
            entry["timeout"] = flatTimeout;
        }

        return entry;
    }

    private async Task InjectYamlHooksAsync(AgentIntegrationProfile profile, string configPath, string command, CancellationToken cancellationToken)
    {
        var content = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : "";
        content = StripSentinelBlock(content).TrimEnd();

        var lines = new List<string> { BlockStart, "hooks:" };
        foreach (var hookEvent in profile.Events)
        {
            lines.Add($"  {hookEvent.Name}:");
            lines.Add($"    - command: \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
        }
        lines.Add(BlockEnd);

        var result = string.IsNullOrWhiteSpace(content)
            ? string.Join(Environment.NewLine, lines) + Environment.NewLine
            : content + Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine;

        await _store.WriteTextAsync(configPath, result, cancellationToken);
    }

    private async Task InstallPluginDirectoryAsync(AgentIntegrationProfile profile, string configPath, string command, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(configPath);
        var manifest = new
        {
            name = "agentguard",
            displayName = "AgentGuard",
            command,
            events = profile.Events.Select(item => item.Name).ToArray()
        };
        var path = Path.Combine(configPath, "agentguard-bridge.json");
        await _store.WriteTextAsync(path, JsonSerializer.Serialize(manifest, JsonFileStore.Options), cancellationToken);
    }

    private static void RemoveAgentGuardEntries(JsonArray entries)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (NodeContainsMarker(entries[index]))
            {
                entries.RemoveAt(index);
            }
        }
    }

    private static bool NodeContainsMarker(JsonNode? node)
    {
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text.Contains(Marker, StringComparison.OrdinalIgnoreCase);
        }

        if (node is JsonObject obj)
        {
            return obj.Any(pair => NodeContainsMarker(pair.Value));
        }

        if (node is JsonArray array)
        {
            return array.Any(NodeContainsMarker);
        }

        return false;
    }

    private static string StripSentinelBlock(string content)
    {
        var result = new List<string>();
        var inside = false;
        foreach (var line in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed == BlockStart)
            {
                inside = true;
                continue;
            }

            if (trimmed == BlockEnd)
            {
                inside = false;
                continue;
            }

            if (!inside)
            {
                result.Add(line);
            }
        }

        return string.Join(Environment.NewLine, result).TrimEnd() + Environment.NewLine;
    }

    private Task WriteJsonAsync(string configPath, JsonObject root, CancellationToken cancellationToken)
    {
        return _store.WriteTextAsync(configPath, root.ToJsonString(JsonFileStore.Options), cancellationToken);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
