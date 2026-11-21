# 输入自动化项目审计报告

## 1. 现有架构与调用链

应用入口是 `src/InputProbe.App/App.xaml` 与 `MainWindow.xaml`。窗口创建 `MainViewModel`，加载程序目录下的 `config.json`，创建 Windows `SendInputKeySender`、`SequenceRunner` 和 `RuntimeController`。

运行调用链为：

```text
WPF 控件/全局 WH_KEYBOARD_LL
  -> MainViewModel.HandleKeyCode
  -> RuntimeController.TriggerAsync / Stop / Pause
  -> SequenceRunner.RunAsync
  -> IKeySender
  -> SendInputKeySender
  -> user32.dll!SendInput
```

`LowLevelKeyboardHook` 在后台线程安装 `WH_KEYBOARD_LL`，把按键事件转发给 ViewModel，用于开始键、结束键和序列键录入。程序关闭时 ViewModel 停止运行并释放 hook。

## 2. 实际输入后端与关键文件

- `src/InputProbe.Windows/WindowsInput.cs`：唯一实际发送后端，使用 `SendInput`，追踪已按下键并在 `ReleaseAll` 中释放。
- 当前后端支持两种发送格式：`VirtualKey`（`wVk`）和 `ScanCode`（`KEYEVENTF_SCANCODE`）；应用默认使用扫描码模式，便于和不同客户端的键盘读取方式进行对比。
- `src/InputProbe.Windows/LowLevelKeyboardHook.cs`：全局低级键盘 hook，仅用于观察/录入，不发送输入。
- 未发现 `keybd_event`、Windows 消息发送、第三方键盘模拟库或 DLL 注入。
- `src/InputProbe.Core/Models.cs`：`IKeySender`、调度器、运行状态和配置模型。
- `src/InputProbe.Core/ConfigStore.cs`：JSON 配置读写。
- `src/InputProbe.App/FileInputLogger.cs`：文件日志；运行时和 Windows 输入后端共用，记录成功发送、发送失败、停止、取消和异常。

## 3. 功能状态

已实现：

- 单键 Click（按下后立即释放）和持续循环。
- 序列增加、删除、上下移动和按键捕获。
- 开始/结束键捕获、暂停、停止、取消和 `ReleaseAll`。
- JSON 保存/加载、基础状态显示、全局 hook 安装状态。
- 普通 Windows 窗口的 `SendInput` 链路已修复 x64 `INPUT` 联合体尺寸问题并验证成功。
- Core xUnit 测试覆盖序列顺序、取消释放和配置往返。

未实现或存在缺陷：

- 输入接口没有显式 `Press`、`Shutdown`，调度器直接拼接 `KeyDown/KeyUp`。
- 配置没有版本字段、非法步骤校验或未知版本处理。
- 输入发送失败现在统一抛出并记录异常；未知键不再静默继续。
- 全局停止键 `Ctrl+Shift+F12` 目前只显示在界面，未按组合键状态识别。
- 空序列和负数/过大间隔可以启动，可能造成无效循环或异常延迟。
- 没有独立 Windows 测试窗口；现有测试是 Core 单元测试，不能证明操作系统实际处理时间，也不能证明目标应用兼容。
- `TargetProcess` 仍在模型中，但当前未启用目标窗口守卫。

## 4. 已有证据与待验证假设

已确认：普通 Windows 窗口曾出现 `SendInput` 错误码 87，修复结构尺寸后发送调用成功；现有测试通过 3 项，构建无错误。已确认剑网三不接受当前标准 `SendInput`，但这只说明目标应用兼容性不同，不能推出普通窗口链路失效。

尚待验证：剑网三窗口模式、前台焦点、运行权限、输入是否到达窗口以及应用是否主动忽略输入。项目不修改目标进程、不注入 DLL、不伪装硬件身份，也不绕过反作弊机制。

## 5. 最小下一步方案

1. 在 `IKeySender` 增加 `Press` 和 `Shutdown`，让调度器只依赖接口，并统一释放状态。
2. 增加配置版本和加载校验，阻止空键、未知键和非法间隔进入执行器。
3. 记录输入成功/失败、取消和清理结果，并识别全局停止组合键。
4. 增加 Core 时序/校验测试，再建立独立 Windows 测试窗口验证普通输入链路；目标应用兼容性单独记录诊断结果。
