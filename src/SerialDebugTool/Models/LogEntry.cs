using System;

namespace SerialDebugTool.Models;

/// <summary>日志方向。</summary>
public enum LogDirection
{
    /// <summary>发送</summary>
    Tx,

    /// <summary>接收</summary>
    Rx,

    /// <summary>系统提示</summary>
    Sys,

    /// <summary>错误</summary>
    Err
}

/// <summary>一条收发日志（不可变，直接给 UI 绑定）。</summary>
public sealed class LogEntry
{
    public LogEntry(LogDirection direction, string content, DateTime? time = null)
    {
        Direction = direction;
        Content = content;
        Time = time ?? DateTime.Now;
    }

    public DateTime Time { get; }

    public LogDirection Direction { get; }

    public string Content { get; }

    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string DirectionText => Direction switch
    {
        LogDirection.Tx => "→ TX",
        LogDirection.Rx => "← RX",
        LogDirection.Sys => "· SYS",
        _ => "× ERR"
    };
}
