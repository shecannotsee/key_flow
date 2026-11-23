# Input Probe · DD 按键

以用户已验证成功的 DD 数字 8 命令行程序为基准。界面和命令行共用 PulseLoop 执行内核，只保留 DD HID 输入路线。

## 使用

- `Start-InputProbe.cmd`：界面版。点击“新建数字 8 · 80ms”使用成功参数；点击开始或开始热键后立即重复；在目标窗口使用开始热键可避免切换。
- `Start-8.cmd`：最小命令行版，立即持续输入数字 8，Ctrl+C 或关闭窗口停止。
- 不要同时运行两个入口，避免同时输入和设备占用。

界面支持点击录入按键、编辑每步重复间隔、增加/删除/排序、保存配置、持续循环及停止。默认开始 F8、停止 F9，紧急停止 Ctrl+Shift+F10。关闭界面也会取消执行并尝试释放按键。

新配置默认数字 8、间隔 80ms。界面只支持重复模式，按住时长固定为 30ms，不再显示时长参数；允许间隔 30 至 3600000ms。间隔指一次按下到下一步按下，不是松开后的等待。Windows 调度可能使实际间隔更长；不补发积压事件。

## 配置

`src/InputProbe.App/bin/Debug/net8.0-windows/config.json`，日志位于同目录 input-probe.log。打开旧配置不会覆盖原文件。v1 配置按住 50ms，旧间隔加 50ms 迁移为 v2 的总间隔，以保留原有节奏；点击新建数字 8 配置即可使用已验证参数。保存使用原子替换，并保留上一次文件为 config.json.bak。

## 代码

- `src/InputProbe.Core/`：配置、序列快照、共用 PulseLoop、后台调度和取消。
- `src/InputProbe.Windows/`：DD DLL 适配、键码及日志诊断。
- `src/InputProbe.App/`：WPF 编辑界面和系统热键。
- `tools/Repeat8/`：命令行入口，调用同一 PulseLoop。
- `tools/DdProbe/`：只初始化与转换键码，不发送输入。
- `dependencies/dd/`：配套 hid.72000.dll，不提交厂商二进制。
- `tests/`：当前 DD 路线和运行时的测试。

已删除其他输入后端、自写驱动工程及开发脚本、无关测试和过期方案文档。历史实现可从 Git 查看。已恢复的 SDK 缓存留在本地 .artifacts，不参与当前项目构建。

## 构建与验证

```powershell
dotnet build InputProbe.sln
dotnet test InputProbe.sln
Start-8.cmd --check
```

用户已反馈独立 DD 数字 8 程序在目标应用成功；共用内核后的界面版仍需用户复测。普通退出有释放保护，强制杀进程或驱动挂死不保证清理。

运行显示绿色状态条和‘正在重复’标题，停止显示灰色状态条和‘已停止’标题。开始播放上行双音，停止播放下行双音。顶部‘窗口置顶’可切换置顶状态。提示音在后台播放，不阻塞输入；静音或无输出设备时可能听不到。

更新后关闭旧窗口，再运行 Start-InputProbe.cmd；脚本会构建最新版，不移动配置。
