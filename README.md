# RazerDpiTool

一个不装雷云（Razer Synapse）也能改雷蛇鼠标 DPI 的小工具。

Windows · C# / WinForms · .NET 8 · 约 3200 行 · 单文件 exe

> ⚠️ **本项目为个人非官方作品，与 Razer Inc. 无任何关联，未获其授权或认可。**
> "Razer" 是其所有者的商标，此处仅用于说明本工具兼容的设备类型。
> 使用本工具修改硬件设置的风险由使用者自行承担，详见文末[免责声明](#免责声明)。

---

## 它解决什么问题

装了雷云只想改个 DPI，要装几百 MB 的驱动、注册账号、跑一堆后台服务，还要接受它把设置同步到云端。很多人其实只想做一件事：**把鼠标灵敏度从 1600 改成 800**。

这个工具就是那件事。绿色免安装，双击运行，读完直接写，写完自动读回来核对。

## 功能

- **读取当前 DPI** —— 单独「只读」按钮，不动任何设置
- **写入 DPI** —— 水平 X / 垂直 Y 可分别设置，也可联动
- **写入后自动回读核对** —— 只有鼠标亲口回读的值与目标一致才判定成功，不会「假装写成功」
- **可选写入板载存储** —— 开着就写进鼠标本体，拔插、换机器都不丢；关掉则只在本次生效
- **自动识别事务编号** —— 不同型号的事务编号不同，程序会挨个试出能回话的那个
- **原始报文打印** —— 排障时能看到收发的 91 字节原文
- **多接口枚举** —— 一只鼠标会被系统认成多个 HID 接口，程序会标出真正能收配置指令的那个

## 系统要求

| | |
|---|---|
| 操作系统 | Windows 10 / 11（x64） |
| 运行时 | **独立版**：无要求；**轻量版 / 源码**：需 .NET 8 桌面运行时 |
| 权限 | 普通用户即可，**不需要管理员权限** |
| 其他 | 运行前请退出雷云 Synapse，它会抢占鼠标的配置接口 |

## 下载即用

到 [Releases](https://github.com/HiroZero/RazerDpiTool/releases) 下载单个文件 **`RazerDpiTool.exe`**（约 155 MB）：自带 .NET 运行时，拷到任何 Windows 10 / 11 电脑双击即用，不需要预装任何环境。

> 首次启动慢 1–2 秒（要解压内置运行时）；未做代码签名，SmartScreen 或杀软可能提示，这是未签名小工具的常见情况。
>
> 若你本机已装 .NET 8 桌面运行时、想省掉打包运行时的体积，可自行[从源码构建](#从源码构建)打一个轻量版（约 515 KB）。该版本**不随官方 Release 分发**，仅面向已知环境的使用者。

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

```bash
git clone https://github.com/HiroZero/RazerDpiTool.git
cd RazerDpiTool
dotnet build -c Release
# 产物：bin/Release/net8.0-windows/RazerDpiTool.exe
```

打包成单文件：

```bash
# 轻量版（依赖目标机已装 .NET 8）→ 约 515 KB
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:DebugType=None -o out/lite

# 独立版（自带运行时）→ 约 155 MB
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None -o out/standalone
```

> 💡 想缩小独立版体积，追加 `-p:EnableCompressionInSingleFile=true`，可降到约 **68 MB**，代价是首次启动多花一两秒解压。
>
> `-p:DebugType=None` 用于省略 `.pdb`（发布产物不需要调试符号）。本地调试用 `dotnet build` 不受影响。

📦 发布到 GitHub Releases 的就是上方的「独立版」，文件名保持 **`RazerDpiTool.exe`**（与下载表、资产名一致，避免一个 Release 里两个同名 exe 冲突）。

也可以用 Visual Studio 2022 直接打开 `RazerDpiTool.csproj` 按 F5。

## 使用

1. **退出雷云** —— 右下角托盘图标右键退出
2. **选设备** —— 点设备栏右上角 `↻` 重新扫描，选带 ★ 的那一行
3. **定数值** —— 拖滑杆、点档位片，或直接键入 X / Y
4. **写入** —— 点橙色的「写入 DPI · 读回确认」，等第三步打勾

> 💡 **改之前先点一次「只读取当前值」，把原始 DPI 记下来**，随时能改回去。

拖不动、读不到，先看下面的[常见问题](#常见问题)，覆盖了「打不开接口」「鼠标没反应」「读回不一致」等常见情况。

## 工作原理

雷蛇没有公开协议，但社区早已逆向清楚。程序把一条命令拼成一封固定 **90 字节**的 Feature Report，Windows 还要求最前面多一个字节的报告编号，所以实际收发 **91 字节**：

```
偏移  内容
 0    报告编号（Windows 要求，填 0x00）
 1    事务编号（各型号不同）
 5    参数长度
 6    命令类别（DPI = 0x04）
 7    命令编号（0x05 写 / 0x85 读）
 8    存储方式（0x00 临时 / 0x01 板载）
 9-10  水平 X（大端）
11-12  垂直 Y（大端）
88    校验字节 = 第 2–87 字节逐个异或
```

程序只发两类命令：读 DPI、写 DPI。不刷固件、不改按键、不动灯光。

## 已知限制

- **只测过一只鼠标**：Razer DeathAdder Essential（PID `0x0098`）。协议本身在多个社区实现中通用，理论上覆盖大多数支持 HID 配置接口的型号，但未逐一验证
- 老型号 DPI 上限可能只有 16000，超出范围会写入失败并报「读回不一致」
- 极少数型号的配置接口被 Windows 独占，`CreateFile` 打不开时会明确报错
- 无线鼠标睡眠时收不到命令，需要先动一下鼠标唤醒
- 未实现：按键重映射、灯光、宏、轮询率

## 常见问题

| 现象 | 处理 |
|---|---|
| 下拉框是空的 | 鼠标没插好 / 没退出雷云 / 型号没暴露 HID 配置接口。换 USB 口，点设备栏 `↻` 重新扫描 |
| 「打不开这个接口」 | 雷云或别的程序占了配置接口。彻底退出雷云，或换列表里另一行接口；不要同时开两个本程序实例 |
| 「鼠标始终没有回答」 | ① 无线鼠标睡着了，**动一下鼠标马上重试**；② 手动把事务编号在 `1F / 3F / FF / 00` 之间换一个；③ 把「等待(毫秒)」调到 300–500 |
| 状态码 `0x04`（超时／没回应） | 多半是无线鼠标睡眠，或等待时间不够 |
| 状态码 `0x05`（不支持该命令） | 该型号用另一套 DPI 命令。打开「打印原始报文」，把日志贴到 Issue 求助 |
| 「写入后读回不一致」 | 数值超出该型号上限（老款常见 16000，新款 26000–35000），调小再试 |
| 命令成功但鼠标没变 | 选错了接口（其余接口也会回「收到」但不干活），换一行接口重试 |

更完整的排障思路见[工作原理](#工作原理)；仍解决不了，欢迎到仓库提 Issue，附上「打印原始报文」的日志。

## 协议参考与致谢

本项目的协议实现参考了以下开源项目的公开资料，**仅参考协议格式，未复制其代码**（许可证兼容性说明见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)）：

- [openrazer/openrazer](https://github.com/openrazer/openrazer) —— 报文结构、状态码、校验算法
- [meech-io/razer-cli](https://github.com/meech-io/razer-cli) —— DPI 命令编号与参数布局
- [MatthewSaintBull/freenapse](https://github.com/MatthewSaintBull/freenapse) —— Windows 上的 91 字节缓冲区与调用方式
- [geezmolycos/razerqdhid](https://github.com/geezmolycos/razerqdhid) —— 部分型号的接口划分与事务编号差异

## 第三方组件

| 组件 | 版本 | 许可证 |
|---|---|---|
| [HidSharp](https://software.seekye.com/hidsharp) | 2.6.4 | Apache-2.0 |
| .NET 8 运行时（仅独立版打包） | 8.0 | MIT |

Apache-2.0 要求随分发附带许可证副本，完整的第三方许可声明见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

## 许可证

[MIT](LICENSE) © 2026 HiroZero

## 免责声明

本软件按「原样」提供，不附带任何明示或暗示的担保。

- 这是**非官方**接入方式，Razer Inc. 不为这样配置过的设备提供技术支持
- 写入板载存储会**覆盖鼠标当前的 DPI 档位**，雷云里显示的数值可能不同步
- 修改硬件设置本身存在风险。首次使用请记录原始 DPI；若鼠标出现异常，拔插一次或重新打开雷云通常可恢复
- 作者不对因使用本软件造成的设备损坏、数据丢失或其他损失承担责任
