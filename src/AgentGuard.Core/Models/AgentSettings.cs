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
    /// <summary>
    /// Color palette: "porcelain", "aurora", "rose", "shield".
    /// </summary>
    public string ColorPalette { get; set; } = "porcelain";
    /// <summary>
    /// Appearance mode: "system", "light", "dark".
    /// </summary>
    public string AppearanceMode { get; set; } = "system";
}
