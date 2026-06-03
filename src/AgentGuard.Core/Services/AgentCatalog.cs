using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public static class AgentCatalog
{
    public static readonly IReadOnlyList<(string DisplayName, string[] Keywords)> AgentKeywords =
    [
        ("Trae", ["trae", "trae-cn", "traecn"]),
        ("Claude", ["claude", "claude code", "claude-code", "anthropic"]),
        ("Cursor", ["cursor", "cursor-agent"]),
        ("Windsurf", ["windsurf", "codeium"]),
        ("CodeBuddy", ["codebuddy", "codebuddy-cn", "codebuddycn"]),
        ("Doubao", ["doubao"]),
        ("Kimi", ["kimi"]),
        ("DeepSeek", ["deepseek"]),
        ("ChatGPT", ["chatgpt"]),
        ("Gemini", ["gemini"]),
        ("Copilot", ["copilot", "github-copilot"]),
        ("Cline", ["cline"]),
        ("OpenClaw", ["openclaw", "open-claw"]),
        ("QClaw", ["qclaw", "q-claw"]),
        ("Hermes", ["hermes"]),
        ("Codex", ["codex"]),
        ("Augment", ["augment"]),
        ("CodeArts", ["codearts", "huawei"]),
        ("Continue", ["continue"]),
        ("Aider", ["aider"]),
        ("Roo Code", ["roo code", "roo-code", "roocode"]),
        ("Tabby", ["tabby"]),
        ("Cody", ["cody"]),
        ("OpenHands", ["openhands", "open-hands"])
    ];

    public static IReadOnlyList<AgentIntegrationProfile> Profiles { get; } =
    [
        JsonNested("claude-code", "Claude Code", "claude", ".claude/settings.json",
            [Plain("UserPromptSubmit"), Match("PreToolUse", "*"), Match("PostToolUse", "*"),
             Match("PermissionRequest", "*", 21600), Match("Notification", "*"), Plain("Stop"),
             Plain("SubagentStop"), Plain("SessionStart"), Plain("SessionEnd"), Plain("PreCompact")]),

        JsonNested("codex", "Codex", "codex", ".codex/hooks.json",
            [Timed("SessionStart", 5), Timed("UserPromptSubmit", 5), Timed("PreToolUse", 5),
             Timed("PostToolUse", 5), Timed("PermissionRequest", 21600), Timed("Stop", 5)]),

        JsonCommand("gemini", "Gemini CLI", "gemini", ".gemini/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Stop")]),

        new()
        {
            Id = "cursor",
            DisplayName = "Cursor",
            Executable = "cursor",
            InstallationKind = InstallationKind.CursorSettings,
            ConfigurationPath = ".cursor/settings.json",
            SupportsHookInstall = false
        },

        JsonTyped("cursor-cli", "Cursor CLI", "cursor-agent", ".cursor/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonCommand("copilot", "GitHub Copilot", "github-copilot-cli", ".config/github-copilot/hooks.json",
            [Plain("pre_tool_use"), Plain("post_tool_use"), Plain("session_start"), Plain("session_end")]),

        Yaml("trae", "Trae", "trae", ".trae/config.yaml",
            [Plain("pre_tool_use"), Plain("post_tool_use"), Plain("session_start")]),

        Yaml("traecli", "Trae CLI", "trae-cli", ".trae/traecli.yaml",
            [Plain("UserPromptSubmit"), Plain("PreToolUse"), Plain("PostToolUse"),
             Plain("PermissionRequest"), Plain("Stop"), Plain("SessionStart"), Plain("SessionEnd")]),

        Yaml("traecn", "Trae CN", "trae-cn", ".trae-cn/config.yaml",
            [Plain("pre_tool_use"), Plain("post_tool_use"), Plain("session_start")]),

        JsonNested("qoder", "Qoder", "qoder", ".qoder/settings.json",
            [Plain("UserPromptSubmit"), Match("PreToolUse", "*"), Match("PostToolUse", "*"),
             Match("PostToolUseFailure", "*"), Match("PermissionRequest", "*"), Match("Notification", "*"),
             Plain("Stop"), Plain("SessionStart"), Plain("SessionEnd"), Plain("PreCompact"),
             Plain("SubagentStart"), Plain("SubagentStop")]),

        JsonTyped("qoder-cli", "Qoder CLI", "qoder-cli", ".qoder/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonTyped("codebuddy", "CodeBuddy", "codebuddy", ".codebuddy/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("SessionStart"), Plain("SessionEnd")]),

        JsonCommand("codebuddycn", "CodeBuddy CN", "codebuddy-cn", ".codebuddycn/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonCommand("qwen", "Qwen", "qwen", ".qwen/settings.json",
            [Plain("pre_tool_use"), Plain("post_tool_use"), Plain("session_start"), Plain("session_end")]),

        JsonTyped("kimi", "Kimi", "kimi", ".kimi/settings.json",
            [Plain("UserPromptSubmit"), Match("PreToolUse", "*"), Match("PostToolUse", "*"),
             Match("Notification", "*"), Plain("Stop"), Plain("SessionStart"), Plain("SessionEnd"),
             Match("PermissionRequest", "*", 86400)]),

        JsonTyped("deepseek", "DeepSeek", "deepseek", ".deepseek/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonTyped("opencode", "OpenCode", "opencode", ".opencode/settings.json",
            [Plain("SessionStart"), Plain("SessionEnd"), Plain("UserPromptSubmit"),
             Plain("PreToolUse"), Plain("PostToolUse"), Plain("PermissionRequest"), Plain("Stop")]),

        JsonTyped("droid", "Factory / Droid", "droid", ".factory/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonTyped("stepfun", "StepFun", "stepfun", ".stepfun/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonTyped("antigravity", "AntiGravity", "antigravity", ".antigravity/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonTyped("workbuddy", "WorkBuddy", "workbuddy", ".workbuddy/hooks.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        Plugin("hermes", "Hermes", "hermes", ".hermes/plugins/agentguard",
            [Plain("SessionStart"), Plain("SessionEnd"), Plain("UserPromptSubmit"),
             Plain("PreToolUse"), Plain("PostToolUse"), Plain("PermissionRequest"), Plain("Stop")]),

        JsonTyped("kiro", "Kiro", "kiro", ".kiro/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("Notification"), Plain("Stop")]),

        JsonTyped("openclaw", "OpenClaw", "openclaw", ".openclaw/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("PermissionRequest"), Plain("Stop")]),

        JsonTyped("qclaw", "QClaw", "qclaw", ".qclaw/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("PermissionRequest"), Plain("Stop")]),

        JsonTyped("easyclaw", "EasyClaw", "easyclaw", ".easyclaw/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("PermissionRequest"), Plain("Stop")]),

        JsonTyped("autoclaw", "AutoClaw", "autoclaw", ".autoclaw/settings.json",
            [Plain("PreToolUse"), Plain("PostToolUse"), Plain("PermissionRequest"), Plain("Stop")])
    ];

    public static string ResolveAgentName(string processName, string arguments = "")
    {
        var combined = $"{processName} {arguments}".ToLowerInvariant();
        foreach (var (displayName, keywords) in AgentKeywords)
        {
            if (keywords.Any(combined.Contains))
            {
                return displayName;
            }
        }

        return "";
    }

    private static HookEventDescriptor Plain(string name) => HookEventDescriptor.Plain(name);
    private static HookEventDescriptor Timed(string name, int timeoutSeconds) => HookEventDescriptor.Timed(name, timeoutSeconds);
    private static HookEventDescriptor Match(string name, string matcher, int? timeoutSeconds = null) => HookEventDescriptor.MatcherEvent(name, matcher, timeoutSeconds);

    private static AgentIntegrationProfile JsonNested(string id, string displayName, string executable, string config, List<HookEventDescriptor> events) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Executable = executable,
            InstallationKind = InstallationKind.JsonHooks,
            ConfigurationPath = config,
            UsesNestedCommandHooks = true,
            UsesTypedCommandHook = true,
            Events = events
        };

    private static AgentIntegrationProfile JsonTyped(string id, string displayName, string executable, string config, List<HookEventDescriptor> events) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Executable = executable,
            InstallationKind = InstallationKind.JsonHooks,
            ConfigurationPath = config,
            UsesTypedCommandHook = true,
            Events = events
        };

    private static AgentIntegrationProfile JsonCommand(string id, string displayName, string executable, string config, List<HookEventDescriptor> events) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Executable = executable,
            InstallationKind = InstallationKind.JsonHooks,
            ConfigurationPath = config,
            Events = events
        };

    private static AgentIntegrationProfile Yaml(string id, string displayName, string executable, string config, List<HookEventDescriptor> events) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Executable = executable,
            InstallationKind = InstallationKind.YamlHooks,
            ConfigurationPath = config,
            Events = events
        };

    private static AgentIntegrationProfile Plugin(string id, string displayName, string executable, string config, List<HookEventDescriptor> events) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Executable = executable,
            InstallationKind = InstallationKind.PluginDirectory,
            ConfigurationPath = config,
            Events = events
        };
}
