# PCL-OpenClaw-Launcher

基于 **PCL（Plain Craft Launcher）** 界面控件改造的 **OpenClaw** 本地网关启动器。
用 C# / VB.NET（WPF，.NET 10）编写，用于在本机一键启动、管理 OpenClaw 网关及其版本、插件、技能与整合包。

> Author: Twinight_Miner (QQ: 1879648323)
> License: PCL License（见 `LICENCE`）

---

## 功能特性

- **启动总览**：一键启动 / 停止 OpenClaw 网关（`node openclaw.mjs gateway --port 3000 --allow-unconfigured`），并自动打开控制台 `http://localhost:3000`。
- **版本与实例**：管理多个 OpenClaw 版本与运行实例。
- **插件管理 / Skills 管理**：浏览、安装、启用 OpenClaw 插件与技能。
- **软件连接 / 整合包**：管理外部软件接入与整合包安装。
- **操作日志**：记录启动器与网关的关键操作。

## 目录结构

```
PCL-OpenClaw-Launcher/
├── src/                      # 源码（.NET 10 解决方案）
│   ├── Launcher/             # 主启动器（C# / XAML）
│   ├── PclControls/          # 由 PCL 移植的 WPF 控件（VB.NET）
│   ├── Tests/                # 测试项目
│   └── NuGet.Config
├── app-v0.2/ ~ app-v0.5/     # 各版本已编译发布（可直接运行）
├── build.ps1                 # 构建脚本（需 .NET 10 SDK）
├── 使用说明.md                # 使用文档（中文）
├── 源码来源.md                # 源码溯源说明
└── LICENCE
```

## 构建

需要 **.NET 10 SDK**，无需额外 NuGet 源（见 `src/NuGet.Config`）：

```powershell
./build.ps1
```

产物位于 `src/Launcher/bin/Release/net10.0-windows/`。

## 使用

1. 确保你的 OpenClaw 位于默认目录（默认为 `E:\openclaw`，可通过启动器设置修改）。
2. 运行 `app-v0.x/PCL-OpenClaw-Launcher.exe`。
3. 在「启动总览」中点击启动；网关默认监听 `http://localhost:3000`，令牌见本地配置（默认 `my-secure-token`）。

> 详细使用说明见 [`使用说明.md`](使用说明.md)。

## 版本发布

| 版本 | 路径 |
| --- | --- |
| v0.2 | `app-v0.2/` |
| v0.3 | `app-v0.3/` |
| v0.4 | `app-v0.4/` |
| v0.5 | `app-v0.5/` |

每个目录均含可直接运行的 `PCL-OpenClaw-Launcher.exe`（随附 `PclControls.dll` 等依赖）。

## 源码来源

- **PCL（Plain Craft Launcher）** — 龙腾猫跃，github.com/Meloong-Git/PCL（UI 控件移植，commit `565b493`）。
- **DSHL 参考** — Loliyer520/DSHL-Deepseek-Harness-Launcher（闭源，仅作实现参考，commit `70bad4e`）。
- **OpenClaw** — 本地 `E:\openclaw`（package.json 2026.7.2）。

详见 [`源码来源.md`](源码来源.md)。

## 许可证

本项目基于 PCL 许可证发布，详见 [`LICENCE`](LICENCE)。
