using System.Globalization;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Localization;

public static class CoreText
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

    public static string AgentCenter => T("Agent Center", "Agent 中心");
    public static string AgentSession => T("Agent Session", "Agent 会话");
    public static string ApprovalRequest => T("Approval Request", "审批请求");
    public static string QuestionRequest => T("Question Request", "问题请求");
    public static string PlanApproval => T("Plan Approval", "计划审批");
    public static string Question => T("Question", "问题");
    public static string Plan => T("Plan", "计划");
    public static string AgentGuardStopped => T("AgentGuard stopped", "AgentGuard 已停止");

    public static string RecursiveDeleteDescription => T("Recursive delete", "递归删除");
    public static string RecursiveDeleteConsequence => T("Can remove large folder trees", "可能删除大型目录树");
    public static string RecursiveWindowsDeleteDescription => T("Recursive Windows delete", "Windows 递归删除");
    public static string RecursiveWindowsDeleteConsequence => T("Can remove many files without confirmation", "可能在无确认情况下删除大量文件");
    public static string PowerShellDeletionDescription => T("PowerShell deletion", "PowerShell 删除");
    public static string PowerShellDeletionConsequence => T("Review recursive and force flags", "请检查递归和强制删除参数");
    public static string InspectRepositoryState => T("Inspect repository state", "查看仓库状态");
    public static string BuildDotNetProject => T("Build .NET project", "构建 .NET 项目");

    public static string AgentProcessLaunchedTitle => T("Agent process launched", "Agent 进程已启动");
    public static string AgentProcessLaunchedMessage(string agentName, int processId) =>
        T($"{agentName} started (PID {processId})", $"{agentName} 已启动（PID {processId}）");

    public static string EmptyCommand => T("Empty command", "空命令");
    public static string BlockedCommandPatternTitle => T("Blocked command pattern", "命中高风险命令规则");
    public static string BlockedCommandMessage(string agentName, string pattern) =>
        T($"{agentName} used command pattern: {pattern}", $"{agentName} 使用了命令规则：{pattern}");
    public static string DiscoveredCommandDescription => T("Discovered command", "发现新命令");
    public static string DiscoveredCommandMessage => T("Discovered command", "发现新命令");

    public static string BatchDeleteAlertTitle => T("Batch delete alert", "批量删除告警");
    public static string BatchDeleteAlertMessage(string agentName, int count, double seconds) =>
        T($"{agentName} deleted {count} files in {seconds:0}s", $"{agentName} 在 {seconds:0} 秒内删除了 {count} 个文件");
    public static string DeleteEventsDetail(int count) => T($"{count} delete events", $"{count} 个删除事件");

    public static string BatchModifyAlertTitle => T("Batch modify alert", "批量修改告警");
    public static string BatchModifyAlertMessage(string agentName, int count, double seconds) =>
        T($"{agentName} modified {count} files in {seconds:0}s", $"{agentName} 在 {seconds:0} 秒内修改了 {count} 个文件");
    public static string ModifyEventsDetail(int count) => T($"{count} modify events", $"{count} 个修改事件");

    public static string ProtectedDirectoryAccessTitle => T("Protected directory access", "受保护目录访问");
    public static string ProtectedDirectoryAccessMessage(string agentName, OperationType operationType) =>
        T($"{agentName} {operationType} in protected directory", $"{agentName} 在受保护目录中执行了 {Display(operationType)}");

    public static string SensitiveFileAccessTitle => T("Sensitive file access", "敏感文件访问");
    public static string SensitiveFileAccessMessage(string agentName, string fileName) =>
        T($"{agentName} touched {fileName}", $"{agentName} 访问了 {fileName}");

    public static string SensitiveContentDetectedTitle => T("Sensitive content detected", "检测到敏感内容");
    public static string SensitiveContentDetectedMessage(string agentName, string description) =>
        T($"{agentName} modified content containing {description}", $"{agentName} 修改了包含{description}的内容");

    public static string EnvironmentVariableFile => T("Environment variable file", "环境变量文件");
    public static string PrivateKeyOrCertificate => T("Private key or certificate", "私钥或证书");
    public static string CloudServiceCredentials => T("Cloud service credentials", "云服务凭据");
    public static string PackageManagerAuthToken => T("Package manager auth token", "包管理器认证令牌");
    public static string MobileAppSecret => T("Mobile app secret", "移动应用密钥");

    public static string AuditReportSummary(int operationCount, int agentCount) =>
        T($"Recorded {operationCount} operations from {agentCount} agents.",
            $"记录了来自 {agentCount} 个 Agent 的 {operationCount} 次操作。");

    public static string HookServerListening(string pipeName) =>
        T($"Hook server listening on \\\\.\\pipe\\{pipeName}", $"Hook 服务已监听 \\\\.\\pipe\\{pipeName}");
    public static string HookServerStopped => T("Hook server stopped", "Hook 服务已停止");
    public static string HookServerError(string message) =>
        T($"Hook server error: {message}", $"Hook 服务错误：{message}");
    public static string FailedToProcessHookEvent(string message) =>
        T($"Failed to process hook event: {message}", $"处理 Hook 事件失败：{message}");
    public static string WaitingForResponse(PendingRequestType type, string title) =>
        T($"Waiting for {type} response: {title}", $"等待{Display(type)}响应：{title}");

    public static string Display(PendingRequestType type) => type switch
    {
        PendingRequestType.Permission => T("Permission", "权限审批"),
        PendingRequestType.Question => T("Question", "问题"),
        PendingRequestType.Plan => T("Plan", "计划"),
        _ => type.ToString()
    };

    public static string Display(OperationType type) => type switch
    {
        OperationType.Create => T("Create", "创建"),
        OperationType.Modify => T("Modify", "修改"),
        OperationType.Delete => T("Delete", "删除"),
        OperationType.Read => T("Read", "读取"),
        OperationType.Move => T("Move", "移动"),
        OperationType.Rename => T("Rename", "重命名"),
        OperationType.Execute => T("Execute", "执行"),
        _ => type.ToString()
    };

    private static string T(string en, string zh) => IsChinese ? zh : en;
}
