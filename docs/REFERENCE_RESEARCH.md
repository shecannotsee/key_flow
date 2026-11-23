# 成熟项目调研记录

本次调研使用 GitHub 公开源码，重点参考输入捕获、宏序列和发送链路，不复制第三方代码。

## 参考项目

### AutoHotkey

- 仓库：https://github.com/AutoHotkey/AutoHotkey
- Hook 安装位置：`source/hook.cpp` 的 `HookThreadProc`
- 发送位置：`source/keyboard_mouse.cpp`

观察到的做法：

- 使用独立线程承载 `WH_KEYBOARD_LL`，线程内部持续运行 `GetMessage` 消息循环。
- 使用进程模块句柄作为 `SetWindowsHookEx` 的 `hMod` 参数。
- Hook 失败会保留错误状态并反馈给上层，而不是静默继续运行。
- 发送按键时构造成对的 KeyDown/KeyUp 输入事件，并对发送模式、延迟和 Hook 冲突做明确处理。

### MysteriousJ/KeyboardRecorder

- 仓库：https://github.com/MysteriousJ/KeyboardRecorder
- 定位：Windows 键盘录制器原型，代码规模较小，适合对照录制数据模型和界面交互。

### Microsoft PowerToys Keyboard Manager

- 仓库：https://github.com/microsoft/PowerToys
- 定位：Windows 输入重映射的成熟产品级实现。
- 结论：配置模型、映射校验和生命周期管理应独立于 UI，不能把输入状态散落在窗口事件中。

## 对本项目的修正

1. `LowLevelKeyboardHook` 改为独立线程 + 消息循环，避免 WPF 界面线程和 Hook 生命周期互相影响。
2. Hook 安装失败必须显示 Win32 错误码。
3. `SendInput` 改为标准的成对事件发送，并支持扫描码映射；不加入驱动、注入或绕过游戏检测的路径。
4. 录入状态仍由 Application/Core 管理，UI 只发起“等待下一键”的命令。
5. 先在记事本等普通窗口验证捕获与发送，再判断具体游戏客户端是否接受标准模拟输入。

## 本项目实测发现

原实现的 `SendInput` 调用返回 Win32 错误码 `87`（参数无效）。原因是 x64 下 `INPUT` 联合体按键分支被错误地当成整个联合体，传入的结构大小不足。修正为 32 字节联合体后，标准虚拟键码发送返回成功；这是输入完全无效的直接原因。

## 限制

GitHub 项目只能说明通用 Windows 输入链路，不能证明剑网三会接受模拟输入。若普通窗口验证通过而游戏无响应，应把问题归类为客户端输入策略或规则限制，不继续增加规避检测能力。
