# 串口调试工具（C# + .NET 8 + Avalonia 11 + MVVM）

一个跨平台（Windows / Linux / macOS）的串口调试助手，采用严格 MVVM 分层，界面为**左右结构**：

- **左侧上半部分**：发送 / 接收日志
- **左侧下半部分**：串口设置（波特率、数据位、校验位、停止位、流控、编码）、Hex 显示、时间显示、发送输入框与发送按钮
- **右侧**：两个可自动隐藏的指令列表（DataGrid）
  - **列表1 · 主动发送指令**：名称 / HEX 勾选 / 指令 / 循环标号 / 延时(ms)
  - **列表2 · 接收自动应答**：名称 / HEX 勾选 / 期待接收指令 / 接收后自动发送指令 / 循环标号 / 延时(ms)
- **数据本地化**：两个列表与界面设置分别保存为软件所在目录下的 JSON 文件

---

## 一、目录结构

```
SerialDebugTool/
├── SerialDebugTool.sln
├── README.md
├── src/SerialDebugTool/
│   ├── SerialDebugTool.csproj      # net8.0 + Avalonia 11.2.1 + CommunityToolkit.Mvvm
│   ├── Program.cs                  # 入口，注册 GB2312/GBK 代码页
│   ├── App.axaml / App.axaml.cs    # 应用与主题（Fluent + DataGrid），退出时落盘
│   ├── Models/
│   │   ├── LogEntry.cs             # 日志条目（TX/RX/SYS/ERR + 时间 + 内容）
│   │   └── SerialPortConfig.cs     # 串口参数（可持久化）
│   ├── Services/
│   │   ├── SerialPortService.cs    # 串口打开/关闭/收发（后台异步读流）
│   │   ├── HexUtility.cs           # Hex↔文本转换、转义、子串匹配
│   │   └── JsonStorageService.cs   # 软件目录下的 JSON 读写（原子写 + 坏文件备份）
│   ├── ViewModels/
│   │   ├── MainWindowViewModel.cs  # 主 VM：收发引擎、循环发送、自动应答、持久化
│   │   ├── SendCommandItem.cs      # 列表1 行模型
│   │   ├── ReceiveCommandItem.cs   # 列表2 行模型
│   │   └── UiSettings.cs           # 界面设置 POCO（对应 settings.json）
│   ├── Converters/ValueConverters.cs
│   └── Views/
│       ├── MainWindow.axaml        # 左右布局 + 双 DataGrid
│       └── MainWindow.axaml.cs     # 仅做自动滚动与 Host 注入，无业务逻辑
└── tests/
    ├── device.py                   # 虚拟串口设备模拟器（pty，用于端到端测试）
    └── SmokeTest/                  # 无界面冒烟测试（Avalonia.Headless）
```

MVVM 边界：`Views` 只做界面与自动滚动；所有业务逻辑（串口、循环、匹配、持久化）都在 `ViewModels` / `Services`，可脱离 UI 测试。

---

## 二、构建与运行

