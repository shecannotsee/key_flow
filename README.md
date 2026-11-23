# Input Probe

## 最小输入测试

双击 **Start-8.cmd**：启动独立 DD 命令行程序，5 秒后持续输入主键盘数字 8。
每次按住 30ms，按下到下一次按下的目标间隔 80ms；Windows 调度可能使实际间隔更长，不补发突发事件。
先关闭旧按键程序，倒计时内切换到目标窗口。Ctrl+C 或关闭命令行窗口停止。
该入口不用 WPF、配置档案或全局开始热键，不自动切换到 SendInput。

`Start-8.cmd --check` 只检查 DLL 初始化及数字 8 键码，不发送按键。
已实测构建零警告、零错误，真实初始化及转换成功（DD=208）；尚未确认目标窗口收到输入。
普通退出和控制台关闭会尝试释放按键；强制结束进程或驱动调用挂死时无法保证清理。

## 完整界面

双击根目录唯一入口 **Start-InputProbe.cmd**，默认使用 DD HID 后端。
关闭旧程序后再启动，避免热键和设备占用。配置和日志仍位于原 App 输出目录。

可选启动方式（终端）：

```powershell
.\Start-InputProbe.cmd -Backend SendInput
.\Start-InputProbe.cmd -Elevated
.\Start-InputProbe.cmd -Backend SendInput -Elevated
```

`-Elevated` 触发 Windows UAC。默认不要求提权。原 DD 和 Admin 快捷启动文件已合并到此入口。

## 目录

- `src/`：应用、调度与输入后端。
- `tests/`：单元测试及独立 Windows 接收窗口。
- `dependencies/dd/`：本地 DD 运行库，不提交厂商二进制。
- `docs/`：设计、审计及历史验证记录；旧启动文件名仅供历史参考，以本 README 为准。
- `tools/`：统一启动脚本、DD 探针、驱动开发工具。
- `drivers/`：保留自有 VHF 原型源码。
- `.artifacts/`：依赖缓存及研究记录。

构建：`dotnet build InputProbe.sln`；测试：`dotnet test InputProbe.sln`。

DD 驱动已在本机安装并通过初始化验证，真实输入接收与游戏兼容性尚未完成验证。详见 [DD 研究和实测记录](docs/DD_RESEARCH.md)。
