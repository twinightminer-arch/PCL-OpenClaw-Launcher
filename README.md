# PCL-OpenClaw-Launcher

基于 **PCL（Plain Craft Launcher）** 界面控件改造的 **OpenClaw** 本地网关启动器。
用 C# / VB.NET（WPF，.NET 10）编写，用于在本机一键启动、管理 OpenClaw 网关及其版本、插件、技能与整合包。

> Author: Twinight_Miner
> License: PCL License（见 `LICENCE`）

---

## 功能特性

- **启动总览**：一键启动 / 停止 OpenClaw 网关（`node openclaw.mjs gateway --port 3000 --allow-unconfigured`），并自动打开控制台 `http://localhost:3000`。
- **版本与实例**：管理多个 OpenClaw 版本与运行实例。**每个实例对应一个 OpenClaw 版本**；把实例改名后再创建，即可让同一版本并存多个实例（如同 PCL 的多个存档）。
- **实例设置**：实例名称/版本、复制实例，以及本实例的插件、Skill、整合包入口（资源随实例走）。
- **新实例默认插件集**：下载/创建新实例时只携带“必要插件”，其余由用户自行安装或制作；可一键“照搬当前实例”采集默认集。
- **插件管理 / Skills 管理**：列出内置与自定义（extensions 目录）插件与技能，统计数量，支持启用 / 禁用 / 更新 / 卸载，并可一键打开插件或 Skills 目录。
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
| v0.6 | `app-v0.6.0/` |
| v0.7 | `app-v0.7.0/` |

每个目录均含可直接运行的 `PCL-OpenClaw-Launcher.exe`（随附 `PclControls.dll` 等依赖）。

### Windows 安装包

使用 [Inno Setup](https://jrsoftware.org/isinfo.php) 按用户级（无需管理员）安装，安装到 `%LOCALAPPDATA%\Programs\OCL-<版本>`：

| 版本 | 安装包 |
| --- | --- |
| v0.5 | `OCL-0.5.0-Setup.exe` |
| v0.6 | `OCL-0.6.0-Setup.exe` |
| v0.7 | `OCL-0.7.0-Setup.exe` |

安装包只包含可执行文件、依赖与图标资源，**不含**任何个人配置、密钥或源码。脚本见 `installer/ocl.iss.tmpl` 与 `installer/ocl-*.iss`。

## OpenClaw 壁纸插件

`plugins/wallpaper-engine`（Wallpaper Engine，v0.3.0）把本机 Wallpaper Engine 库中的壁纸，或任意本地图片 / GIF / 视频文件，作为 OpenClaw 网页控制台背景，并可自定义控制台字体颜色与字号。

```powershell
openclaw plugins install ./plugins/wallpaper-engine --force
```

关键能力：`wallpaper_ui_import` 直接导入库外本地媒体（绝对路径）；`wallpaper_ui_config` 的 `fontCustom / fontColor / fontSize / fontWeight / fontFamily / caretColor` 调整控制台排版。详见 [`plugins/wallpaper-engine/README.md`](plugins/wallpaper-engine/README.md)。

## 源码来源

- **PCL（Plain Craft Launcher）** — 龙腾猫跃，github.com/Meloong-Git/PCL（UI 控件移植，commit `565b493`）。
- **DSHL 参考** — Loliyer520/DSHL-Deepseek-Harness-Launcher（闭源，仅作实现参考，commit `70bad4e`）。
- **OpenClaw** — 本地 `E:\openclaw`（package.json 2026.7.2）。

详见 [`源码来源.md`](源码来源.md)。

## 许可证

本项目基于 PCL 许可证发布，详见 [`LICENCE`](LICENCE)。