前置：[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
cd src/SerialDebugTool
dotnet run -c Release          # 直接运行
dotnet publish -c Release -r win-x64 --self-contained false   # 发布 Windows 版
```

> Windows 上串口号形如 `COM3`；Linux 上为 `/dev/ttyUSB0`、`/dev/ttyACM0`、`/dev/pts/N`。
> 点击「刷新」可重新枚举串口。

---

## 三、字段与行为说明

### 循环标号（两个列表与手动发送区语义一致）

| 取值 | 行为 |
| --- | --- |
| `0` | **不循环**，只发送一次 |
| `N > 0` | 循环发送 N 次，每次间隔「延时」毫秒 |
| `-1` | 无限循环，直到点击「停止」/「全部停止」或关闭串口 |

「延时」= 相邻两次发送之间的间隔时间（毫秒）。

### 列表1 · 主动发送指令

- `启用`：勾选后「运行勾选项」会批量启动这些指令
- `HEX`：勾选表示「指令」按 16 进制解析（支持 `AA 01`、`AA01`、`0xAA,0xBB`、`aa-bb` 等写法）；不勾选按当前编码当文本发送，支持 `\r` `\n` `\t` `\0` 转义
- 每行右侧「运行 / 停止」可单独控制，`已发` 列显示累计发送次数

### 列表2 · 接收自动应答

- `HEX`：勾选后「期待接收指令」与「接收后自动发送指令」都按 16 进制解析
- `期待接收指令`：收到匹配数据即触发
- `接收后自动发送指令`：命中后发出的内容，同样受「循环标号 / 延时」控制
- **匹配方式**（列表2 头部下拉）：
  - `包含匹配`：在最近 8KB 的接收缓冲里查找该序列（应对分包到达）
  - `完全匹配`：要求单次收到的数据帧与期待指令完全一致
- **防重复触发**：命中后清空接收缓冲，同一条规则带 20ms 防抖，避免应答风暴
- `命中` 列显示触发次数，「命中清零」可复位；「测试」按钮可不等硬件直接验证应答内容

---

## 四、本地化 JSON（保存在软件所在目录）

配置文件位于可执行文件同级目录（`AppContext.BaseDirectory`），UTF-8、带缩进、中文不转义，可直接手改：

| 文件 | 内容 |
| --- | --- |
| `settings.json` | 串口参数、Hex 显示、时间显示、过滤条件、面板显隐、匹配方式等 |
| `send_commands.json` | 列表1 全部行 |
| `receive_commands.json` | 列表2 全部行 |

保存时机：任何改动后 **600ms 防抖自动保存**，窗口关闭 / 程序退出时再同步保存一次。
写入方式为「临时文件 → 备份旧文件为 `.bak` → 替换」，避免掉电损坏；若 JSON 被改坏，程序不会崩溃，会把坏文件另存为 `*.corrupt-时间戳` 并使用默认值启动。

`send_commands.json` 示例：

```json
[
  {
    "Name": "查询版本",
    "IsHex": true,
    "Command": "AA 01 00 55",
    "LoopCount": 0,
    "DelayMs": 500,
    "IsEnabled": true
  },
  {
    "Name": "心跳(循环3次)",
    "IsHex": true,
    "Command": "AA 02 00 55",
    "LoopCount": 3,
    "DelayMs": 1000,
    "IsEnabled": true
  }
]
```

`receive_commands.json` 示例：

```json
[
  {
    "Name": "版本应答",
    "IsHex": true,
    "Expected": "AA 01",
    "Response": "AA 81 00 55",
    "LoopCount": 0,
    "DelayMs": 100,
    "IsEnabled": true
  }
]
```

> `IsRunning` / `SentCount` / `HitCount` 属于运行时状态，已用 `[JsonIgnore]` 排除，不会写进文件。

---

## 五、界面操作

- **右侧整栏隐藏**：左右分区之间的竖条上点 `◀` 折叠、`▶` 展开
- **单个列表隐藏**：各列表头部的「隐藏列表 / 显示列表」按钮；隐藏后仅保留头部，另一列表自动占满
- **日志**：`Hex 显示` 同时作用于收发两个方向；`显示时间` 控制时间戳；可按 发送 / 接收 / 系统 分类过滤，也支持关键字过滤
- **日志上限**：默认保留 5000 条（`settings.json` 中 `MaxLogLines` 可调），超出自动丢弃最旧的，避免长时间跑爆内存
- **保存日志**：导出为 txt/log，含完整时间戳与方向标记

---

## 六、验证方式

仓库自带无界面冒烟测试（`Avalonia.Headless`），配合 `tests/device.py` 创建的虚拟串口对（pty）做端到端验证，覆盖：XAML 加载、Hex 解析、JSON 落盘与回读、面板显隐、串口打开 / 发送 / 接收 / 自动应答 / 关闭。

```bash
# 1) 启动虚拟串口设备（Linux / macOS，需要 python3）
python3 tests/device.py &          # 输出 slave 路径，如 /dev/pts/0
# 2) 运行冒烟测试
cd tests/SmokeTest && dotnet run -c Release
```

最近一次运行结果：**20 项全部 PASS**。其中「应答指令 BB 22 真实发出到串口」由设备侧独立记录的字节流证实：

```
AA 01 00 55      ← 工具发出的原始指令
BB 22            ← 命中 AA 81 后自动发出的应答
```

---

## 七、已知边界

- 串口读取使用 `BaseStream.ReadAsync` 后台读流（而非 `DataReceived` 事件），三平台行为一致；设备被拔出时会上报「串口连接已断开」并自动置为未打开状态。
- 部分虚拟串口 / USB 转串口不支持 DTR、RTS 控制线，程序已做容错，设置失败不影响打开。
- Avalonia 的构建任务需要在支持随机偏移写入的常规文件系统上执行；网络挂载盘（如对象存储挂载目录）会在 `GenerateAvaloniaResourcesTask` 阶段报 `IOException: Invalid argument`，把工程放到本地磁盘即可。
