namespace AgentGuard.Core.Models;

public sealed class AgentSettings
{
    public string BridgePath { get; set; } = "";
    public bool NotificationsEnabled { get; set; } = true;
    public bool AutoScanAgentHistoryOnStartup { get; set; } = false;
    public int HistoryRecordLimit { get; set; } = 2000;
    public List<string> RecentBridgePaths { get; set; } = [];
    /// <summary>
    /// UI language code: "auto" (follow OS), "en", "zh-CN", "zh-TW".
    /// </summary>
    public string Language { get; set; } = "auto";
}
