# GameSir 启明星2 电量托盘

在 Windows 通知区域（系统托盘）实时显示 **盖世小鸡启明星2（GameSir Nova 2）** 2.4G 无线手柄电量的托盘小工具，支持同时连接多个手柄。

A tiny Windows tray app that shows the battery level of **GameSir Nova 2** controllers connected through the 2.4 GHz dongle. Multiple controllers are supported.

## 特性

- 每个手柄一个独立托盘图标，鼠标悬停显示 `名字 · 电量%`
- 多手柄：按接收器上报的 MAC 地址区分，拔插后仍能对应
- 断连 / 手柄关机时，图标自动变为灰色空电池加斜杠
- 双击图标立即刷新，默认每 30 秒轮询一次
- 右键可重命名手柄，配置持久化到 `%AppData%\GameSirBatteryTray\devices.json`
- 可选开机自启（写入 `HKCU\...\Run`）

## 工作原理

2.4G 模式下没有任何标准 API 能拿到第三方手柄电量：

- `XInput` 只有按键与摇杆；`XInputGetBatteryInformation` 对该适配器返回 `BATTERY_TYPE_WIRED` / `FULL`，无意义
- HID 报告描述符里没有声明标准的 Battery Strength（Usage Page `0x06` / Usage `0x20`），因此 `Windows.Devices.Power` 也读不到

实际电量走厂商私有 HID 通道。接收器枚举为复合设备 `VID 0x3537 / PID 0x1098`，其中 `MI_02` 是一个厂商自定义集合（UsagePage `0xFF7A`，input / output 各 65 字节）。

**请求**（写入 65 字节，`00 01 01 00` 之后补零）：

```
00 01 01 00 00 00 ...
```

**响应**（65 字节）：

| 偏移 | 含义 |
| --- | --- |
| 0 | report id，固定 `0x00` |
| 1..6 | 接收器 MAC 地址 |
| 11..12 | dongle 固件版本（如 `28 00`） |
| 19 | **电量百分比**（0–100） |

例如 `00 | 01 F4 96 50 03 25 | ... | 49` → 电量 `0x49` = 73%。

这些字段是通过分析官方客户端（GameSir Connect / `zw-lib.dll`）的 HID 通信日志并与界面读数比对确认的，`tools/probe3_battery.py` 可复现该命令。

## 构建

需要 .NET 9 SDK。

```bash
dotnet build GamesirBatteryTray -c Release
dotnet run --project GamesirBatteryTray            # 直接运行（托盘）
dotnet run --project GamesirBatteryTray -- --once  # 命令行检查电量，不启动托盘

# 发布为单文件
dotnet publish GamesirBatteryTray -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true -o publish
```

程序为纯用户态实现，使用 `setupapi.dll` / `hid.dll` / `kernel32.dll` 的 P/Invoke，无任何 NuGet 依赖，也不需要安装驱动。

## 使用

1. 插上 2.4G 接收器并开启手柄
2. 运行 `GamesirBatteryTray.exe`
3. 图标会出现在任务栏通知区域

Windows 11 默认会把新图标收进折叠区，点击右下角 `∧` 查看；想常驻显示可前往
「设置 → 个性化 → 任务栏 → 其他系统托盘图标」把 `GamesirBatteryTray` 打开。

右键菜单：电量、设备 MAC、立即刷新、重命名、开机自动启动、退出。双击图标立即刷新。

## 调试脚本

`tools/` 下的 Python 脚本用于确认设备与协议（仅标准库，需要手柄已连接）：

| 脚本 | 用途 |
| --- | --- |
| `probe_gamepad.py` | 枚举 HID 手柄接口，输出 VID/PID、Usage、报告长度、报告描述符，并检查 XInput 电池信息 |
| `probe2_vendor.py` | 列出全部 HID 集合，被动监听厂商通道是否主动上报 |
| `probe3_battery.py` | 复现 device-info 命令并打印原始响应 |

## 兼容性

仅在 **启明星2（Nova 2）+ 手柄固件 8200 / dongle 2800** 上验证。私有协议的字段偏移可能随固件版本变化，其他 GameSir 型号需要先跑 `probe3_battery.py` 确认响应结构。

## 免责声明

非官方项目，与 GameSir 无关。协议来自对官方客户端与本机设备通信的观察，仅供学习研究使用。

## License

[MIT](LICENSE)
