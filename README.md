# PCL-OpenClaw-Launcher

基于 **PCL（Plain Craft Launcher）** 界面控件改造的 **OpenClaw** 本地网关启动器。
用 C# / VB.NET（WPF，.NET 10）编写，用于在本机一键启动、管理 OpenClaw 网关及其版本、插件、技能与整合包。

> Author: Twinight_Miner
> License: PCL License（见 `LICENCE`）

---

## 功能特性

- **启动总览**：一键启动 / 停止 OpenClaw 网关（`node openclaw.mjs gateway --port 3000 --allow-unconfigured`），并自动打开控制台 `http://localhost:3000`。
- **版本与实例**：管理多个 OpenClaw 版本与运行实例。**每个实例对应一个 OpenClaw 版本**；把实例改名后再创建，即可让同一版本并存多个实例（如同 PCL 的多个存档）。
- **实例设置（每个实例独立）**：不再是「设置」的子页，而是每个实例都拥有自己的设置入口——在「版本与实例」列表点「设置」即可进入该实例的名称/版本、复制实例、运行环境与扩展资源管理；插件、Skill、整合包都只作用于当前实例。
- **新实例默认插件集**：下载/创建新实例时只携带“必要插件”，其余由用户自行安装或制作；可一键“照搬当前实例”采集默认集。
- **插件市场 / Skill 库 / 整合包（位于「实例」分组）**：列出官方内置与自定义（extensions 目录）插件与技能，每项带「蓝=启用 / 灰=禁用」开关与删除按钮，支持按名称/状态/来源筛选、安装、诊断；启用、禁用、删除都只影响当前实例。
- **软件连接 / 整合包**：管理外部软件接入与整合包安装。
- **操作日志**：记录启动器与网关的关键操作。
- **升级不丢设置（v0.8.0 起）**：用户数据统一放在 `%LOCALAPPDATA%\OCL`，升级/卸载都不丢设置、背景图与歌单；首次运行会自动继承旧版本数据目录，并把背景媒体、音乐复制一份到新目录。
- **快捷方式始终指向最新版（v0.8.0 起）**：桌面快捷方式固定名为 `OCL.lnk`，每次启动自动重写为当前版本，不会留下指向旧版本的僵尸链接。
- **打赏入口（v0.8.0）**：「关于」页新增蓝色「打赏 OCL 作者」按钮，点击打开本地打赏页，内含微信 / 支付宝收款码。
- **快捷方式图标（v0.8.1）**：「设置 → 快捷方式图标」可选桌面 OCL.lnk 使用 OCL 六边形或 OpenClaw 角色图标，确定后即时更换，无需重启。
- **运行环境自检（v0.8.1）**：启动时自动探测满足 OpenClaw 要求的 Node.js（≥22.22.3 或 ≥24.15.0 等），避免 Node 版本不符导致插件 / 技能列表为空。
- **连接页面重做（v0.8.2）**：分为「插件链接」（Wallpaper Engine 走插件直连）与「API 密钥链接（Token）」（微信 / QQ / Telegram 已连接，Signal / Line / Discord 可连接但尚未接入）；两类均带「检测连接」按钮与绿/黄/红/灰状态指示灯。
- **Wallpaper 插件可见性修复（v0.8.2）**：受管实例原本在插件列表里看不到自带的 wallpaper-engine；现已在创建/复制实例时自动以目录连接挂入实例扩展目录并写入启用配置，也可在「软件连接 → Wallpaper Engine」卡片点「修复插件」手动修复。
- **Wallpaper 卡顿修复（v0.3.1）**：OpenClaw 由空闲转入思考/工作态时控制台动态壁纸会卡住，已通过把视频层提升为 GPU 合成层并自动恢复暂停的视频修复。
- **导航精简 +「关于」归位（v0.8.3）**：顶端导航栏去掉「实例」标签，恢复为 **启动 / 下载 / 连接 / 设置 / 关于**；「关于」重新成为独立顶端导航项（0.8.2 曾误并入「设置」，本版还原），其组内仍含「操作日志」。
- **实例设置入口收敛（v0.8.3）**：实例设置只保留**启动页侧栏**这一个入口（作用于当前所选实例），打开后顶端导航栏照常显示。
- **插件管理重做为横向条目（v0.8.4）**：参照 PCL 的「实例 Mod 管理」，每个插件一行——左侧状态指示灯（绿=启用 / 灰=禁用 / 红=异常）、中间名称与「状态 · 来源」，右侧开关与删除按钮，不再用竖排卡片。
- **插件开关 / 删除真正写入（v0.8.4）**：开关直接调用 `openclaw plugins enable|disable <id>` 并写回实例配置 `plugins.entries.<id>.enabled`；删除先由 OpenClaw 卸载，若插件仍在列表中（手动放入的插件没有安装记录，或全局扩展被再次发现），按 `plugins list` 报告的 `rootDir` 兜底移除目录——普通目录移入启动器回收目录 `removed-plugins/` 可手动移回，目录连接点只删链接不会误删源目录——并清理配置条目。
- **修复「共 0 个」误导（v0.8.4）**：进入插件 / 实例设置页时若列表尚未加载会自动拉取，加载期间显示「正在加载插件列表…」而不是空白的「共 0 个」；同时避免导航时重复刷新导致列表反复抖动。
- **Skill 管理并入同一套横向条目（v0.8.5）**：参照 PCL「资源包管理」，Skill 与插件共用同一种行版式——指示灯 + 名称/状态/来源 ‖ 开关 + 删除；「Skill 库」与「实例设置 → Skill（本实例）」两处都是横排。
- **Skill 删除边界明确（v0.8.5）**：独立安装的 Skill（工作区 `workspace/skills` 或共享 `skills` 目录）删除时移入启动器回收目录 `removed-skills/` 可手动移回；内置、插件附带、链接目录会给出明确原因并引导用开关禁用或去卸载所属插件。
- **实例独立性自检（v0.8.5）**：「实例设置 → 实例独立性」可一键逐个实例查询插件 / Skill 数量与状态目录；若有两个实例共用同一状态目录会直接告警（这种情况插件与 Skill 会互相影响）。
- **一键从其他实例导入实例设置（v0.8.5）**：同一入口可选来源实例，把它的插件启停与插件配置、Skill 启停导入当前实例，并按来源记录补装缺少的插件；端口、令牌与账号凭据不会被复制。
- **安装目录改到 E 盘（v0.8.5）**：安装脚本 `DefaultDirName` 由 `%LOCALAPPDATA%\Programs\OCL` 改为 `E:\OCL`，不再往 C 盘塞程序文件（用户数据仍在 `%LOCALAPPDATA%\OCL`）。
- **插件 / Skill 升级为「实例设置」专属导航（v0.8.6）**：插件与 Skill 是每个实例一份的东西，进入实例设置后顶端分区导航换成「实例设置」一项（红色高亮），退出即还原为 启动 / 下载 / 连接 / 设置 / 关于；左侧该栏分「实例」（概览 / 运行环境 / 导入实例设置）与「扩展资源」（插件 / Skill / 整合包）两组。
- **新增插件市场（v0.8.6）**：「下载」页拆成 自动安装 / 本地实例 / 插件市场；按关键词搜索 ClawHub 仓库，结果显示名称、作者、版本、下载量与官方标记，点每行红色「＋」选实例后一键安装。
- **插件 / Skill 列表点开即全显（v0.8.6）**：列表结果写入 `%LOCALAPPDATA%\OCL\cache\lists-<实例ID>.json` 快照，进页先用快照瞬间铺满再后台刷新，不再「空白等扫描」；快照只补空列表，不覆盖刚刷新的数据。
- **分区可收起（v0.8.6）**：插件页与 Skill 页的列表区加了红色三角收起 / 展开按钮，状态跨页保持。
- **实例独立性改为底层自动（v0.8.6）**：去掉手动检查按钮，启动与保存运行设置时自动核对各实例状态目录，两个实例共用同一目录会直接写进操作日志。
- **整合包只做导出（v0.8.6）**：取消导入整合包；新增「预览可导出的整合包」——先列出全部插件与 Skill（默认勾选已启用项）再导出，能确定来源的记安装引用，不能还原的列入「待补充项」。
- **运行环境 / 导入实例设置独立成页（v0.8.6）**：「运行环境与配置」与「从其他实例导入设置」从实例设置页拆出来，各自成为左侧导航项。

