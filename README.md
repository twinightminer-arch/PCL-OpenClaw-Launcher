# PCL-OpenClaw-Launcher · OCL 0.6.0

基于 PCL 控件风格的 Windows OpenClaw 启动器，使用 C# / VB.NET、WPF 和 .NET 10。
作者 Twinight_Miner；PCL 原作者龙腾猫跃。许可与来源见 LICENCE、源码来源.md。

## 0.6.0

- 插件扫描允许较长启动时间，失败原因直接显示，刷新保留列表筛选与选择。
- 连接页为发现的每个渠道及账号提供检测区域、状态灯和单独重测；不再用无关的服务状态检查阻断渠道 RPC。
- 绿色表示已连接或探测通过，黄色表示未验证，红色表示连接异常，灰色表示未配置或未安装。
- Wallpaper Engine 插件 0.2.0：网页右下角“壁纸”打开本地库，可搜索、切换背景、调整不透明度或关闭。
- 图片、GIF、MP4、WebM 保留原格式；Wallpaper Engine 原生 scene/pkg 使用预览图，浏览器不能运行其原生场景引擎。
- 启动区及开场动画中的 OCL、OpenClaw 图标等尺寸水平排列。

## 使用

安装 .NET Desktop Runtime 10 及当前 OpenClaw 所需的 Node.js，运行 `app-v0.6/PCL-OpenClaw-Launcher.exe`。
`data-location.txt` 指向 `../app/Data`，让各版本共享启动器设置。发布包不含账号、实例数据、模型密钥或聊天记录。
启动器按所选实例的实际 OpenClaw 配置运行；地址、端口和鉴权由 OpenClaw 决定，不预置通用令牌。

壁纸插件安装与配置见 [plugins/wallpaper-engine/README.md](plugins/wallpaper-engine/README.md)。
未配置账号、网络不可达或适配器本身启动失败会如实显示；状态灯不会把这些情况伪装为连接成功。

## 构建与验证

```powershell
./build.ps1
dotnet run --project src/Tests/Tests.csproj -c Release -- node.exe
```

构建产物位于 `app/`。插件可在其目录执行 `npm install`、`npm run build`、`npm test`。
本次验证包含 37 项逻辑检查、WPF 界面冒烟检查，以及真实网页读取 45 项壁纸并切换 MP4 的检查。
图标由用户提供的参考图通过 ImageGen 提取透明背景；源图片保存在 Assets 中。
