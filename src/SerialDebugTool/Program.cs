using System;
using System.Text;
using Avalonia;

namespace SerialDebugTool;

internal static class Program
{
    // 让 GB2312 / GBK 等中文编码在 .NET Core 下可用
    [STAThread]
    public static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
