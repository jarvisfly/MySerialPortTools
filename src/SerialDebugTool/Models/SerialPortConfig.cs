using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Text.Json.Serialization;

namespace SerialDebugTool.Models;

/// <summary>串口打开参数（可持久化）。</summary>
public sealed class SerialPortConfig
{
    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; } = 115200;

    public int DataBits { get; set; } = 8;

    /// <summary>None / Odd / Even / Mark / Space</summary>
    public string Parity { get; set; } = nameof(System.IO.Ports.Parity.None);

    /// <summary>One / OnePointFive / Two</summary>
    public string StopBits { get; set; } = nameof(System.IO.Ports.StopBits.One);

    /// <summary>None / RequestToSend / RequestToSendXOnXOff / XOnXOff</summary>
    public string Handshake { get; set; } = nameof(System.IO.Ports.Handshake.None);

    /// <summary>文本编解码名称，如 utf-8 / gb2312 / us-ascii</summary>
    public string EncodingName { get; set; } = "utf-8";

    // 以下为由字符串配置派生的运行时属性，不参与 JSON 序列化
    [JsonIgnore]
    public Parity ParityValue => ParseEnum(Parity, System.IO.Ports.Parity.None);

    [JsonIgnore]
    public StopBits StopBitsValue => ParseEnum(StopBits, System.IO.Ports.StopBits.One);

    [JsonIgnore]
    public Handshake HandshakeValue => ParseEnum(Handshake, System.IO.Ports.Handshake.None);

    [JsonIgnore]
    public Encoding TextEncoding
    {
        get
        {
            try
            {
                return Encoding.GetEncoding(EncodingName);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }
    }

    public SerialPortConfig Clone() => new()
    {
        PortName = PortName,
        BaudRate = BaudRate,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        Handshake = Handshake,
        EncodingName = EncodingName
    };

    private static T ParseEnum<T>(string text, T fallback) where T : struct
        => Enum.TryParse<T>(text, true, out var value) ? value : fallback;
}
