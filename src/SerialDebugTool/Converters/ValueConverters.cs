using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SerialDebugTool.Models;

namespace SerialDebugTool.Converters;

/// <summary>日志方向 → 颜色。</summary>
public sealed class DirectionToBrushConverter : IValueConverter
{
    private static readonly IBrush Tx = Brush.Parse("#FF6CB6FF");
    private static readonly IBrush Rx = Brush.Parse("#FF63E68A");
    private static readonly IBrush Sys = Brush.Parse("#FFB0B0B0");
    private static readonly IBrush Err = Brush.Parse("#FFFF7B72");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogDirection d
            ? d switch
            {
                LogDirection.Tx => Tx,
                LogDirection.Rx => Rx,
                LogDirection.Err => Err,
                _ => Sys
            }
            : Sys;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// bool → 文本。parameter 形如 "串口已打开|串口未打开"，前者为 true 时显示。
/// </summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|');
        if (parts is not { Length: 2 })
        {
            return string.Empty;
        }

        return value is true ? parts[0] : parts[1];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool 取反。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>bool → double（用于折叠宽度等）。parameter 形如 "560|0"。</summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|');
        if (parts is not { Length: 2 })
        {
            return 0d;
        }

        var text = value is true ? parts[0] : parts[1];
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
