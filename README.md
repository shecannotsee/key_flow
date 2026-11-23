# Input Probe

Windows DD 重复按键工具，.NET 8 / WPF。

## 使用

双击唯一主线入口 `Start-InputProbe.cmd`，构建最新版并请求管理员权限。直接打开生成的 EXE 同样要求提权。

1. 点击“准备使用”。此前配置编辑、保存、置顶、开始、停止和热键不可用。
2. 初始化成功后选择配置，点击按键按钮录入首个键，设置重复间隔。
3. 开始后按顺序持续重复，停止键或 Ctrl+Shift+F10 结束。运行时收起配置。

初始化可能触发 Win 菜单或鼠标移动；手动按钮让用户决定初始化时机，不代表消除了厂商副作用。初始化不自动执行序列。失败需重启，不反复加载。

## 配置

支持新建、重命名、复制、删除（至少保留一个）、JSON 导入和导出当前配置。导入追加，不替换现有配置；点击“保存配置”才写入本地。保存使用原子替换并保留 `.bak`。

配置与日志仍在 `src/InputProbe.App/bin/Debug/net8.0-windows/` 中的 `config.json` 和 `input-probe.log`，本轮未移动或覆盖。DD 依赖保留在 `dependencies/dd/hid.72000.dll`，须与安装驱动配套。

## 架构

- Core：重复调度、配置存储、运行时。
- Windows：DD 输出、键盘监听、诊断、配置复制与校验。
- App：模块化 WPF；MainViewModel.Setup.cs 负责手动初始化与配置交换。
- tests：核心和 Windows 测试。运行 `dotnet test InputProbe.sln`。

输出仅用 DD；热键接收使用 MouseKeyHook、Raw Input、系统热键和状态轮询。独立 Demo 和旧启动入口已删除，历史可由 Git 恢复。

参见 [功能调研](docs/FEATURE_RESEARCH.md)、[初始化问题](docs/DD_INITIALIZATION_OPTIONS.md)、[热键研究](docs/HOTKEY_LIBRARY_RESEARCH.md)。研究文件保留历史记录，当前使用方法以本文为准。
