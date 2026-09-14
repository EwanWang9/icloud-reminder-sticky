# iCloud 提醒便利贴

[![CI](https://github.com/EwanWang9/icloud-reminder-sticky/actions/workflows/ci.yml/badge.svg)](https://github.com/EwanWang9/icloud-reminder-sticky/actions/workflows/ci.yml)

一个固定在 Windows 桌面右上角的轻量 iCloud 提醒事项桌面清单。后台页面来自 Apple 中国大陆官方站点 `https://www.icloud.com.cn/reminders/`，登录凭据由 WebView2 的独立浏览器资料目录管理，应用本身不会读取 Apple 密码。

> 本项目是非官方开源工具，与 Apple Inc. 无隶属或合作关系。它依赖 iCloud 网页结构，Apple 更新网页后可能需要同步更新适配。

## 下载

请从[最新版本下载页](https://github.com/EwanWang9/icloud-reminder-sticky/releases/latest)下载与电脑架构对应的压缩包：

- 大多数 Intel/AMD Windows 电脑：`win-x64`
- Windows ARM 设备：`win-arm64`

压缩包已经包含 .NET 8 运行时，解压后直接运行 `iCloudReminderSticky.exe`。系统仍需安装 Microsoft Edge WebView2 Runtime，Windows 10/11 通常已经包含。

## 使用

1. 双击 `iCloudReminderSticky.exe`。
2. 在 Apple 官方页面登录并完成双重认证。
3. 登录状态会保留，下次启动通常无需重新登录。

登录成功后，应用会读取当前列表中尚未完成的项目，并切换成完全透明的桌面清单。清单只显示完成圆圈和提醒文字；点击圆圈后，项目会以勾选、淡出和向右滑动动画移除，同时通过浏览器级鼠标输入完成 iCloud 网页中的对应项目。

网页模式顶部右侧按钮依次为：置顶、刷新、移回右上角、`返回清单`、退出。添加提醒后使用 `返回清单` 手动切回透明桌面模式。

- 双击标题栏：移回主显示器右上角。
- 托盘图标双击：显示或隐藏。
- 桌面清单不是置顶窗口，打开其他程序时会自然覆盖它。
- 桌面清单顶部可用于拖动，右侧半透明操作区中的 `＋ 添加` 打开 iCloud 新建提醒编辑器，`—` 隐藏清单。
- 桌面清单右键：添加提醒、立即同步、打开 iCloud 网页或隐藏清单。
- 托盘图标右键：切换桌面清单/网页模式、同步或调整网页缩放。
- 后台每 5 秒读取一次网页；桌面模式下每 20 秒主动刷新 iCloud 页面，以获取其他设备新增的项目。
- 完成操作会等待网页确认；若后台操作失败，项目会在最多 30 秒后恢复显示，避免误报完成。

## 隐私与数据

- 提醒事项数据直接由 iCloud 网页读取和保存。
- 本应用不包含 Apple API 密钥，也不会把账号数据发送给第三方服务器。
- WebView2 登录资料位于 `%LOCALAPPDATA%\iCloudReminderSticky\WebView2`。
- 窗口设置位于 `%LOCALAPPDATA%\iCloudReminderSticky\settings.json`。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

发布结果位于 `bin\Release\net8.0-windows\win-x64\publish`。

也可以一次生成 x64 和 ARM64 的自包含 ZIP：

```powershell
.\build-release.ps1 -Version 0.1.0
```

## 更新与兼容性

- 推送到 `main` 会自动执行编译检查。
- 推送 `v0.2.0` 之类的版本标签会自动构建 x64/ARM64 并创建 GitHub Release。
- Dependabot 每周检查 WebView2 SDK 与 GitHub Actions 更新。
- 如果 Apple 修改提醒事项页面结构，请按 `CONTRIBUTING.md` 中的兼容性清单更新并测试选择器。
