using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerialDebugTool.Models;
using SerialDebugTool.Services;

namespace SerialDebugTool.ViewModels;

/// <summary>编码下拉项。</summary>
public sealed class EncodingOption
{
    public EncodingOption(string display, string name)
    {
        Display = display;
        Name = name;
    }

    public string Display { get; }

    public string Name { get; }

    public override string ToString() => Display;
}

/// <summary>匹配方式下拉项。</summary>
public sealed class MatchModeOption
{
    public MatchModeOption(string display, string value)
    {
        Display = display;
        Value = value;
    }

    public string Display { get; }

    public string Value { get; }

    public override string ToString() => Display;
}

/// <summary>主窗口 ViewModel。</summary>
public partial class MainWindowViewModel : ObservableObject
{
    private const int RxBufferLimit = 8192;
    private const int AutoResponseDebounceMs = 20;

    private static readonly int[] DefaultBaudRates =
    {
        1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600
    };

    private static readonly EncodingOption[] EncodingOptionsInternal =
    {
        new("UTF-8", "utf-8"),
        new("GB2312 / GBK", "gb2312"),
        new("ASCII", "us-ascii"),
        new("UTF-16 LE", "utf-16"),
        new("UTF-16 BE", "utf-16BE"),
        new("Latin1", "iso-8859-1")
    };

    private readonly SerialPortService _serial = new();
    private readonly List<LogEntry> _allLogs = new();
    private readonly List<byte> _rxBuffer = new();
    private readonly object _rxSync = new();
    private readonly object _collectionSync = new();
    private readonly Dictionary<object, CancellationTokenSource> _running = new();
    private readonly Dictionary<ReceiveCommandItem, DateTime> _lastHit = new();
    private readonly Timer? _saveTimer;

    private UiSettings _settings = new();
    private bool _isLoading;

    /// <summary>供 View 调用：日志新增后滚动到底部。</summary>
    public event Action? LogAppended;

    /// <summary>由 View 注入，用于文件对话框。</summary>
    public TopLevel? Host { get; set; }

    public MainWindowViewModel()
    {
        Logs = new ObservableCollection<LogEntry>();
        SendCommands = new ObservableCollection<SendCommandItem>();
        ReceiveCommands = new ObservableCollection<ReceiveCommandItem>();
        AvailablePorts = new ObservableCollection<string>();

        BaudRateOptions = DefaultBaudRates.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        DataBitsOptions = new[] { 5, 6, 7, 8 };
        ParityOptions = Enum.GetNames(typeof(System.IO.Ports.Parity));
        StopBitsOptions = Enum.GetNames(typeof(System.IO.Ports.StopBits));
        HandshakeOptions = Enum.GetNames(typeof(System.IO.Ports.Handshake));
        EncodingOptions = EncodingOptionsInternal;
        MatchModeOptions = new[]
        {
            new MatchModeOption("包含匹配", "Contains"),
            new MatchModeOption("完全匹配", "Exact")
        };

        LoadAll();

        _serial.DataReceived += OnSerialDataReceived;
        _serial.ErrorOccurred += message =>
        {
            AppendLog(LogDirection.Err, message);
            if (_serial.IsOpen == false)
            {
                Dispatcher.UIThread.Post(() => IsPortOpen = false);
            }
        };

        SendCommands.CollectionChanged += OnCommandsCollectionChanged;
        ReceiveCommands.CollectionChanged += OnCommandsCollectionChanged;

        // 防抖保存：任何属性变化后 600ms 落盘一次
        _saveTimer = new Timer(_ => SaveAll(), null, Timeout.Infinite, Timeout.Infinite);

        RefreshPorts();
        AppendLog(LogDirection.Sys, "串口调试工具已启动。配置目录：" + JsonStorageService.AppDirectory);
    }

    // ==================== 日志 ====================

    public ObservableCollection<LogEntry> Logs { get; }

    [ObservableProperty]
    private string _logFilter = string.Empty;

