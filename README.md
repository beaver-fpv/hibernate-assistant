<p align="center">
  <img src="assets/icon-blue.png" width="104" alt="休眠助手图标">
</p>

# 休眠助手 · Hibernate Assistant

一个轻量的 Windows 休眠工具。查看休眠状态，点击开始，在全屏 5 秒倒计时后保存工作状态；下次开机接着用。

[下载最新版](https://github.com/beaver-fpv/hibernate-assistant/releases/latest) · [报告问题](https://github.com/beaver-fpv/hibernate-assistant/issues) · [MIT 许可证](LICENSE)

## 功能

- **蓝色磨砂界面**：主窗口与全屏倒计时采用透明蓝色，跟随系统浅色／深色主题。
- **5 秒内随时取消**：点击「取消休眠」或按 `Esc`，立即回到主窗口。
- **查看系统状态**：显示休眠是否就绪、休眠文件大小和系统盘剩余空间。
- **按需开启休眠**：需要时通过 Windows 管理员授权开启完整休眠。
- **单文件运行**：无需安装，无联网、遥测或第三方包。

## 下载与使用

适用于 **Windows 10／11 x64**，建议使用 **.NET Framework 4.8**。系统、硬件和驱动需要支持 Windows 休眠。

1. 在 [Releases](https://github.com/beaver-fpv/hibernate-assistant/releases/latest) 下载 `HibernateAssistant.exe`，或下载 ZIP 压缩包后解压。
2. 双击程序。打开界面只会读取状态，不会立即休眠。
3. 如果显示休眠尚未开启，点击「开启休眠功能」，按 Windows 提示授予管理员权限。
4. 点击「开始休眠」，进入全屏倒计时。5 秒内可按 `Esc` 或点击取消按钮。
5. 倒计时结束后，程序请求 Windows 保存状态并进入休眠。

**只有在电脑完全进入休眠、状态保存完成后，才可以断开电源。** 倒计时结束仅表示开始请求休眠，不代表数据已经写完。恢复供电后，通常需要按电源键恢复使用。

休眠期间程序会暂停，下载、渲染等任务不会继续执行。休眠也不能代替保存重要文件或备份；保存过程中断电、硬件／驱动兼容问题等情况可能导致恢复失败。

## 磨砂与透明效果

Windows 11 支持时使用系统 Acrylic 磨砂背景，直接透出窗口背后的桌面内容，没有额外的全屏背景图。

关闭 Windows 透明效果、启用高对比度，或系统不支持该效果时，界面会使用不透明背景。Windows 10 可以使用休眠功能，但不会获得同样的 Acrylic 效果。

## 工作方式与权限

程序通过 Windows 电源能力接口读取状态，通过系统目录中的 `powercfg.exe` 开启休眠，通过 `shutdown.exe /h` 请求休眠。

- 日常运行使用普通用户权限；只有点击开启休眠功能时才请求管理员授权。
- 休眠命令不包含强制关闭程序的参数。
- 主题偏好只读取系统设置，不修改系统明暗或透明度设置。
- 休眠文件由 Windows 管理，程序不会删除、压缩或清理该文件。

## 从源码构建

项目使用 C# 和 WPF，无需下载第三方包。请在 Windows x64 上，进入仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建结果为：

```text
dist/HibernateAssistant.exe
```

构建脚本使用 Windows .NET Framework 自带的 C# 编译器。图标的生成脚本位于 `tools` 目录，已生成的图标保存在 `assets` 中。

## 测试

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test.ps1
```

测试使用模拟电源服务（`FakePower`）和离屏窗口，不会开启休眠、触发实际休眠或修改 Windows 电源设置。覆盖倒计时、取消、关闭窗口、失败后重试、主题切换及全屏切换等行为。

**验证范围**：自动测试验证界面与操作流程，未实测真实休眠后的断电恢复，也不保证每种硬件与驱动组合都能恢复工作状态。

## 反馈与贡献

欢迎通过 [Issues](https://github.com/beaver-fpv/hibernate-assistant/issues) 报告问题，或提交 Pull Request。报告问题时请附上 Windows 版本、显示缩放比例及复现步骤；日志与截图请先检查是否包含个人信息。

## 许可证

[MIT](LICENSE) © 2026 beaver-fpv
