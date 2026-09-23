using SerialDebugTool.Models;

namespace SerialDebugTool.ViewModels;

/// <summary>
/// 界面与行为设置（纯 POCO，直接序列化为 settings.json）。
/// </summary>
public sealed class UiSettings
{
    public SerialPortConfig Serial { get; set; } = new();

    // ---- 日志显示 ----
    public bool HexDisplay { get; set; }
    public bool ShowTimestamp { get; set; } = true;
    public bool AutoScroll { get; set; } = true;
    public bool ShowTx { get; set; } = true;
    public bool ShowRx { get; set; } = true;
    public bool ShowSys { get; set; } = true;
    public string LogFilter { get; set; } = string.Empty;
    public int MaxLogLines { get; set; } = 5000;

    // ---- 发送区 ----
    public string SendText { get; set; } = string.Empty;
    public bool HexSend { get; set; }
    public bool AppendNewLine { get; set; }
    public int SendLoopCount { get; set; }
    public int SendLoopDelayMs { get; set; } = 500;

    // ---- 布局 ----
    public bool RightPanelVisible { get; set; } = true;
    public bool List1Visible { get; set; } = true;
    public bool List2Visible { get; set; } = true;

    // ---- 自动应答 ----
    public bool AutoResponseEnabled { get; set; } = true;

    /// <summary>Contains = 包含匹配，Exact = 完全匹配</summary>
    public string MatchMode { get; set; } = "Contains";
}
