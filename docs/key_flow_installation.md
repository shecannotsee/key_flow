# key_flow 安装交付

下文为早期安装验证记录（名称与路径已统一到当前命名，不代表重新执行了历史测试）。当前重命名版本为 1.2.1，Windows x64。项目/命名空间/程序集、解决方案、入口和用户界面已统一为 key_flow。工作区物理根目录已迁移到 E:\workspace\projects\key_flow；主应用为 key_flow.exe。

## 产物与入口

- `dist/key_flow_setup_1.0.0_x64.exe`，约 48 MB；同目录 `.sha256` 校验。
- 已安装路径：`C:\Program Files\key_flow`，桌面及开始菜单快捷方式。
- `start_key_flow.cmd`：源码运行入口，请求管理员权限。
- `tools/build_installer.ps1`：发布自包含 .NET 8 Windows x64 应用并编译 NSIS。
- `tools/test_installer.ps1`：管理员生命周期验证脚本；只接受无既有 key_flow 安装的环境，结束时重新安装。

参考 `E:\workspace\conductor\src\scripts\windows\installer.nsh` 的 NSIS、快捷方式选项、版本化图标和卸载保留配置做法；不复制 Electron 专用包装。默认安装所有用户，桌面/开始菜单快捷方式可在组件页选择。

## 图标

原图 `resource/logo.png` 保留不改。生成 `key_flow.ico`，包含 16/24/32/48/64/128/256 七种尺寸，以及界面 PNG。`tools/generate_icons.py` 使用 Pillow 可重建。

图标嵌入应用 PE、WPF 窗口标题栏/任务栏、应用页头、安装器、卸载器。快捷方式及 DisplayIcon 指向安装目录版本化 ICO，安装/卸载发送 Shell 刷新通知。应用设置稳定 AppUserModelID `key_flow_desktop`，防止误分组；单实例避免同时占用后端。

## 配置与依赖

配置/日志位于当前用户 `%LOCALAPPDATA%\key_flow`。本次迁移保留原始文件，哈希一致；代码仅在新文件不存在时复制旧配置，安装包不携带个人配置。

安装器包含自包含运行时、已验证签名的厂商 DLL 及配套安装文件。系统已有 ddhid72000 时复用并检查 Running；没有则调用厂商安装器并等待服务，失败提示、保留系统卸载入口。不会在安装时调用 DD_btn(0) 或发送键盘输入。

卸载使用构建生成的明确文件清单，不递归删除任意安装目录内容；个人配置、日志及系统共享驱动保留。关闭程序后才能卸载。全新系统的首次驱动安装和重启要求没有在本机重新安装驱动验证。

安装包/应用没有项目代码签名证书，Windows 可能显示“未知发布者”或 SmartScreen 提示；这不等于图标缺失。厂商依赖签名已检查，但公开再分发许可未确认，此产物用于当前用户本机测试，第三方说明已打包。

## 实际验证（2026-11-24）

- 改名后 dotnet test：Core 19、Windows 37，共 56 项通过；应用构建零警告零错误。
- 自包含 publish 成功，NSIS 最终构建无警告。修复了初版脚本 UTF-8 与卸载路径引号问题。
- 实际管理员测试：安装退出码 0；安装目录、DisplayName/DisplayIcon/引号包裹的卸载命令检查通过。
- 桌面/开始菜单快捷方式目标、版本化图标路径检查通过。
- 使用 Windows Shell 提取安装器、应用、卸载器图标，三者像素一致，均为提供的 Logo；ICO 七个尺寸检查通过。
- 实际打开安装后的 WPF 应用，标题含 key_flow，稳定运行、正常关闭。日志确认等待手动准备，没有自动初始化或发送按键。
- 实际卸载：目录、入口、快捷方式清除；配置哈希不变；共享驱动仍 Running。
- 实际重新安装退出码 0；最终保留安装好的 key_flow。
- 详细本机记录：`.artifacts/install-lifecycle/lifecycle.log`、`result.txt=PASS` 及三张 Shell 图标截图。

没有重新进行游戏内按键测试，没有验证全新 Windows 上安装驱动；安装器交互页尚未逐页点击验证，生命周期测试采用静默安装/卸载。当前 Windows 已有 .NET，本次自包含发布不等于已在完全无 .NET 的机器验证。
