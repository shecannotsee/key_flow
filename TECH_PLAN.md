# 剑网三按键工具技术方案（初版）

> 当前实现审计与验证记录见 [AUDIT_REPORT.md](AUDIT_REPORT.md)。当前版本使用 Click 语义和持续循环，配置版本为 1，输入接口包含 `KeyDown`、`KeyUp`、`Press`、`ReleaseAll`、`Shutdown`。

## 1. 目标与边界

初版做一个 Windows 桌面按键工具，支持：

- 将一个物理按键映射为另一个按键或组合键。
- 配置有限长度的固定按键序列，例如 `1 -> 2 -> 3`。
- 每个 profile 配置独立的开始按键和结束按键；开始按键启动持续序列，结束按键立即取消并释放按键。
- 序列中的每个步骤使用 Click 语义并配置步骤间隔，支持调整顺序。
- 启动后的行为固定为持续循环完整序列；只配置步骤之间的间隔，结束键负责取消循环。
- 目标窗口限制属于后续可选能力，当前验证阶段不要求选择目标进程。
- 目标窗口限制在当前验证阶段可关闭，便于先确认全局 Hook 和 `SendInput` 链路。
- 全局暂停/停止热键、托盘状态和配置持久化。
- 提供“键盘事件日志/测试窗口”，不需要启动游戏即可验证输入链路。

不包含：绕过游戏反作弊、驱动级注入、内存读写、自动识别画面/目标、自动寻路或无人值守循环。这些能力会显著增加封禁和安全风险，也不属于普通键盘辅助工具的必要范围。实际使用前应确认剑网三当前用户协议及运营规则允许相关输入方式。

## 2. 推荐技术栈

- **系统**：Windows 10/11。
- **语言/运行时**：C#、.NET 8。
- **界面**：WPF；生态成熟，适合托盘、全局热键和桌面配置工具。
- **输入监听**：`SetWindowsHookEx(WH_KEYBOARD_LL)` 监听全局按键，仅用于识别触发键和停止键。
- **发送输入**：Windows `SendInput`，发送标准键盘事件，不使用驱动或进程注入。
- **窗口校验**：`GetForegroundWindow` + `GetWindowThreadProcessId`，按进程名/窗口标题匹配目标窗口。
- **配置**：JSON 文件，使用 `System.Text.Json`；后续需要多配置时按 profile 分文件或单文件数组管理。
- **日志**：`Microsoft.Extensions.Logging`，默认写本地滚动日志，不记录不必要的全局按键内容。
- **测试**：xUnit；输入发送层通过接口抽象，用 fake sender 做单元测试。

## 3. 模块划分

```text
InputProbe/
  src/InputProbe.App/          WPF 界面、托盘、生命周期
  src/InputProbe.Core/         配置模型、状态机、序列调度
  src/InputProbe.Windows/      Win32 hook、SendInput、前台窗口检测
  tests/InputProbe.Core.Tests/ 状态机和调度测试
```

核心接口建议：

```csharp
public interface IKeySender
{
    void KeyDown(string key);
    void KeyUp(string key);
    void Press(string key);
    void ReleaseAll();
    void Shutdown();
}

public interface IForegroundWindowGuard
{
    bool IsTargetForeground();
}
```

## 4. 运行状态与安全机制

状态只有 `Stopped`、`Armed`、`Running`、`Paused` 四种。任意异常、目标窗口失焦、用户按下停止热键、工具退出时，都执行 `KeyUp` 清理，避免按键卡住。

建议默认行为：

- 启动后为 `Stopped`，不能直接向任何窗口发送输入。
- 用户明确启用 profile 后进入 `Armed`。
- 触发键按下时先检查目标窗口，再运行一次序列。
- 默认禁止并发触发；上一次序列未结束时忽略重复触发。
- 每个 profile 设置最大序列长度、最大持续时间和每分钟触发上限。
- `Ctrl+Shift+F12` 作为默认全局紧急停止键，且允许修改。
- 所有按键发送前再次检查目标窗口；失焦立即终止。

