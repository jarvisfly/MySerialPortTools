using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SerialDebugTool.ViewModels;

/// <summary>
/// 列表2 的一行：收到期待指令后自动应答。
/// 对应 receive_commands.json 中的一个对象。
/// </summary>
public sealed partial class ReceiveCommandItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "新应答";

    /// <summary>勾选表示“期待接收指令”和“自动发送指令”都按 16 进制解析。</summary>
    [ObservableProperty]
    private bool _isHex;

    /// <summary>期待接收到的指令内容（命中即触发应答）。</summary>
    [ObservableProperty]
    private string _expected = string.Empty;

    /// <summary>命中后自动发送的指令内容。</summary>
    [ObservableProperty]
    private string _response = string.Empty;

    /// <summary>循环标号：0 = 不循环（只回一次）；N &gt; 0 = 循环 N 次；-1 = 无限循环直到手动停止。</summary>
    [ObservableProperty]
    private int _loopCount;

    /// <summary>发送间隔时间（毫秒），循环时生效。</summary>
    [ObservableProperty]
    private int _delayMs = 500;

    /// <summary>是否启用该条自动应答规则。</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>运行状态（不落盘）。</summary>
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private bool _isRunning;

    /// <summary>命中次数（不落盘）。</summary>
    [JsonIgnore]
    public int HitCount
    {
        get => _hitCount;
        set => SetProperty(ref _hitCount, value);
    }

    private int _hitCount;

    public override string ToString() => Name;
}
