using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using SerialDebugTool;
using SerialDebugTool.Models;
using SerialDebugTool.Services;
using SerialDebugTool.ViewModels;
using SerialDebugTool.Views;

namespace SmokeTest;

internal static class Program
{
    private static int _failed;

    private static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 1) 无界面启动 Avalonia，验证 XAML / 主题 / DataGrid 模板能真正加载
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(300);

        Check("窗口 XAML 加载成功", window.IsVisible);
        Check("启动日志已写入", vm.Logs.Count > 0);

        // 2) Hex 工具单元校验
        var parsed = HexUtility.ParseHex("AA 01,0xBB-cc");
        Check("ParseHex 兼容多种分隔符", parsed.SequenceEqual(new byte[] { 0xAA, 0x01, 0xBB, 0xCC }));
        Check("ToHex 输出空格分隔", HexUtility.ToHex(parsed) == "AA 01 BB CC");
        Check("文本模式转义 \\r\\n",
            HexUtility.ToBytes("AB\\r\\n", false, Encoding.UTF8).SequenceEqual(new byte[] { 0x41, 0x42, 0x0D, 0x0A }));
        Check("IndexOf 子串查找", HexUtility.IndexOf(new byte[] { 1, 2, 3, 4 }, new byte[] { 3, 4 }) == 2);

        // 3) 指令列表 + JSON 本地化持久化
        vm.AddSendItemCommand.Execute(null);
        var added = vm.SendCommands[^1];
        added.Name = "持久化测试";
        added.IsHex = true;
        added.Command = "11 22 33";
        added.LoopCount = 3;
        added.DelayMs = 250;

        vm.AddReceiveItemCommand.Execute(null);
        var recv = vm.ReceiveCommands[^1];
        recv.Name = "应答测试";
        recv.IsHex = true;
        recv.Expected = "AA 81";
        recv.Response = "BB 22";

        vm.SaveAll();
        Check("send_commands.json 已生成", File.Exists(JsonStorageService.GetFullPath(JsonStorageService.SendCommandFile)));
        Check("receive_commands.json 已生成", File.Exists(JsonStorageService.GetFullPath(JsonStorageService.ReceiveCommandFile)));
        Check("settings.json 已生成", File.Exists(JsonStorageService.GetFullPath(JsonStorageService.SettingsFile)));

        var reloadedSend = JsonStorageService.Load<System.Collections.Generic.List<SendCommandItem>>(JsonStorageService.SendCommandFile);
        Check("重新加载后指令内容一致",
            reloadedSend != null && reloadedSend.Any(i => i.Name == "持久化测试" && i.Command == "11 22 33" && i.LoopCount == 3 && i.DelayMs == 250));
        var sendJson = File.ReadAllText(JsonStorageService.GetFullPath(JsonStorageService.SendCommandFile));
        Check("运行时状态不写入 JSON", !sendJson.Contains("IsRunning") && !sendJson.Contains("SentCount"));
        var reloadedRecv = JsonStorageService.Load<System.Collections.Generic.List<ReceiveCommandItem>>(JsonStorageService.ReceiveCommandFile);
        Check("重新加载后应答内容一致",
            reloadedRecv != null && reloadedRecv.Any(i => i.Name == "应答测试" && i.Expected == "AA 81" && i.Response == "BB 22"));
        var recvJson = File.ReadAllText(JsonStorageService.GetFullPath(JsonStorageService.ReceiveCommandFile));
        Check("命中次数不写入 JSON", !recvJson.Contains("HitCount"));

        // 4) 面板显隐（自动隐藏）
        vm.ToggleList1Command.Execute(null);
        Check("列表1 可隐藏", vm.List1Visible == false);
        vm.ToggleList1Command.Execute(null);
        vm.ToggleRightPanelCommand.Execute(null);
        Check("右侧整栏可隐藏", vm.RightPanelVisible == false);
        vm.ToggleRightPanelCommand.Execute(null);
        Pump(100);

        // 5) 虚拟串口端到端：打开 → 发送 → 接收 → 自动应答回写
        var slave = File.Exists("/tmp/pty_slave.txt") ? File.ReadAllText("/tmp/pty_slave.txt").Trim() : null;
        if (string.IsNullOrEmpty(slave))
        {
            Console.WriteLine("SKIP: 未找到虚拟串口（/tmp/pty_slave.txt），跳过串口端到端测试");
        }
        else
        {
            vm.SelectedPortName = slave;
            vm.BaudRateText = "115200";
            vm.HexSend = true;
            vm.HexDisplay = true;
            vm.AutoResponseEnabled = true;
            vm.SelectedMatchMode = vm.MatchModeOptions.First(m => m.Value == "Contains");
            vm.TogglePortCommand.Execute(null);
            Pump(200);

            Check($"打开虚拟串口 {slave}", vm.IsPortOpen);

            vm.SendText = "AA 01 00 55";
            vm.SendCommand.Execute(null);
            Pump(2500);

            Check("收到设备应答（RX 日志）", vm.Logs.Any(l => l.Direction == LogDirection.Rx));
            Check("自动应答规则被命中", vm.ReceiveCommands.Any(i => i.Name == "应答测试" && i.HitCount > 0));

            var deviceRx = File.Exists("/tmp/device_rx.log") ? File.ReadAllText("/tmp/device_rx.log") : string.Empty;
            Check("应答指令 BB 22 真实发出到串口", deviceRx.Contains("BB 22", StringComparison.OrdinalIgnoreCase));

            vm.TogglePortCommand.Execute(null); // IsPortOpen == true → 走关闭分支
            Pump(200);
            Check("串口已关闭", vm.IsPortOpen == false);
        }

        window.Close();
        Pump(200);

        Console.WriteLine(_failed == 0
            ? "\n===== 冒烟测试全部通过 ====="
            : $"\n===== 冒烟测试失败 {_failed} 项 =====");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok)
    {
        if (!ok)
        {
            _failed++;
        }

        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
    }

    private static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }
    }
}
