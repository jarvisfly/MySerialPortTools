using System;
using System.Collections.Generic;
using System.Text;

namespace SerialDebugTool.Services;

/// <summary>Hex / 文本 互转工具。</summary>
public static class HexUtility
{
    /// <summary>
    /// 把用户输入的 Hex 字符串解析成字节数组。
    /// 支持："AA BB"、"AABB"、"0xAA,0xBB"、"aa-bb"、"AA\tBB"，以及 \r \n \t \0 转义。
    /// </summary>
    public static byte[] ParseHex(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<byte>();
        }

        var text = input.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Replace("0X", string.Empty, StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsHexDigit(c))
            {
                sb.Append(c);
            }
            else if (c is '\\' && i + 1 < text.Length)
            {
                // 允许 \r \n \t \0 这类转义写法（按 Hex 处理时通常不需要，这里容错跳过）
                i++;
            }
            // 其余分隔符（空格、逗号、短横线、制表、换行）直接忽略
        }

        if (sb.Length % 2 != 0)
        {
            sb.Insert(0, '0'); // 奇数位时前面补 0，避免抛异常
        }

        var result = new byte[sb.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Convert.ToByte(sb.ToString(i * 2, 2), 16);
        }

        return result;
    }

    /// <summary>是否为合法 Hex 字符串（忽略分隔符后长度为偶数且全为 Hex 字符）。</summary>
    public static bool IsValidHex(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return true; // 空串视为合法（发送时提示即可）
        }

        try
        {
            ParseHex(input);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>字节数组转 "AA BB CC" 形式。</summary>
    public static string ToHex(ReadOnlySpan<byte> data, string separator = " ")
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            sb.Append(data[i].ToString("X2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把用户输入按“是否 Hex”转换为待发送字节。
    /// Hex 模式：解析 16 进制；文本模式：按指定编码，并支持 \r \n \t \0 转义。
    /// </summary>
    public static byte[] ToBytes(string? input, bool isHex, Encoding encoding)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Array.Empty<byte>();
        }

        return isHex ? ParseHex(input) : encoding.GetBytes(Unescape(input));
    }

    /// <summary>把接收到的字节按“是否 Hex”转换为显示文本。</summary>
    public static string ToDisplay(ReadOnlySpan<byte> data, bool isHex, Encoding encoding)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        if (isHex)
        {
            return ToHex(data);
        }

        var text = encoding.GetString(data);
        // 控制字符转成可见转义，避免日志里出现"空白行"看不出内容
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\0':
                    sb.Append("\\0");
                    break;
                default:
                    sb.Append(char.IsControl(ch) ? $"\\x{(int)ch:X2}" : ch);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>处理 \r \n \t \0 \\ 转义。</summary>
    public static string Unescape(string input)
    {
        if (input.IndexOf('\\') < 0)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != '\\' || i + 1 >= input.Length)
            {
                sb.Append(input[i]);
                continue;
            }

            i++;
            switch (input[i])
            {
                case 'r':
                    sb.Append('\r');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case '0':
                    sb.Append('\0');
                    break;
                case '\\':
                    sb.Append('\\');
                    break;
                default:
                    sb.Append('\\').Append(input[i]);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>在缓冲区中查找子串，返回索引，未找到返回 -1。</summary>
    public static int IndexOf(IReadOnlyList<byte> buffer, IReadOnlyList<byte> pattern)
    {
        if (pattern.Count == 0 || buffer.Count < pattern.Count)
        {
            return -1;
        }

        var last = buffer.Count - pattern.Count;
        for (var i = 0; i <= last; i++)
        {
            var matched = true;
            for (var j = 0; j < pattern.Count; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
