# 构建微映

微映是 Windows 10/11 x64 上的 iPad、iPhone 投屏接收端。本目录包含桌面程序的 C# 源码、构建脚本和许可证。

## 生成 EXE

1. 将完整源码压缩包解压到可写目录。
2. 在 Windows x64 上打开 PowerShell 7（`pwsh`），进入含有 `build.ps1` 的目录。脚本使用 PowerShell 7 自带的 Roslyn 编译器，以及本机 .NET Framework 4.8 的 x64 桌面程序集。
3. 运行下列命令。成功后生成 `bin/WeiMirror.exe` 和 `bin/WeiMirror.Source.zip`。这份源码归档也嵌在 EXE 中，可以在程序内导出。

```powershell
./build.ps1
```

## 准备投屏组件

下载和导入 ZIP 的组件保存在 `%LOCALAPPDATA%\WeiMirror\packs` 下的版本子目录。准备窗口会显示本次的完整路径。连接面板显示当前实际使用的组件目录，支持打开文件夹。选择已有目录时会直接使用所选组件。微映会在 `%LOCALAPPDATA%\WeiMirror\hosts` 中保存启动投屏所需的工作副本。

桌面程序加载 iPhoneMirror v1.8.3 的原生组件。首次使用时可在程序中准备组件，也可使用从项目发布页下载的便携 ZIP。文件名为 `iPhoneMirror-v1.8.3-win-x64.zip`，官方 SHA-256 如下。

```text
97c5ded39a9437069cb24fd4ee59fe9775c1ebcf2700e3fd6305c9e424396bf0
```

下载与版本信息见 [iPhoneMirror v1.8.3 发布页](https://github.com/RayrenSX/iPhoneMirror/releases/tag/v1.8.3)。希望自行构建引擎时，使用[同一标签的源码和脚本](https://github.com/RayrenSX/iPhoneMirror/tree/v1.8.3)。许可与第三方来源见 `NOTICE.txt`、`LICENSE.txt`。

## 投屏窗口和设置

控制台负责选择连接方式和设备。收到画面后，可在独立投屏窗口中点击「置顶」，按 F11 切换全屏，按 Esc 退出全屏。最小化后，可从控制台点击「显示投屏窗口」找回。关闭画面窗口将停止本次接收。

画面可用时，按 Ctrl+S 或点击「截图」可以保存原分辨率的 PNG。截取的是按键时的设备画面，保存位置可自行选择。还未收到画面或接收内核报告受保护内容时，截图不可用。

连接方式、AirPlay 名称、无线画质、电脑音频和窗口置顶设置保存在 `%LOCALAPPDATA%\WeiMirror\preferences.ini`。关闭电脑音频会让接收端静音；iPad 的播放位置由系统和应用决定。

## 微映 1.3

USB 投屏中拔线后，画面窗口会保留。将同一台设备接回即可重试连接，关闭画面窗口、停止接收或切换到 AirPlay 都会取消重连。存在同名设备时，程序按设备标识匹配。

预览工具栏的「跟随比例」默认开启。可按 Ctrl+R 切换，程序会记住窗口位置、大小及所在显示器。全屏或最大化时旋转 iPad，会在恢复普通窗口时调整大小。

无线画质新增 720p · 60 fps（试验）。默认仍为 1080p60。720p60 使用微映自己的屏幕镜像接收进程，调用同一套已校验的 AirPlay 组件，向发送端请求 1280 × 720、最高 60 fps。实际输出的画面尺寸和帧率显示在预览窗口。到「工具 / 关于」可以打开或导出离线延迟测试页，对比各档位在你的设备上的端到端延迟。

控制台会显示当前连接阶段。插线后先看 Windows 有没有识别设备；识别后再检查配对、驱动和视频首帧状态。平板端明确返回等待信任时，界面会提示解锁并确认。
