---
name: wallpaper
description: 从本机 Wallpaper Engine 库选择，或直接导入任意本地图片/GIF/视频，作为 OpenClaw 网页控制台背景；并可调整控制台字体颜色与字号。当用户要求切换网页背景、导入本地媒体、寻找壁纸或调整背景显示与字体时使用。
---

# Wallpaper Engine

默认操作 OpenClaw 网页背景。只有用户明确要求修改 Windows 桌面时才调用 wallpaper_desktop_set。

1. 使用 wallpaper_list 或 wallpaper_search 查询当前实例配置的真实壁纸库，不假定目录、数量或标题。
2. 使用 wallpaper_info 检查选中项目；用 wallpaper_ui_set 的 query 参数传入 ID。
3. 库外的本地文件用 wallpaper_ui_import，path 传本机绝对路径（图片/GIF/mp4/webm/mov/mkv/avi）。插件会把文件复制进控制台资源目录再应用，不依赖壁纸库目录。
4. 根据返回值确认 applied。MP4/WebM 和 GIF 保持动画，原生 scene/pkg 使用预览素材；不要声称浏览器运行了 Wallpaper Engine 的场景引擎。
5. 通过 wallpaper_ui_config 调整 opacity、blur、scrim、fit、translucentApp、appAlpha；控制台字体用 fontCustom（总开关）+ fontColor、fontSize(10-28)、fontWeight、fontFamily、caretColor。数值须符合工具参数范围。
6. wallpaper_ui_status 返回配置与库数量；wallpaper_ui_off 关闭背景并保留最后选择（不影响字体设置）。

首次安装后需要重启所选实例网关并刷新网页。插件启动时自动安装网页脚本；日常切换约四秒内生效，无需重启网关。
控制台右下角“壁纸”面板也能直接浏览和切换。连接失败时先检查网关连接和插件状态；使用实例注入的配置，不读取其他用户或实例的配置覆盖它。
网页无法显示背景时检查 controlUiRoot 是否为当前实际服务目录，以及 translucentApp 是否开启。其他动画格式需要 ffmpeg 才能提取静帧。