    [ObservableProperty]
    private bool _showTx = true;

    [ObservableProperty]
    private bool _showRx = true;

    [ObservableProperty]
    private bool _showSys = true;

    [ObservableProperty]
    private bool _hexDisplay;

    [ObservableProperty]
    private bool _showTimestamp = true;

    [ObservableProperty]
    private bool _autoScroll = true;

    public int MaxLogLines { get; private set; } = 5000;

    public string CounterText => $"日志 {Logs.Count} / {_allLogs.Count} 条　TX {_serial.TxBytes} B　RX {_serial.RxBytes} B";

    // ==================== 串口设置 ====================

    public ObservableCollection<string> AvailablePorts { get; }

    public string[] BaudRateOptions { get; }

    public int[] DataBitsOptions { get; }

    public string[] ParityOptions { get; }

    public string[] StopBitsOptions { get; }

    public string[] HandshakeOptions { get; }

    public EncodingOption[] EncodingOptions { get; }

    public MatchModeOption[] MatchModeOptions { get; }

    [ObservableProperty]
    private string? _selectedPortName;

    [ObservableProperty]
    private string _baudRateText = "115200";

    [ObservableProperty]
    private int _selectedDataBits = 8;

    [ObservableProperty]
    private string _selectedParity = nameof(System.IO.Ports.Parity.None);

    [ObservableProperty]
    private string _selectedStopBits = nameof(System.IO.Ports.StopBits.One);

    [ObservableProperty]
    private string _selectedHandshake = nameof(System.IO.Ports.Handshake.None);

