using System.Globalization;

namespace AgentGuard.App.Localization;

public static class AppText
{
    private static bool IsChinese
    {
        get
        {
            var overrideLanguage = Environment.GetEnvironmentVariable("AGENTGUARD_LANG");
            var name = string.IsNullOrWhiteSpace(overrideLanguage)
                ? CultureInfo.CurrentUICulture.Name
                : overrideLanguage;
            return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string WindowTitle => T("AgentGuard for Windows", "AgentGuard Windows 版");
    public static string ProductName => "AgentGuard";
    public static string Server => T("Server", "服务");
    public static string Monitor => T("Monitor", "监控");
    public static string StartServer => T("Start Server", "启动服务");
    public static string StartServerTip => T("Start the local hook server", "启动本地 Hook 服务");
    public static string StopServer => T("Stop Server", "停止服务");
    public static string StopServerTip => T("Stop the local hook server", "停止本地 Hook 服务");
    public static string Refresh => T("Refresh", "刷新");
    public static string RefreshTip => T("Refresh current AgentGuard data", "刷新当前 AgentGuard 数据");
    public static string Pending => T("Pending", "待处理");
    public static string ActiveSessions => T("Active Sessions", "活跃会话");
    public static string AuditRecords => T("Audit Records", "审计记录");
    public static string Alerts => T("Alerts", "告警");
    public static string Agents => T("Agents", "Agent");
    public static string Approvals => T("Approvals", "审批");
    public static string Type => T("Type", "类型");
    public static string Agent => "Agent";
    public static string Title => T("Title", "标题");
    public static string Project => T("Project", "项目");
    public static string Requested => T("Requested", "请求时间");
    public static string Path => T("Path", "路径");
    public static string Detail => T("Detail", "详情");
    public static string ToolInput => T("Tool Input", "工具输入");
    public static string ReplyReason => T("Reply / reason", "回复 / 原因");
    public static string OptionalReasonOrPlanMessage => T("Optional reason or plan message", "可选原因或计划说明");
    public static string Allow => T("Allow", "允许");
    public static string Deny => T("Deny", "拒绝");
    public static string Answer => T("Answer", "回答");
    public static string AcceptPlan => T("Accept Plan", "接受计划");
    public static string CancelPlan => T("Cancel Plan", "取消计划");
    public static string Audit => T("Audit", "审计");
    public static string Time => T("Time", "时间");
    public static string Target => T("Target", "目标");
    public static string Integrations => T("Integrations", "集成");
    public static string RefreshAgents => T("Refresh Agents", "刷新 Agent");
    public static string RefreshAgentsTip => T("Re-scan supported agents and hook status", "重新扫描支持的 Agent 和 Hook 状态");
    public static string InstallSelectedHooks => T("Install Selected Hooks", "安装所选 Hook");
    public static string InstallSelectedHooksTip => T("Install hooks for the selected agent", "为所选 Agent 安装 Hook");
    public static string InstallAllAvailable => T("Install All Available", "安装全部可用 Hook");
    public static string InstallAllAvailableTip => T("Install hooks for every available supported agent", "为所有可用的受支持 Agent 安装 Hook");
    public static string Status => T("Status", "状态");
    public static string Executable => T("Executable", "可执行文件");
    public static string Config => T("Config", "配置");
    public static string Sessions => T("Sessions", "会话");
    public static string Phase => T("Phase", "阶段");
    public static string LastTool => T("Last Tool", "最近工具");
    public static string Cwd => T("Cwd", "工作目录");
    public static string CommandRules => T("Command Rules", "命令规则");
    public static string List => T("List", "列表");
    public static string Pattern => T("Pattern", "模式");
    public static string Calls => T("Calls", "调用");
    public static string Today => T("Today", "今日");
    public static string LastCalledBy => T("Last Called By", "最近调用者");
    public static string Description => T("Description", "说明");
    public static string Consequence => T("Consequence", "影响");
    public static string Severity => T("Severity", "级别");
    public static string Message => T("Message", "消息");
    public static string Settings => T("Settings", "设置");
    public static string ProtectedDirectories => T("Protected directories", "受保护目录");
    public static string ProtectedDirectoriesDescription =>
        T("File changes in these directories are audited by the Windows monitor.",
            "这些目录中的文件变化会被 Windows 本地监控记录。");
    public static string Add => T("Add", "添加");
    public static string AddDirectoryTip => T("Add this directory to monitoring", "将此目录加入监控");
    public static string DirectoryExampleTip => T(@"Example: C:\Users\you\Documents\Project", @"示例：C:\Users\you\Documents\Project");
    public static string Runtime => T("Runtime", "运行状态");
    public static string StartMonitoring => T("Start Monitoring", "启动监控");
    public static string StartMonitoringTip => T("Start local file and process monitoring", "启动本地文件和进程监控");
    public static string StopMonitoring => T("Stop Monitoring", "停止监控");
    public static string StopMonitoringTip => T("Stop local file and process monitoring", "停止本地文件和进程监控");
    public static string ActiveAgentProcesses => T("Active agent processes", "活跃 Agent 进程");
    public static string ProcessEvents => T("Process events", "进程事件");
    public static string Event => T("Event", "事件");
    public static string Pid => "PID";

    public static string StartingAgentGuard => T("Starting AgentGuard...", "正在启动 AgentGuard...");
    public static string AgentGuardReady => T("AgentGuard is ready.", "AgentGuard 已就绪。");
    public static string AgentGuardReadyWithWarnings(string warnings) =>
        T($"AgentGuard opened with startup warnings: {warnings}", $"AgentGuard 已打开，但启动时有警告：{warnings}");
    public static string StartupError(string message) =>
        T($"Startup failed: {message}", $"启动失败：{message}");
    public static string WindowsMonitorRunning => T("Windows monitor is running.", "Windows 本地监控已运行。");
    public static string WindowsMonitorStopped => T("Windows monitor stopped.", "Windows 本地监控已停止。");
    public static string InstallingHooksFor(string displayName) =>
        T($"Installing hooks for {displayName}...", $"正在为 {displayName} 安装 Hook...");
    public static string HooksUpdatedFor(string displayName) =>
        T($"{displayName} hooks updated.", $"{displayName} Hook 已更新。");
    public static string InstallingHooksForAvailableAgents =>
        T("Installing hooks for available agents...", "正在为可用 Agent 安装 Hook...");
    public static string AgentHookInstallationCompleted =>
        T("Agent hook installation pass completed.", "Agent Hook 安装已完成。");
    public static string PermissionDecisionSent(string decision, string title) =>
        decision.Equals("deny", StringComparison.OrdinalIgnoreCase)
            ? T($"deny sent for {title}.", $"已拒绝 {title}。")
            : T($"allow sent for {title}.", $"已允许 {title}。");
    public static string QuestionAnswerSent => T("Question answer sent.", "问题回答已发送。");
    public static string PlanModeSent(string mode) =>
        mode.Equals("cancel", StringComparison.OrdinalIgnoreCase)
            ? T("cancel sent for plan.", "已取消计划。")
            : T("accept sent for plan.", "已接受计划。");
    public static string ProtectedDirectoryAddedAndMonitorRefreshed =>
        T("Protected directory added and monitor refreshed.", "受保护目录已添加，监控已刷新。");
    public static string ProtectedDirectoryAdded => T("Protected directory added.", "受保护目录已添加。");
    public static string ProtectedDirectoryAlreadyExists => T("Protected directory already exists.", "受保护目录已存在。");

    private static string T(string en, string zh) => IsChinese ? zh : en;
}