## 5. 配置示例

```json
{
  "profiles": [
    {
      "name": "基础序列",
      "targetProcess": "",
      "startHotkey": "F8",
      "stopHotkey": "F12",
      "steps": [
        { "key": "D1", "waitAfterMs": 80 },
        { "key": "D2", "waitAfterMs": 100 }
      ],
      "enabled": false
    }
  ],
  "panicHotkey": "Ctrl+Shift+F12"
}
```

配置界面应提供按键录入、顺序调整、持续/间隔时间、目标进程选择和启用开关；不要允许用户输入任意脚本或执行外部命令。

## 6. 分阶段实现

### MVP-1：输入链路

1. 建立 .NET 8 WPF 解决方案和 Core/Windows/App 项目。
2. 实现 `SendInput` 封装和独立测试窗口。
3. 实现低级键盘 hook，只捕获配置的触发键和停止键。
4. 加入目标前台窗口检查、停止清理和日志。

### MVP-2：可配置序列

1. JSON 配置读写和 profile 管理。
2. 序列编辑器、启用/暂停、托盘图标。
3. 以 fake sender 覆盖状态机、失焦、异常和停止场景。

### MVP-3：剑网三验证

1. 先在记事本/自建测试窗口验证按下、抬起、停止和失焦行为。
2. 再在剑网三非战斗场景做单键和短序列测试。
3. 记录客户端版本、窗口模式、管理员权限和输入结果；不为绕过拦截增加驱动或注入能力。

## 7. 关键风险

- 游戏更新可能改变窗口行为或限制模拟输入，不能承诺长期兼容。
- 管理员权限不一致时，低权限工具可能无法向高权限窗口发送输入；应优先提示用户统一权限，而不是静默提权。
- 全局 hook 属于敏感能力，必须限制监听范围、提供可见运行状态，并在退出时可靠卸载。
- 剑网三规则可能禁止宏/自动化操作。发布或实际使用前需要由用户自行确认规则，工具不提供规避检测功能。

## 8. 下一步需要确定

1. 先做“按键映射”，还是直接做“固定序列宏”。
2. 目标客户端进程名/窗口标题，以及是否需要支持多开。
3. 是否只支持 Windows 10/11，是否允许 .NET 8 Desktop Runtime 作为安装前置。
4. 触发方式是按下触发一次，还是按住持续触发；初版建议只支持按下触发一次。

## 9. 技术路线与语言选择

### 推荐路线：C# + .NET 8 + WPF

剑网三的主要运行环境是 Windows，工具需要使用 Win32 的全局键盘钩子、前台窗口 API 和 `SendInput`。C# 调用这些 API 的成本低，WPF 适合配置界面和托盘程序，.NET 8 也便于后续拆分测试项目。

不建议初版使用 Python：做原型很快，但全局 hook、托盘生命周期、发布和异常清理更容易受到第三方包影响。也不建议一开始使用 C++/驱动：只有在明确需要内核级设备开发时才有必要，普通按键工具不需要。

### 编码方式

1. 使用 Visual Studio 或 `dotnet` CLI 创建解决方案。
2. 所有 Win32 调用集中在 `InputProbe.Windows`，通过 P/Invoke 封装，不让 WPF 界面直接调用 Win32。
3. Core 层只处理配置、规则、状态机和调度，使用接口依赖输入发送器和窗口守卫。
4. 调度使用 `CancellationToken` 和异步延时，停止时统一取消任务并释放所有已按下的键。
5. 先用 fake sender、fake clock 和 fake foreground guard 测试，再连接真实 Windows 实现。

## 10. 分层与模块化架构