    [ObservableProperty]
    private EncodingOption? _selectedEncoding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortStateText))]
    private bool _isPortOpen;

    [ObservableProperty]
    private string _statusText = "就绪";

    public string PortStateText => IsPortOpen ? "关闭串口" : "打开串口";

    // ==================== 发送区 ====================

    [ObservableProperty]
    private string _sendText = string.Empty;

    [ObservableProperty]
    private bool _hexSend;

    [ObservableProperty]
    private bool _appendNewLine;

    [ObservableProperty]
    private int _sendLoopCount;

    [ObservableProperty]
    private int _sendLoopDelayMs = 500;

    [ObservableProperty]
    private bool _isSending;

    // ==================== 右侧面板 ====================

    [ObservableProperty]
    private bool _rightPanelVisible = true;

    [ObservableProperty]
    private bool _list1Visible = true;

    [ObservableProperty]
    private bool _list2Visible = true;

    [ObservableProperty]
    private bool _autoResponseEnabled = true;

    [ObservableProperty]
    private MatchModeOption? _selectedMatchMode;

    public ObservableCollection<SendCommandItem> SendCommands { get; }

    public ObservableCollection<ReceiveCommandItem> ReceiveCommands { get; }

    private Encoding CurrentEncoding => SelectedEncoding?.Name is { } name
        ? SafeGetEncoding(name)
        : SafeGetEncoding("utf-8");

    // ==================== 串口开关 ====================

    [RelayCommand]
    private void RefreshPorts()
    {
        var previous = SelectedPortName;
        AvailablePorts.Clear();
        foreach (var name in SerialPortService.GetPortNames())
        {
            AvailablePorts.Add(name);
        }

        if (previous != null && AvailablePorts.Contains(previous))
        {
            SelectedPortName = previous;
        }
        else if (AvailablePorts.Count > 0)
        {
            SelectedPortName ??= AvailablePorts[0];
        }

        StatusText = AvailablePorts.Count > 0
            ? $"发现 {AvailablePorts.Count} 个串口"
            : "未发现可用串口（可点刷新重试）";
    }

    [RelayCommand]
    private void TogglePort()
    {
        if (IsPortOpen)
        {
            ClosePort();
            return;
        }

        var config = BuildConfig();
        if (config == null)
        {
            return;
        }

        try
        {
            _serial.ResetCounters();
            _serial.Open(config);
            lock (_rxSync)
            {
                _rxBuffer.Clear();
            }

            IsPortOpen = true;
            StatusText = $"{config.PortName} 已打开 @ {config.BaudRate},{config.DataBits},{ShortStopBits(config.StopBits)},{ShortParity(config.Parity)}";
            AppendLog(LogDirection.Sys, $"串口已打开：{StatusText}");
        }
        catch (Exception ex)
        {
            IsPortOpen = false;
            StatusText = "打开失败";
            AppendLog(LogDirection.Err, $"打开串口失败：{ex.Message}");
        }
    }

    private void ClosePort()
    {
        try
        {
            StopAllLoops();
            _serial.Close();
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"关闭串口异常：{ex.Message}");
        }
        finally
        {
            IsPortOpen = false;
            StatusText = "串口已关闭";
            AppendLog(LogDirection.Sys, "串口已关闭。");
        }
    }

    private SerialPortConfig? BuildConfig()
    {
        if (string.IsNullOrWhiteSpace(SelectedPortName))
        {
            AppendLog(LogDirection.Err, "请先选择串口。");
            return null;
        }

        if (!int.TryParse(BaudRateText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud) || baud <= 0)
        {
            AppendLog(LogDirection.Err, $"波特率无效：{BaudRateText}");
            return null;
        }

        return new SerialPortConfig
        {
            PortName = SelectedPortName!.Trim(),
            BaudRate = baud,
            DataBits = SelectedDataBits,
            Parity = SelectedParity,
            StopBits = SelectedStopBits,
            Handshake = SelectedHandshake,
            EncodingName = SelectedEncoding?.Name ?? "utf-8"
        };
    }

    // ==================== 手动发送 ====================

    [RelayCommand]
    private void Send()
    {
        var text = SendText ?? string.Empty;
        if (AppendNewLine && !text.EndsWith("\n", StringComparison.Ordinal))
        {
            text += "\r\n";
        }

        byte[] payload;
        try
        {
            payload = HexUtility.ToBytes(text, HexSend, CurrentEncoding);
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"发送内容解析失败（Hex 格式不正确）：{ex.Message}");
            return;
        }

        if (payload.Length == 0)
        {
            AppendLog(LogDirection.Err, "发送内容为空。");
            return;
        }

        if (!_serial.IsOpen)
        {
            AppendLog(LogDirection.Err, "串口未打开，无法发送。");
            return;
        }

        if (SendLoopCount == 0)
        {
            SendBytes(payload);
            return;
        }

        _ = RunSendLoopAsync("manual", payload, SendLoopCount, Math.Max(0, SendLoopDelayMs), "手动发送",
            running => Dispatcher.UIThread.Post(() => IsSending = running));
    }

    [RelayCommand]
    private void StopSending() => StopLoop("manual");

    // ==================== 列表1：主动发送指令 ====================

    [RelayCommand]
    private void AddSendItem()
    {
        var item = new SendCommandItem
        {
            Name = $"指令{SendCommands.Count + 1}",
            Command = string.Empty,
            IsHex = HexSend,
            DelayMs = 500,
            LoopCount = 0
        };

        item.PropertyChanged += (_, _) => ScheduleSave();
        lock (_collectionSync)
        {
            SendCommands.Add(item);
        }

        ScheduleSave();
    }

    [RelayCommand]
    private void RemoveSendItem(SendCommandItem? item)
    {
        if (item == null)
        {
            return;
        }

        StopLoop(item);
        lock (_collectionSync)
        {
            SendCommands.Remove(item);
        }

        ScheduleSave();
    }

    [RelayCommand]
    private void RunSendItem(SendCommandItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.IsRunning)
        {
            StopLoop(item);
            return;
        }

        byte[] payload;
        try
        {
            payload = HexUtility.ToBytes(item.Command, item.IsHex, CurrentEncoding);
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"[{item.Name}] 指令解析失败：{ex.Message}");
            return;
        }

        if (payload.Length == 0)
        {
            AppendLog(LogDirection.Err, $"[{item.Name}] 指令内容为空。");
            return;
        }

        if (!_serial.IsOpen)
        {
            AppendLog(LogDirection.Err, "串口未打开，无法发送。");
            return;
        }

        _ = RunSendLoopAsync(item, payload, item.LoopCount, Math.Max(0, item.DelayMs), item.Name,
            running => Dispatcher.UIThread.Post(() => item.IsRunning = running));
    }

    [RelayCommand]
    private void RunAllEnabledSendItems()
    {
        SendCommandItem[] items;
        lock (_collectionSync)
        {
            items = SendCommands.Where(i => i.IsEnabled).ToArray();
        }

        if (items.Length == 0)
        {
            AppendLog(LogDirection.Sys, "没有勾选启用的发送指令。");
            return;
        }

        foreach (var item in items)
        {
            RunSendItem(item);
        }
    }

    // ==================== 列表2：自动应答 ====================

    [RelayCommand]
    private void AddReceiveItem()
    {
        var item = new ReceiveCommandItem
        {
            Name = $"应答{ReceiveCommands.Count + 1}",
            DelayMs = 100,
            LoopCount = 0
        };

        item.PropertyChanged += (_, _) => ScheduleSave();
        lock (_collectionSync)
        {
            ReceiveCommands.Add(item);
        }

        ScheduleSave();
    }

    [RelayCommand]
    private void RemoveReceiveItem(ReceiveCommandItem? item)
    {
        if (item == null)
        {
            return;
        }

        StopLoop(item);
        lock (_collectionSync)
        {
            ReceiveCommands.Remove(item);
        }

        ScheduleSave();
    }

    [RelayCommand]
    private void TestReceiveItem(ReceiveCommandItem? item)
    {
        if (item == null)
        {
            return;
        }

        TriggerAutoResponse(item, "手动测试");
    }

    [RelayCommand]
    private void ClearHitCounts()
    {
        lock (_collectionSync)
        {
            foreach (var item in ReceiveCommands)
            {
                item.HitCount = 0;
            }
        }
    }

    // ==================== 通用停止 ====================

    [RelayCommand]
    private void StopAll()
    {
        StopAllLoops();
        AppendLog(LogDirection.Sys, "已停止全部循环发送。");
    }

    // ==================== 日志操作 ====================

    [RelayCommand]
    private void ClearLog()
    {
        _allLogs.Clear();
        Logs.Clear();
        lock (_rxSync)
        {
            _rxBuffer.Clear();
        }

        _serial.ResetCounters();
        OnPropertyChanged(nameof(CounterText));
        AppendLog(LogDirection.Sys, "日志已清空。");
    }

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        if (Host == null)
        {
            AppendLog(LogDirection.Err, "无法获取窗口句柄，保存失败。");
            return;
        }

        try
        {
            var file = await Host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存日志",
                SuggestedFileName = $"serial_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("日志文件") { Patterns = new[] { "*.log" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            if (file == null)
            {
                return;
            }

            var path = file.TryGetLocalPath();
            LogEntry[] snapshot;
            lock (_collectionSync)
            {
                snapshot = Logs.ToArray();
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteLineAsync($"# 串口调试日志 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            foreach (var entry in snapshot)
            {
                await writer.WriteLineAsync(
                    $"[{entry.Time:yyyy-MM-dd HH:mm:ss.fff}] {entry.DirectionText} {entry.Content}");
            }

            AppendLog(LogDirection.Sys, $"日志已保存：{path ?? file.Path.ToString()}（{snapshot.Length} 条）");
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"保存日志失败：{ex.Message}");
        }
    }

    // ==================== 面板显隐 ====================

    [RelayCommand]
    private void ToggleRightPanel() => RightPanelVisible = !RightPanelVisible;

    [RelayCommand]
    private void ToggleList1() => List1Visible = !List1Visible;

    [RelayCommand]
    private void ToggleList2() => List2Visible = !List2Visible;

    // ==================== 收发核心 ====================

    private bool SendBytes(byte[] payload)
    {
        if (!_serial.IsOpen)
        {
            AppendLog(LogDirection.Err, "串口未打开，发送已中止。");
            return false;
        }

        try
        {
            _serial.Send(payload);
            AppendLog(LogDirection.Tx, HexUtility.ToDisplay(payload, HexDisplay, CurrentEncoding));
            OnPropertyChanged(nameof(CounterText));
            return true;
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"发送失败：{ex.Message}");
            return false;
        }
    }

    private void OnSerialDataReceived(byte[] chunk)
    {
        AppendLog(LogDirection.Rx, HexUtility.ToDisplay(chunk, HexDisplay, CurrentEncoding));
        OnPropertyChanged(nameof(CounterText));

        lock (_rxSync)
        {
            _rxBuffer.AddRange(chunk);
            if (_rxBuffer.Count > RxBufferLimit)
            {
                _rxBuffer.RemoveRange(0, _rxBuffer.Count - RxBufferLimit);
            }
        }

        if (!AutoResponseEnabled)
        {
            return;
        }

        TryAutoRespond(chunk);
    }

    private void TryAutoRespond(byte[] chunk)
    {
        ReceiveCommandItem[] items;
        lock (_collectionSync)
        {
            items = ReceiveCommands.Where(i => i.IsEnabled).ToArray();
        }

        if (items.Length == 0)
        {
            return;
        }

        byte[] snapshot;
        lock (_rxSync)
        {
            snapshot = _rxBuffer.ToArray();
        }

        var exact = SelectedMatchMode?.Value == "Exact";

        foreach (var item in items)
        {
            byte[] expected;
            try
            {
                expected = HexUtility.ToBytes(item.Expected, item.IsHex, CurrentEncoding);
            }
            catch (Exception)
            {
                continue; // 期待指令写错时跳过，不影响其它规则
            }

            if (expected.Length == 0)
            {
                continue;
            }

            var hit = exact
                ? chunk.SequenceEqual(expected) || snapshot.SequenceEqual(expected)
                : HexUtility.IndexOf(snapshot, expected) >= 0;

            if (!hit)
            {
                continue;
            }

            // 防抖：同一条规则在极短时间内不重复触发，避免应答风暴
            if (_lastHit.TryGetValue(item, out var last) &&
                (DateTime.UtcNow - last).TotalMilliseconds < AutoResponseDebounceMs)
            {
                continue;
            }

            _lastHit[item] = DateTime.UtcNow;

            // 命中后清空接收缓冲，避免同一段数据反复触发
            lock (_rxSync)
            {
                _rxBuffer.Clear();
            }

            TriggerAutoResponse(item, "自动匹配");
            if (exact)
            {
                break;
            }
        }
    }

    private void TriggerAutoResponse(ReceiveCommandItem item, string reason)
    {
        byte[] payload;
        try
        {
            payload = HexUtility.ToBytes(item.Response, item.IsHex, CurrentEncoding);
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"[{item.Name}] 应答指令解析失败：{ex.Message}");
            return;
        }

        Dispatcher.UIThread.Post(() => item.HitCount++);

        if (payload.Length == 0)
        {
            AppendLog(LogDirection.Sys, $"[{item.Name}] {reason}命中，但应答指令为空。");
            return;
        }

        AppendLog(LogDirection.Sys, $"[{item.Name}] {reason}命中，自动发送应答。");
        _ = RunSendLoopAsync(item, payload, item.LoopCount, Math.Max(0, item.DelayMs), item.Name,
            running => Dispatcher.UIThread.Post(() => item.IsRunning = running));
    }

    /// <summary>
    /// 循环发送引擎。
    /// loopCount: 0 = 只发一次；N &gt; 0 = 发 N 次；-1 = 无限循环直到手动停止。
    /// </summary>
    private async Task RunSendLoopAsync(
        object ownerKey,
        byte[] payload,
        int loopCount,
        int delayMs,
        string tag,
        Action<bool> setRunning)
    {
        StopLoop(ownerKey);

        var cts = new CancellationTokenSource();
        lock (_collectionSync)
        {
            _running[ownerKey] = cts;
        }

        setRunning(true);

        var infinite = loopCount < 0;
        var total = infinite ? int.MaxValue : Math.Max(1, loopCount);
        var sent = 0;

        if (total > 1)
        {
            AppendLog(LogDirection.Sys,
                infinite
                    ? $"[{tag}] 开始无限循环发送，间隔 {delayMs} ms（点“停止”结束）"
                    : $"[{tag}] 开始循环发送 {total} 次，间隔 {delayMs} ms");
        }

        try
        {
            for (var i = 0; i < total && !cts.IsCancellationRequested; i++)
            {
                if (!SendBytes(payload))
                {
                    break;
                }

                sent++;
                if (ownerKey is SendCommandItem sendItem)
                {
                    Dispatcher.UIThread.Post(() => sendItem.SentCount++);
                }

                if (delayMs > 0 && i < total - 1)
                {
                    await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 手动停止，正常退出
        }
        catch (Exception ex)
        {
            AppendLog(LogDirection.Err, $"[{tag}] 循环发送异常：{ex.Message}");
        }
        finally
        {
            setRunning(false);
            lock (_collectionSync)
            {
                if (_running.TryGetValue(ownerKey, out var current) && ReferenceEquals(current, cts))
                {
                    _running.Remove(ownerKey);
                }
            }

            cts.Dispose();

            if (total > 1)
            {
                AppendLog(LogDirection.Sys, $"[{tag}] 循环发送结束，共发送 {sent} 次。");
            }
        }
    }

    private void StopLoop(object ownerKey)
    {
        CancellationTokenSource? cts = null;
        lock (_collectionSync)
        {
            if (_running.TryGetValue(ownerKey, out cts))
            {
                _running.Remove(ownerKey);
            }
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception)
        {
            // 忽略取消异常
        }
    }

    private void StopAllLoops()
    {
        object[] keys;
        lock (_collectionSync)
        {
            keys = _running.Keys.ToArray();
        }

        foreach (var key in keys)
        {
            StopLoop(key);
        }

        Dispatcher.UIThread.Post(() => IsSending = false);
    }

    // ==================== 日志写入 ====================

    private void AppendLog(LogDirection direction, string content)
    {
        var entry = new LogEntry(direction, content);

        void Apply()
        {
            _allLogs.Add(entry);

            if (_allLogs.Count > MaxLogLines)
            {
                var overflow = _allLogs.Count - MaxLogLines;
                for (var i = 0; i < overflow; i++)
                {
                    var removed = _allLogs[0];
                    _allLogs.RemoveAt(0);
                    Logs.Remove(removed);
                }
            }

            if (PassesFilter(entry))
            {
                Logs.Add(entry);
            }

            OnPropertyChanged(nameof(CounterText));
            LogAppended?.Invoke();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private bool PassesFilter(LogEntry entry)
    {
        var directionOk = entry.Direction switch
        {
            LogDirection.Tx => ShowTx,
            LogDirection.Rx => ShowRx,
            LogDirection.Sys or LogDirection.Err => ShowSys,
            _ => true
        };

        if (!directionOk)
        {
            return false;
        }

        var keyword = LogFilter?.Trim();
        return string.IsNullOrEmpty(keyword)
            || entry.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnLogFilterChanged(string value) => RebuildFilteredLogs();

    partial void OnShowTxChanged(bool value) => RebuildFilteredLogs();

    partial void OnShowRxChanged(bool value) => RebuildFilteredLogs();

    partial void OnShowSysChanged(bool value) => RebuildFilteredLogs();

    private void RebuildFilteredLogs()
    {
        Logs.Clear();
        foreach (var entry in _allLogs.Where(PassesFilter))
        {
            Logs.Add(entry);
        }

        OnPropertyChanged(nameof(CounterText));
        LogAppended?.Invoke();
    }

    // ==================== 持久化 ====================

    private void ScheduleSave()
    {
        if (_isLoading)
        {
            return;
        }

        _saveTimer?.Change(600, Timeout.Infinite);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // UI 状态变化统一触发防抖保存（CounterText 等派生属性也会走到这里，但代价可忽略）
        if (e.PropertyName != nameof(CounterText))
        {
            ScheduleSave();
        }
    }

    private void OnCommandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 两个列表共用同一个处理器，必须按元素实际类型分派，不能强转
        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems)
            {
                switch (newItem)
                {
                    case SendCommandItem sendItem:
                        sendItem.PropertyChanged -= OnSendItemPropertyChanged;
                        sendItem.PropertyChanged += OnSendItemPropertyChanged;
                        break;
                    case ReceiveCommandItem receiveItem:
                        receiveItem.PropertyChanged -= OnReceiveItemPropertyChanged;
                        receiveItem.PropertyChanged += OnReceiveItemPropertyChanged;
                        break;
                }
            }
        }

        ScheduleSave();
    }

    private void OnSendItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleSave();

    private void OnReceiveItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleSave();

    /// <summary>加载全部本地化 JSON 配置。</summary>
    public void LoadAll()
    {
        _isLoading = true;
        try
        {
            _settings = JsonStorageService.Load<UiSettings>(JsonStorageService.SettingsFile) ?? new UiSettings();

            var s = _settings;
            SelectedPortName = string.IsNullOrWhiteSpace(s.Serial.PortName) ? null : s.Serial.PortName;
            BaudRateText = s.Serial.BaudRate.ToString(CultureInfo.InvariantCulture);
            SelectedDataBits = s.Serial.DataBits;
            SelectedParity = s.Serial.Parity;
            SelectedStopBits = s.Serial.StopBits;
            SelectedHandshake = s.Serial.Handshake;
            SelectedEncoding = EncodingOptionsInternal.FirstOrDefault(e =>
                                   string.Equals(e.Name, s.Serial.EncodingName, StringComparison.OrdinalIgnoreCase))
                               ?? EncodingOptionsInternal[0];

            HexDisplay = s.HexDisplay;
            ShowTimestamp = s.ShowTimestamp;
            AutoScroll = s.AutoScroll;
            ShowTx = s.ShowTx;
            ShowRx = s.ShowRx;
            ShowSys = s.ShowSys;
            LogFilter = s.LogFilter;
            MaxLogLines = s.MaxLogLines <= 0 ? 5000 : s.MaxLogLines;

            SendText = s.SendText;
            HexSend = s.HexSend;
            AppendNewLine = s.AppendNewLine;
            SendLoopCount = s.SendLoopCount;
            SendLoopDelayMs = s.SendLoopDelayMs;

            RightPanelVisible = s.RightPanelVisible;
            List1Visible = s.List1Visible;
            List2Visible = s.List2Visible;
            AutoResponseEnabled = s.AutoResponseEnabled;
            SelectedMatchMode = MatchModeOptions.FirstOrDefault(m => m.Value == s.MatchMode) ?? MatchModeOptions[0];

            var sendItems = JsonStorageService.Load<List<SendCommandItem>>(JsonStorageService.SendCommandFile);
            var recvItems = JsonStorageService.Load<List<ReceiveCommandItem>>(JsonStorageService.ReceiveCommandFile);

            lock (_collectionSync)
            {
                SendCommands.Clear();
                if (sendItems != null)
                {
                    foreach (var item in sendItems)
                    {
                        SendCommands.Add(item);
                    }
                }

                ReceiveCommands.Clear();
                if (recvItems != null)
                {
                    foreach (var item in recvItems)
                    {
                        ReceiveCommands.Add(item);
                    }
                }
            }

            if (SendCommands.Count == 0)
            {
                SeedSampleData();
            }
        }
        catch (Exception ex)
        {
            StatusText = "配置加载失败，已使用默认值";
            AppendLog(LogDirection.Err, $"加载本地配置失败：{ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SeedSampleData()
    {
        SendCommands.Add(new SendCommandItem
        {
            Name = "查询版本",
            IsHex = true,
            Command = "AA 01 00 55",
            LoopCount = 0,
            DelayMs = 500
        });
        SendCommands.Add(new SendCommandItem
        {
            Name = "心跳(循环3次)",
            IsHex = true,
            Command = "AA 02 00 55",
            LoopCount = 3,
            DelayMs = 1000
        });

        ReceiveCommands.Add(new ReceiveCommandItem
        {
            Name = "版本应答",
            IsHex = true,
            Expected = "AA 01",
            Response = "AA 81 00 55",
            LoopCount = 0,
            DelayMs = 100
        });
    }

    /// <summary>保存全部本地化 JSON 配置（退出时同步调用）。</summary>
    public void SaveAll()
    {
        _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        var settings = new UiSettings
        {
            Serial = BuildConfigForSave(),
            HexDisplay = HexDisplay,
            ShowTimestamp = ShowTimestamp,
            AutoScroll = AutoScroll,
            ShowTx = ShowTx,
            ShowRx = ShowRx,
            ShowSys = ShowSys,
            LogFilter = LogFilter ?? string.Empty,
            MaxLogLines = MaxLogLines,
            SendText = SendText ?? string.Empty,
            HexSend = HexSend,
            AppendNewLine = AppendNewLine,
            SendLoopCount = SendLoopCount,
            SendLoopDelayMs = SendLoopDelayMs,
            RightPanelVisible = RightPanelVisible,
            List1Visible = List1Visible,
            List2Visible = List2Visible,
            AutoResponseEnabled = AutoResponseEnabled,
            MatchMode = SelectedMatchMode?.Value ?? "Contains"
        };

        SendCommandItem[] sendItems;
        ReceiveCommandItem[] recvItems;
        lock (_collectionSync)
        {
            sendItems = SendCommands.ToArray();
            recvItems = ReceiveCommands.ToArray();
        }

        JsonStorageService.Save(JsonStorageService.SettingsFile, settings);
        JsonStorageService.Save(JsonStorageService.SendCommandFile, sendItems.ToList());
        JsonStorageService.Save(JsonStorageService.ReceiveCommandFile, recvItems.ToList());
    }

    private SerialPortConfig BuildConfigForSave()
    {
        int.TryParse(BaudRateText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud);
        return new SerialPortConfig
        {
            PortName = SelectedPortName ?? string.Empty,
            BaudRate = baud > 0 ? baud : 115200,
            DataBits = SelectedDataBits,
            Parity = SelectedParity,
            StopBits = SelectedStopBits,
            Handshake = SelectedHandshake,
            EncodingName = SelectedEncoding?.Name ?? "utf-8"
        };
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        AppendLog(LogDirection.Sys, "配置文件目录：" + JsonStorageService.AppDirectory);
        StatusText = JsonStorageService.AppDirectory;
    }

    // ==================== 小工具 ====================

    private static Encoding SafeGetEncoding(string name)
    {
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }

    private static string ShortStopBits(string value) => value switch
    {
        nameof(System.IO.Ports.StopBits.One) => "1",
        nameof(System.IO.Ports.StopBits.OnePointFive) => "1.5",
        nameof(System.IO.Ports.StopBits.Two) => "2",
        _ => value
    };

    private static string ShortParity(string value) => value switch
    {
        nameof(System.IO.Ports.Parity.None) => "N",
        nameof(System.IO.Ports.Parity.Odd) => "O",
        nameof(System.IO.Ports.Parity.Even) => "E",
        nameof(System.IO.Ports.Parity.Mark) => "M",
        nameof(System.IO.Ports.Parity.Space) => "S",
        _ => value
    };
}
