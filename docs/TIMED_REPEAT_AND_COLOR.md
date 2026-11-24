# 限时重复与屏幕取色 · 1.1.0

## 行为

- 一个选中的配置对应一个执行任务。主开始键和可选备用开始键均触发该任务，可配置 X / F → 重复 E。
- 持续时间单位为秒，支持小数，范围 0–86400。0 表示无限重复，6 表示从触发起计时六秒；颜色等待也占用时间。
- 同一方案不叠加任务；再次触发默认从最后一次触发重新计算限时，也可设为忽略重复触发。不同方案仍使用共享 FIFO 激活池。单键开关启用时，主开始键继续承担停止功能，备用键只负责开始。
- 停止快捷键与紧急停止保留；截止、取消、读取异常均走原有按键释放路径。
- 每个配置保存一个屏幕物理像素坐标、目标 RGB 和容差。可建立不同配置保存不同检测点，目前不提供多点 AND/OR 流程编排。
- 手动取色三秒倒计时期间禁用配置操作和热键；结束后恢复监听。退出取消倒计时。
- 单次检测显示当前颜色及匹配状态。可选颜色条件在每次按下前检查；未匹配时以 50ms 间隔检查，匹配才继续。已经按下的键仍按原有短按时长释放。
- 检测失败不会被当作“不匹配”无限等待，而是报错、停止和清理。

## 模块和调用链

`AutomationEditor.xaml → MainViewModel.Automation → 配置模型 / ScreenColorReader / ColorMatcher`

`SystemHotkeys (1 或 4) → MainViewModel.StartSequence → RuntimeController → SequenceRunner → PulseLoop → IKeySender`

`SequenceRunner → ActivationWindow 截止令牌 + IScreenColorReader → ColorMatcher → PulseLoop 的输出条件`

- Core/ScreenColor.cs：颜色数据、条件配置、匹配算法、参数验证和读屏接口，无 UI/Windows 依赖。
- Windows/ScreenColorReader.cs：可见桌面 GetPixel 与物理鼠标坐标；临时 DPI 上下文在 finally 恢复，屏幕 DC 在 finally 释放。
- Core/SequenceRunner：快照配置，创建关联取消令牌和截止时间，区分自然到期与外部取消。
- Core/RuntimeController：状态变化、失败记录和最终释放。
- App/Controls/AutomationEditor：只做展示和绑定。
- App/MainViewModel.Automation：取色倒计时、命令、状态展示；不包含颜色比较和按键算法。
- MainViewModel 与 RelayCommand 从 MainWindow.xaml.cs 拆出；窗口只保留窗口事件和生命周期衔接。
- 原有输出实现和独立输入监听保持不变；新备用开始键使用注册 ID 4。

## 数据兼容

配置版本升到 3；读取版本 1/2 时迁移到 3，缺失字段默认无限运行、备用键为空、颜色条件关闭。保存原子替换并保留备份，复制/导入/导出均保留新增字段，颜色配置深拷贝。开发和测试没有写入用户实际配置。

## 实际验证（2026-11-25）

运行 `dotnet test key_flow.sln --verbosity quiet --logger "trx;LogFilePrefix=automation" --results-directory .artifacts/automation-tests`：Core 31 项、Windows 39 项，全部通过。

新增测试覆盖：

- 六秒任务、80ms 间隔使用假输入后端重复执行；重复触发不会产生第二个任务，结束后按下/释放数相等，清理一次。
- 自动截止、长按期间快速停止、颜色未匹配时零输出、匹配恢复后继续。
- 先输出一次后读屏异常：释放并报告执行失败。
- RGB 每通道容差边界、非法颜色/时长、旧 v2 默认值与新配置往返、复制隔离与快捷键冲突。
- 真实 Windows/WPF 无边框色块窗口：实际屏幕读取 #20A0E0，容差 2 内匹配；无效坐标明确失败。该测试会短暂显示窗口。
- WPF 实际控件以预览数据渲染，并人工查看普通尺寸截图；未在预览中初始化输入组件或触发按键。

测试日志保存在 `.artifacts/automation-tests`。时间测试测量调度器总运行时间，**不代表目标应用接收按键的时间**。未对新增功能执行游戏内测试、HDR/独占全屏测试或长时间业务流程验证。安装包构建与既有版本的安装生命周期测试需区分；本轮不自动覆盖正在使用的安装版本。

## 使用边界

屏幕颜色只代表指定位置当前显示的像素。遮挡、动画、HDR、远程桌面、窗口移动、显示缩放可能改变结果，需重新取色和检测。不能据此读取被遮挡窗口或应用内部业务状态。复杂流程可后续在独立核心模块增加多点条件和流程节点，不放入界面事件处理器。