```text
Presentation  WPF 窗口、托盘、ViewModel、用户提示
     |
Application   启停命令、Profile 管理、热键协调、运行状态
     |
Domain        Profile、KeyStep、状态机、序列规则、限流规则
     |
Infrastructure.Windows
              LowLevelKeyboardHook、SendInput、前台窗口检查、JSON、日志
```

### Domain 层

- `KeyProfile`：目标窗口、触发键、步骤、启用状态。
- `KeyStep`：键值、按下持续时间、步骤间隔。
- `MacroStateMachine`：`Stopped/Armed/Running/Paused` 状态转换。
- `SequencePolicy`：序列长度、最长持续时间、触发频率和是否允许并发。

Domain 不引用 WPF、Win32 或具体日志库，保证可以在普通测试进程中运行。

### Application 层

- `ProfileService`：加载、校验、保存配置。
- `HotkeyOrchestrator`：接收 hook 事件，判断触发键/停止键并调用状态机。
- `SequenceRunner`：按照步骤调用 `IKeySender`，每一步都通过 `IForegroundWindowGuard` 检查。
- `EmergencyStopService`：任何停止原因都走同一条清理路径。

### Infrastructure.Windows 层

- `LowLevelKeyboardHook`：只监听已配置的触发键和紧急停止键，退出时可靠卸载。
- `SendInputKeySender`：把抽象键值转换为 Win32 `INPUT` 结构，负责成对发送 KeyDown/KeyUp。
- `ForegroundWindowGuard`：检查当前窗口进程名、窗口句柄和进程完整性级别。
- `JsonProfileStore`：配置文件读写和版本迁移。

### Presentation 层

界面只绑定 Application 层命令和状态，不直接发送按键。至少显示当前 profile、目标窗口匹配结果、`Stopped/Armed/Running/Paused` 状态和最近一次停止原因。

## 11. 游戏拦截时的处理策略

先区分三类情况：

1. **工具没有收到触发键**：检查 hook 是否安装、是否与游戏权限级别一致、是否被安全软件拦截。
2. **工具发送成功但游戏没有响应**：检查游戏窗口模式、键位绑定、输入法、管理员权限和客户端版本；用记事本或自建测试窗口验证 `SendInput` 是否正常。
3. **游戏明确拒绝模拟输入或规则禁止自动化**：停止扩展，保留标准输入模式并提示用户。这种情况下不增加驱动注入、DLL 注入、内存读写、虚拟 HID 伪装或其他规避检测手段。

因此，代码中应把输入实现抽象成 `IKeySender`，初版只实现 `SendInputKeySender`。如果未来官方提供受支持的插件或接口，可以新增一个合规适配器，而不是修改 Core 层或加入绕过逻辑。

## 12. 数据流

```text
物理按键
  -> LowLevelKeyboardHook
  -> HotkeyOrchestrator
  -> 前台窗口检查 + Profile 校验
  -> MacroStateMachine
  -> SequenceRunner
  -> IKeySender(SendInput)
  -> 每一步检查窗口/取消信号
  -> 完成或 EmergencyStop 清理
```

所有异常和取消都必须汇聚到 `ReleaseAllPressedKeys()`，这是防止键盘卡住的核心安全点。

## 13. 推荐实现顺序

1. 建立四个项目：`App`、`Core`、`Windows`、`Core.Tests`。
2. 先完成 Domain 状态机和序列规则，不接 Win32。
3. 加入 fake sender/clock/guard，覆盖停止、失焦、异常、重复触发。
4. 实现 `SendInput`，在记事本测试按下/抬起成对性。
5. 实现低级 hook 和紧急停止，测试退出时卸载。
6. 实现 WPF 配置界面和托盘生命周期。
7. 最后在剑网三非战斗场景验证，并记录哪些输入模式可用。

## 14. 配套前端使用界面

### 技术路线

前端采用 **WPF + MVVM + CommunityToolkit.Mvvm**，与 .NET 8 主程序同进程。WPF 适合 Windows 桌面工具的托盘、全局热键、窗口激活和系统通知；MVVM 可以让界面不直接操作 hook 或 `SendInput`。

