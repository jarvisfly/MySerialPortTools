using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SerialDebugTool.Models;

namespace SerialDebugTool.Services;

/// <summary>
/// 串口收发服务。
/// 采用后台异步读流（BaseStream.ReadAsync）而非 DataReceived 事件，
/// 这样在 Windows / Linux / macOS 上行为一致，也不会因为事件线程回调导致 UI 竞争。
/// </summary>
public sealed class SerialPortService : IDisposable
{
    private readonly object _sync = new();
    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    /// <summary>收到数据（后台线程触发，调用方需自行切回 UI 线程）。</summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>串口异常断开或读写错误。</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>串口已打开。</summary>
    public event Action<string>? Opened;

    /// <summary>串口已关闭。</summary>
    public event Action<string>? Closed;

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return _port is { IsOpen: true };
            }
        }
    }

    public string CurrentPortName
    {
        get
        {
            lock (_sync)
            {
                return _port?.PortName ?? string.Empty;
            }
        }
    }

    /// <summary>累计收发字节数（用于状态栏）。</summary>
    public long TxBytes { get; private set; }

    public long RxBytes { get; private set; }

    public void ResetCounters()
    {
        TxBytes = 0;
        RxBytes = 0;
    }

    /// <summary>枚举可用串口。</summary>
    public static IReadOnlyList<string> GetPortNames()
    {
        var names = new List<string>();
        try
        {
            names.AddRange(SerialPort.GetPortNames());
        }
        catch (Exception)
        {
            // 某些平台无串口驱动时会抛异常，忽略
        }

        // Linux 下补充常见设备节点（部分驱动不会出现在 GetPortNames 中）
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var extra = System.IO.Directory
                    .GetFiles("/dev", "tty*")
                    .Where(p => p.StartsWith("/dev/ttyUSB", StringComparison.Ordinal)
                             || p.StartsWith("/dev/ttyACM", StringComparison.Ordinal)
                             || p.StartsWith("/dev/ttyS", StringComparison.Ordinal)
                             || p.StartsWith("/dev/ttyAMA", StringComparison.Ordinal));
                names.AddRange(extra);
            }
            catch (Exception)
            {
                // 无 /dev 访问权限时忽略
            }
        }

        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>打开串口。</summary>
    public void Open(SerialPortConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.PortName))
        {
            throw new InvalidOperationException("请先选择串口。");
        }

        lock (_sync)
        {
            if (_port is { IsOpen: true })
            {
                throw new InvalidOperationException($"串口 {config.PortName} 已处于打开状态。");
            }

            var port = new SerialPort(config.PortName, config.BaudRate)
            {
                DataBits = config.DataBits,
                Parity = config.ParityValue,
                StopBits = config.StopBitsValue,
                Handshake = config.HandshakeValue,
                ReadTimeout = 500,
                WriteTimeout = 1000,
                Encoding = config.TextEncoding
            };

            port.Open();

            // 部分设备（虚拟串口 / 某些 USB 转串口）不支持 modem 控制线，设置失败不应影响打开
            TrySetModemLines(port, config.HandshakeValue);

            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            _port = port;
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(port, _readCts.Token));
            Opened?.Invoke(config.PortName);
        }
    }

    /// <summary>关闭串口。</summary>
    public void Close()
    {
        SerialPort? port;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            port = _port;
            cts = _readCts;
            _port = null;
            _readCts = null;
        }

        if (port == null)
        {
            return;
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception)
        {
            // 忽略
        }

        var name = port.PortName;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch (Exception)
        {
            // 拔线时 Close 可能抛异常，忽略
        }
        finally
        {
            port.Dispose();
            cts?.Dispose();
            Closed?.Invoke(name);
        }
    }

    /// <summary>发送字节。线程安全。</summary>
    public void Send(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var bytes = data.ToArray();
        lock (_sync)
        {
            if (_port is not { IsOpen: true })
            {
                throw new InvalidOperationException("串口未打开。");
            }

            _port.Write(bytes, 0, bytes.Length);
            TxBytes += bytes.Length;
        }
    }

    private static void TrySetModemLines(SerialPort port, Handshake handshake)
    {
        try
        {
            port.DtrEnable = true;
        }
        catch (Exception)
        {
            // 设备不支持 DTR，忽略
        }

        try
        {
            if (handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff)
            {
                port.RtsEnable = true;
            }
        }
        catch (Exception)
        {
            // 设备不支持 RTS，忽略
        }
    }

    private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested && port.IsOpen)
            {
                int read;
                try
                {
                    read = await port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (read <= 0)
                {
                    // 返回 0 通常意味着设备被拔出
                    if (!token.IsCancellationRequested)
                    {
                        ErrorOccurred?.Invoke("串口连接已断开（设备可能被拔出）。");
                    }

                    break;
                }

                var chunk = new byte[read];
                Array.Copy(buffer, chunk, read);
                RxBytes += read;
                DataReceived?.Invoke(chunk);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                ErrorOccurred?.Invoke($"读取串口失败：{ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Close();
        }
        catch (Exception)
        {
            // 忽略释放期异常
        }

        _readTask = null;
    }
}