## 目录结构

```
PCL-OpenClaw-Launcher/
├── src/                      # 源码（.NET 10 解决方案）
│   ├── Launcher/             # 主启动器（C# / XAML）
│   ├── PclControls/          # 由 PCL 移植的 WPF 控件（VB.NET）
│   ├── Tests/                # 测试项目
│   └── NuGet.Config
├── app-v0.2/ ~ app-v0.8.6/   # 各版本已编译发布（可直接运行）
├── installers/<版本>/         # 各版本 Inno Setup 安装包
├── installer/                # 安装脚本（模板 ocl.iss.tmpl、生成器 gen-iss.py、各版本 ocl-*.iss）
├── plugins/wallpaper-engine/ # OpenClaw 壁纸插件
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
| v0.8 | `app-v0.8.0/` |
| v0.8.1 | `app-v0.8.1/` |
| v0.8.2 | `app-v0.8.2/` |
| v0.8.3 | `app-v0.8.3/` |
| v0.8.4 | `app-v0.8.4/` |
| v0.8.5 | `app-v0.8.5/` |
| v0.8.6 | `app-v0.8.6/` |

每个目录均含可直接运行的 `PCL-OpenClaw-Launcher.exe`（随附 `PclControls.dll` 等依赖）。

### Windows 安装包

使用 [Inno Setup](https://jrsoftware.org/isinfo.php) 按用户级（无需管理员）安装。
**v0.8.0–v0.8.4 安装目录为 `%LOCALAPPDATA%\Programs\OCL`；v0.8.5 起改为 `E:\OCL`**（不再带版本号），配合固定 AppId，
新版本会**原地覆盖升级**并自动关闭正在运行的 OCL，不会并存多个安装。

| 版本 | 安装包 |
| --- | --- |
| v0.5 | `OCL-0.5.0-Setup.exe` |
| v0.6 | `OCL-0.6.0-Setup.exe` |
| v0.7 | `OCL-0.7.0-Setup.exe` |
| v0.8 | `OCL-0.8.0-Setup.exe` |
| v0.8.1 | `OCL-0.8.1-Setup.exe` |
| v0.8.2 | `OCL-0.8.2-Setup.exe` |
| v0.8.3 | `OCL-0.8.3-Setup.exe` |
| v0.8.4 | `OCL-0.8.4-Setup.exe` |
| v0.8.5 | `OCL-0.8.5-Setup.exe` |
| v0.8.6 | `OCL-0.8.6-Setup.exe` |

安装包只包含可执行文件、依赖、图标与打赏页资源，**不含**任何个人配置、密钥或源码。脚本见 `installer/ocl.iss.tmpl`（模板）与 `installer/ocl-*.iss`（各版本），生成脚本 `installer/gen-iss.py`。

> **升级约定（0.8.0 起固定）**：固定 AppId `{8E1C3A72-0F5B-4D31-9C10-0A1F2B3C4D70}`、**固定 `DefaultDirName=E:\OCL`（0.8.5 起）**、固定快捷方式名 `OCL`、**`UsePreviousAppDir=no`**。
> 后续版本不要再改这几项，否则会变成并存安装并留下僵尸快捷方式。
> ⚠️ `UsePreviousAppDir` 若保持默认 `yes`，Inno 会沿用注册表里「上次安装目录」的残留记录，把新版本装回任意旧位置（实测曾把 0.8.0 装进开发目录而不是 `Programs\OCL`）。

## 打赏

「关于」页的蓝色按钮 **打赏 OCL 作者** 会打开本地页面 `Assets/donate/index.html`，内含作者的微信与支付宝收款码；
旁边的 **打赏 PCL 原作者** 会跳转 PCL 作者的[爱发电](https://meloong.com/afd/a/LTCat)页面。
打赏完全自愿，不影响任何功能。

## OpenClaw 壁纸插件

`plugins/wallpaper-engine`（Wallpaper Engine，v0.3.1）把本机 Wallpaper Engine 库中的壁纸，或任意本地图片 / GIF / 视频文件，作为 OpenClaw 网页控制台背景，并可自定义控制台字体颜色与字号。受管实例可在「软件连接 → Wallpaper Engine」卡片点「修复插件」自动安装到当前实例。

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