不建议把核心界面做成浏览器页面。浏览器不能可靠管理全局键盘 hook、进程前台检测和退出清理；如果以后需要远程查看状态，可以另加只读 Web API，但不作为初版输入控制入口。

### 界面分区

```text
主窗口
  顶部：运行状态、目标窗口、紧急停止按钮
  左侧：Profile 列表
  中部：按键序列编辑器
  右侧：目标窗口和输入链路诊断
  底部：事件日志、配置保存、启用/暂停
托盘菜单：启用当前配置、暂停、紧急停止、打开主窗口、退出
```

### 页面与控件

#### 1. 总览页

- 当前 Profile 名称和启用状态。
- 状态徽标：`Stopped`、`Armed`、`Running`、`Paused`。
- 目标窗口：进程名、窗口标题、是否前台、权限级别是否一致。
- 最近一次触发时间、完成/停止原因。
- 明显的“紧急停止”按钮；停止后显示所有按键已释放。

#### 2. Profile 编辑页

- Profile 名称、目标进程选择、触发键录入。
- 序列表格：顺序、按键、按下时间、间隔时间、删除和上下移动。
- 校验提示：空序列、非法键值、超长持续时间、重复触发策略。
- “保存并测试”与“启用”分开，避免编辑时意外向游戏发送输入。

#### 3. 诊断页

诊断页要明确区分输入链路：

```text
触发键捕获：成功/失败
目标窗口匹配：成功/失败
SendInput 调用：已执行/失败
测试窗口响应：成功/未响应
剑网三响应：只能由用户观察确认
```

这里不能把“SendInput API 调用成功”显示成“剑网三执行成功”。游戏可能使用不同的输入读取方式或主动忽略模拟输入，工具只能报告自身可观察到的结果。

#### 4. 设置页

- 紧急停止热键。
- 是否随 Windows 启动。
- 日志级别和日志目录。
- 目标窗口匹配方式：进程名优先，窗口标题作为辅助。
- 启动时默认状态固定为 `Stopped`。

### MVVM 结构

```text
Views/
  MainWindow.xaml
  DashboardView.xaml
  ProfileEditorView.xaml
  DiagnosticsView.xaml
  SettingsView.xaml
ViewModels/
  MainViewModel.cs
  DashboardViewModel.cs
  ProfileEditorViewModel.cs
  DiagnosticsViewModel.cs
Services/
  IUiNotificationService.cs
  ITrayService.cs
  IAppCoordinator.cs
```

ViewModel 只依赖 Application 层接口，例如 `IProfileService`、`IAppCoordinator`、`IDiagnosticsService`。按钮通过 `RelayCommand` 调用启用、暂停、停止和保存命令；状态通过 `ObservableProperty` 或事件推送更新。

### 普通按键拦截下的用户流程

1. 用户先在诊断页选择“测试窗口”，确认触发键能被工具捕获。
2. 工具确认目标窗口前台后，执行一次短序列。
3. 若测试窗口能响应而剑网三无响应，界面显示“系统发送成功，目标未确认”，并提示检查游戏键位、窗口模式、权限和规则。
4. 工具不自动切换驱动级输入、注入模块或虚拟 HID，也不把失败重试设计成规避拦截。

这样前端的作用是配置、状态控制和诊断，而不是把游戏内部不可观察的结果伪装成成功。

### 视觉与交互原则

- 默认深色或中性深色主题，适合游戏运行时查看；状态颜色保持固定：绿色可用、黄色暂停/等待、红色停止或异常。
- 所有危险操作使用二次确认或明显的停止反馈；紧急停止不放在容易误触的普通操作旁边。
- 主窗口关闭时默认最小化到托盘，真正退出必须经过托盘菜单；退出前执行统一清理。
- 所有状态变化都显示文字，不只依赖颜色，保证可读性。
