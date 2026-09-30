# Wallpaper Engine 0.3.0

将本机 Wallpaper Engine 库中的壁纸，或任意本地图片 / GIF / 视频文件，作为 OpenClaw 网页控制台背景，并可自定义控制台字体颜色与字号。无需额外 HTTP 服务，不读取浏览器令牌；选择器复用已认证的网关连接。

## 新增（0.3.0）

- `wallpaper_ui_import` / RPC `wallpaper.import`：直接把库外的本地媒体文件（绝对路径）复制进控制台资源目录并应用，支持 png/jpg/webp/bmp/gif/mp4/webm/mov/mkv/avi。
- `wallpaper_ui_config` 新增字体项：`fontCustom`（总开关）、`fontColor`、`fontSize`(10–28)、`fontWeight`、`fontFamily`、`caretColor`；控制台右下角“壁纸”面板提供颜色选择器与字号滑块。

## 安装

从本仓库的 `plugins/wallpaper-engine` 安装：

```powershell
openclaw plugins install ./plugins/wallpaper-engine --force
```

在所用实例的 OpenClaw 配置中设置插件：

```json
{
  "plugins": {
    "entries": {
      "wallpaper-engine": {
        "enabled": true,
        "config": {
          "libraryRoot": "D:/STEAM/steamapps/workshop/content/431960",
          "controlUiRoot": "E:/openclaw/dist/control-ui"
        }
      }
    }
  }
}
```

将两个路径改为你的实际目录。重启所选实例的网关，再刷新控制台；右下角“壁纸”可浏览和切换。构建好的 dist 已随附，安装不需要开发依赖。
插件启动时复制浏览器脚本并幂等加入 index.html，首次修改前保留 `.bak-wallpaper`。OpenClaw 更新后，重启插件会重新安装脚本。
网页资源存放在指定控制台目录的 wallpaper/ 下。只会复制被选中的壁纸媒体；这些媒体会随控制台静态资源提供，请使用你愿意作为控制台背景的内容。

## 格式与限制

图片、GIF 和浏览器支持的 MP4/WebM 可直接显示。原生 scene/pkg 使用项目预览，不能在浏览器重现 Wallpaper Engine 的原生特效。
视频静音循环，遵循浏览器“减少动态效果”偏好；后台页暂停播放。其他动画格式提取静帧时需配置 `ffmpegPath`。
此集成已在 OpenClaw 2026.7.2 的 Light DOM 控制台上验证；若后续 OpenClaw 更换前端接口，需要更新 `ui/wallpaper.js` 中的连接桥接。

## 工具与接口

工具：wallpaper_list、wallpaper_search、wallpaper_info、wallpaper_ui_set、wallpaper_ui_config、wallpaper_ui_off、wallpaper_ui_status、wallpaper_desktop_set。
网页 RPC：wallpaper.list / status 需要 operator.read；wallpaper.set / configure / off 需要 operator.write。
桌面静态壁纸工具独立于网页背景，不会随网页选择自动改变 Windows 桌面。

## 开发

```powershell
npm install
npm run build
npm test
```

回归检查覆盖真实注册、配置隔离、图片路径越界防护、GIF 和视频保留、脚本幂等安装与 RPC 参数验证。
