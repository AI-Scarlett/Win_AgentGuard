using System.Globalization;
using System.Windows.Data;
using AgentGuard.Core.Models;

namespace AgentGuard.App.Localization;

public sealed class EnumTextConverter : IValueConverter
{
    private static bool IsChinese => AppText.ActiveLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        || (AppText.ActiveLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!IsChinese || value is null)
        {
            return value?.ToString() ?? "";
        }

        return value switch
        {
            SessionPhase.Ready => "就绪",
            SessionPhase.Idle => "空闲",
            SessionPhase.Processing => "处理中",
            SessionPhase.WaitingApproval => "等待审批",
            SessionPhase.WaitingInput => "等待输入",
            SessionPhase.Compacting => "压缩上下文",
            SessionPhase.Done => "完成",
            SessionPhase.Error => "错误",
            SessionPhase.Interrupted => "已中断",
            PendingRequestType.Permission => "权限审批",
            PendingRequestType.Question => "问题",
            PendingRequestType.Plan => "计划",
            AlertType.BatchDelete => "批量删除",
            AlertType.BatchModify => "批量修改",
            AlertType.SensitiveFile => "敏感文件",
            AlertType.SensitiveContent => "敏感内容",
            AlertType.ProtectedDirectory => "受保护目录",
            AlertType.ProcessLaunch => "进程启动",
            AlertType.ProcessExit => "进程退出",
            AlertType.CommandBlocked => "命令阻断",
            AlertSeverity.Info => "信息",
            AlertSeverity.Warning => "警告",
            AlertSeverity.Critical => "严重",
            OperationType.Create => "创建",
            OperationType.Modify => "修改",
            OperationType.Delete => "删除",
            OperationType.Read => "读取",
            OperationType.Move => "移动",
            OperationType.Rename => "重命名",
            OperationType.Execute => "执行",
            CommandListType.Blacklist => "黑名单",
            CommandListType.Whitelist => "白名单",
            CommandListType.Unclassified => "未分类",
            AdapterStatus.Active => "活跃",
            AdapterStatus.Installed => "已安装",
            AdapterStatus.Unavailable => "不可用",
            AdapterStatus.Error => "错误",
            AgentHistoryOperation.Read => "读取",
            AgentHistoryOperation.Write => "写入",
            AgentHistoryOperation.Edit => "编辑",
            AgentHistoryOperation.Delete => "删除",
            AgentHistoryOperation.Move => "移动",
            AgentHistoryOperation.Rename => "重命名",
            AgentHistoryOperation.Search => "搜索",
            AgentHistoryOperation.Execute => "执行",
            AgentHistoryOperation.Fetch => "拉取",
            AgentHistoryOperation.List => "列表",
            AgentHistoryOperation.Submit => "提交",
            AgentHistoryOperation.Plan => "计划",
            AgentHistoryOperation.Message => "消息",
            string text => ConvertString(text),
            _ => value.ToString() ?? ""
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string ConvertString(string text) => text.ToLowerInvariant() switch
    {
        "launch" => "启动",
        "exit" => "退出",
        "running" => "运行中",
        "completed" => "已完成",
        "failed" => "失败",
        _ => text
    };
}
