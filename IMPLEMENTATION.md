# Input Probe 实施记录

## 当前已落地

- 建立 `InputProbe.sln`，拆分 `Core`、`Windows`、`App` 和测试项目。
- Core：按键配置模型、JSON 配置存储、序列执行器、运行状态控制器。
- Windows：标准 `SendInput` 键盘发送和前台进程检查。
- Windows：低级键盘 Hook 基础设施，支持后续接入触发键和紧急停止。
- App：WPF 控制台初版，包含配置档案、触发键、目标进程、序列列表、启用/暂停/停止/测试触发、状态和诊断区域。
- UI：按 Apple HIG 原则重做浅色主题，并拆分为 `HeaderBar`、`ProfileSidebar`、`ProfileEditor`、`StatusSidebar` 四个 UserControl。
- Profile：支持开始按键、结束按键、目标进程、序列按键、按下时长、步骤间隔，以及序列项增删和上下移动。
- 录制：开始键、结束键和序列键均支持“录制”按钮，点击后捕获下一次全局按键并写入配置。
- 序列按键列为只读，必须先选中行并点击“录制按键”，再按下一个键完成录入；没有选中行时会自动新增按键位。
- 输入：当前阶段关闭目标窗口限制，工具启用后可在任意前台窗口验证；后续再作为可选安全开关恢复窗口限制。
- 设计：`APPLE_DESIGN_GUIDE.md` 与 `Themes/AppleTheme.xaml` 记录并实现颜色、间距、字体、圆角和状态反馈基线。
- 根目录：`Start-InputProbe.cmd`，构建后可双击启动应用。
- 文档：`TECH_PLAN.md` 记录技术路线、分层、游戏拦截处理和 UI 规划。

## 当前限制

- 全局 Hook 已接入 Application 协调器：配置启用后，按触发键即可启动一次序列；托盘服务和真正的 Profile 编辑器尚未接入。
- `MainViewModel` 已统一使用同一个输入发送器实例，保证 `ReleaseAll` 能清理同一批按键。
- 尚未安装 .NET SDK，当前环境只能检查文件，不能执行构建或测试。
- 输入只使用标准 `SendInput`，不实现驱动、注入、虚拟 HID 或绕过游戏拦截的路径。

## 下一步实现顺序

1. 增加 Ctrl+Shift+F12 的组合键识别，完成全局紧急停止热键。
2. 将 Profile 列表、序列增删改和配置加载接入界面。
3. 增加 Core 单元测试项目中的状态机、停止清理和失焦测试。
4. 增加托盘生命周期和最小化到托盘。
5. 安装 .NET 8 SDK 后执行 `dotnet build` 与测试，再在记事本测试输入链路。
