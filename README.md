# Input Probe

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
