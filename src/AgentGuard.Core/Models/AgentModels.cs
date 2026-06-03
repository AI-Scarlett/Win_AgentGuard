namespace AgentGuard.Core.Models;

public enum AdapterStatus
{
    Active,
    Installed,
    Unavailable,
    Error
}

public enum InstallationKind
{
    JsonHooks,
    YamlHooks,
    CursorSettings,
    PluginDirectory,
    Unknown
}

public sealed class HookEventDescriptor
{
    public string Name { get; set; } = "";
    public string? Matcher { get; set; }
    public int? TimeoutSeconds { get; set; }

    public static HookEventDescriptor Plain(string name) => new() { Name = name };

    public static HookEventDescriptor MatcherEvent(string name, string matcher, int? timeoutSeconds = null) =>
        new() { Name = name, Matcher = matcher, TimeoutSeconds = timeoutSeconds };

    public static HookEventDescriptor Timed(string name, int timeoutSeconds) =>
        new() { Name = name, TimeoutSeconds = timeoutSeconds };
}

public sealed class AgentIntegrationProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Executable { get; set; } = "";
    public InstallationKind InstallationKind { get; set; }
    public string ConfigurationPath { get; set; } = "";
    public bool UsesNestedCommandHooks { get; set; }
    public bool UsesTypedCommandHook { get; set; }
    public bool SupportsHookInstall { get; set; } = true;
    public List<HookEventDescriptor> Events { get; set; } = [];

    public string FullConfigurationPath(string userProfile) =>
        Path.Combine(userProfile, ConfigurationPath.Replace('/', Path.DirectorySeparatorChar));
}

public sealed class AgentAdapterState
{
    public string AgentId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Executable { get; set; } = "";
    public string ConfigurationPath { get; set; } = "";
    public AdapterStatus Status { get; set; }
    public bool SupportsHookInstall { get; set; }
    public string StatusText => Status.ToString();
}
