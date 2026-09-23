using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SerialDebugTool.ViewModels;

/// <summary>
/// 列表1 的一行：主动发送指令。
/// 对应 send_commands.json 中的一个对象。
/// </summary>
public sealed partial class SendCommandItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "新指令";

    /// <summary>勾选表示“指令”按 16 进制解析。</summary>
    [ObservableProperty]
    private bool _isHex;

    /// <summary>要发送的指令内容（Hex 字符串或文本，文本支持 \r \n 转义）。</summary>
    [ObservableProperty]
    private string _command = string.Empty;

    /// <summary>循环标号：0 = 不循环（只发一次）；N &gt; 0 = 循环 N 次；-1 = 无限循环直到手动停止。</summary>
    [ObservableProperty]
    private int _loopCount;

    /// <summary>发送间隔时间（毫秒），循环时生效。</summary>
    [ObservableProperty]
    private int _delayMs = 500;

    /// <summary>是否参与批量执行 / 自动应答。</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>运行状态（仅运行时使用，不落盘）。</summary>
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private bool _isRunning;

    /// <summary>累计发送次数（仅运行时使用，不落盘）。</summary>
    [JsonIgnore]
    public int SentCount
    {
        get => _sentCount;
        set => SetProperty(ref _sentCount, value);
    }

    private int _sentCount;

    public override string ToString() => Name;
}
