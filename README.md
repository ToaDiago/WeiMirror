# 微映 WeiMirror

微映是一款运行在 Windows 上的 iPad、iPhone 投屏接收程序，支持 USB 数据线和无线 AirPlay。它提供独立的投屏窗口，连接面板用于选择设备、管理画质和查看接收状态。

## 功能

- 支持 USB 投屏及同一台设备断线后自动重连。
- 支持 AirPlay 无线屏幕镜像，可选择 1080p60、720p30、540p30 和试验性 720p60 档位。
- 投屏窗口可置顶、全屏、跟随设备横竖屏调整，并记住上次窗口位置。
- 可选择是否在电脑播放声音，也可以将当前画面保存为原分辨率 PNG。
- 提供连接阶段与常见故障提示，以及用于记录端到端延迟的离线测试页。

画面窗口按 `F11` 切换全屏，`Esc` 退出全屏，`Ctrl+S` 截图，`Ctrl+R` 切换跟随画面比例。

## 使用

在仓库的 Releases 页面下载 `WeiMirror.exe`。

需要 Windows 10 或 11 x64，以及 iPad 或 iPhone。运行 `WeiMirror.exe` 后，首次使用时按提示准备投屏组件，程序支持在线下载、导入官方便携 ZIP 或选择已有的组件目录。组件准备窗口和主界面会显示组件所在位置。

- **无线投屏**：让电脑和设备连接同一局域网，在微映中开启 AirPlay 接收，然后在设备的「屏幕镜像」中选择接收端。
- **USB 投屏**：用支持数据传输的线连接并解锁设备，按需在设备上确认信任，选择设备后开始投屏。程序提供 USB 驱动管理入口。

默认无线档位为 1080p60。720p60 为试验功能，实际画面尺寸、帧率和延迟取决于发送设备和网络环境。项目尚未完成真实 iPad 的端到端延迟验证，欢迎使用「工具 / 关于」中的测试页记录结果。电脑音频开关控制接收端播放，设备端的声音输出由系统和应用决定。

## 从源码构建

源码构建需要 Windows x64、PowerShell 7 和 .NET Framework 4.8。进入项目目录后运行：

```powershell
./build.ps1
```

生成的程序位于 `bin/WeiMirror.exe`，完整源码归档位于 `bin/WeiMirror.Source.zip`，也可在程序中导出。组件位置、官方组件版本和 SHA-256 等详细信息见 [BUILD.md](BUILD.md)。

## 来源与许可证

微映使用 [iPhoneMirror v1.8.3](https://github.com/RayrenSX/iPhoneMirror/tree/v1.8.3) 的投屏组件。微映按 [GPL-3.0-only](LICENSE.txt) 授权，组件来源和第三方许可说明见 [NOTICE.txt](NOTICE.txt)。

AirPlay、iPad 和 iPhone 是 Apple 的商标。微映是独立项目。
