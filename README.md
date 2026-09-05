# External Monitor Dimmer

一个用于 Windows 的外接显示器空闲调光工具。电脑在指定时间内没有键盘或鼠标操作时，程序通过 DDC/CI 降低外接显示器亮度；恢复操作后，自动恢复到调暗前的亮度。

## 功能

- 自由设置未操作时长，支持秒和分钟
- 自由设置最低亮度（0-100%）
- 键盘或鼠标恢复操作后自动恢复原亮度
- 可选同步 Windows 黑屏屏保，实现接近关屏的视觉效果
- 可自定义全局快捷键，立即进入黑屏屏保；屏保退出后恢复原亮度
- 支持系统托盘运行和登录 Windows 后自动启动
- 记录调暗前的亮度，异常退出后可尝试恢复
- 支持多台可通过 DDC/CI 控制的外接显示器
- 单实例运行，无需管理员权限

## 下载

从 [Releases](https://github.com/ruomeng150/ExternalMonitorDimmer/releases/latest) 下载 `ExternalMonitorDimmer.exe` 或完整 ZIP 包。

本程序没有商业代码签名，Windows 可能显示“未知发布者”。

## 使用方法

1. 运行 `ExternalMonitorDimmer.exe`。
2. 设置“未操作时长”和“最低亮度”。
3. 在“立即屏保快捷键”框中单击，然后按下要使用的组合键；按 `Backspace`、`Delete` 或 `Esc` 可清除。字母和数字需要搭配 `Ctrl`、`Alt` 或 `Shift`，也可以使用单独的 `F1-F24`。快捷键被其他程序占用时，应用会提示并保留原快捷键。
4. 需要自动黑屏效果时，勾选“同步使用黑屏屏保”。
5. 点击“应用并开始”。

快捷键和托盘菜单中的“立即进入屏幕保护程序”可以在监控停止时使用。触发后程序会先等待按键或鼠标按钮释放，再将外接显示器调到设定的最低亮度并启动 Windows 黑屏屏保；任意键盘或鼠标操作退出屏保后，程序会恢复触发前的亮度。屏保启动失败时也会尝试恢复亮度。

窗口右上角关闭和最小化都会隐藏到系统托盘，监控仍会继续。双击托盘图标可重新打开窗口。

- “停止监控”：恢复显示器亮度和原来的屏保设置，并保存停止状态。
- “退出程序”：恢复显示器亮度和原来的屏保设置，然后退出进程。
- 托盘右键菜单中的“立即进入屏幕保护程序”：立即执行一次黑屏屏保，不改变已保存的自动监控开关。
- 如果在“监控中”直接退出，下次启动时会自动恢复监控。若不需要，请先点击“停止监控”。

## 开机自动运行

勾选“登录 Windows 后自动运行”，然后点击“应用设置”或“应用并开始”。程序会把启动副本保存到：

```text
%LOCALAPPDATA%\ExternalMonitorDimmer\ExternalMonitorDimmer.exe
```

取消勾选并再次应用设置即可移除启动项。

## 兼容性

- Windows 10 或 Windows 11
- 建议使用 .NET Framework 4.8 或更高版本
- 显示器必须支持 DDC/CI；部分型号需要在显示器菜单中手动启用 DDC/CI

最低亮度 `0%` 只是显示器允许的最低背光亮度，不代表显示器已经断电。

## 从源码构建

项目不依赖第三方 NuGet 包。请在 Windows PowerShell 5.1 或更高版本运行：

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build.ps1
```

默认输出文件：

```text
dist\ExternalMonitorDimmer.exe
```

构建脚本使用系统 .NET Framework C# 编译器：

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

## 数据与系统改动

应用数据保存在：

```text
%LOCALAPPDATA%\ExternalMonitorDimmer\
```

启用相关功能时，程序只修改当前用户范围内的设置：

- 开机启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- 黑屏屏保：`HKCU\Control Panel\Desktop`

程序会在应用黑屏屏保前备份原设置，并在停止监控或退出时恢复。

## 卸载

1. 取消“登录 Windows 后自动运行”并应用设置。
2. 点击“停止监控”。
3. 点击“退出程序”。
4. 删除程序文件以及 `%LOCALAPPDATA%\ExternalMonitorDimmer\`。

## 许可证

[MIT License](LICENSE)
