# 输入兼容性对照：2026-11-24

## 本轮修改
序列执行路径改为 KeyDown -> 可取消 Task.Delay(50) -> finally KeyUp -> 原步骤间隔。
50ms 是请求的保持时间，线程调度会带来误差，不是操作系统或游戏实测接收时间。
保留扫描码 SendInput 后端；即时 Press API 仍保留，但序列不再调用它。
旧配置及间隔不变，无需迁移。按住阶段停止会取消等待并释放按键。

## 公开资料
- Microsoft SendInput：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
  只允许向同级或更低完整性级别应用注入；返回值及 GetLastError 无法直接指出 UIPI 阻止原因。现有物理键状态也会影响输入。
- AutoHotkey SetKeyDelay：https://doggy8088.github.io/AutoHotkeyDocs/docs/lib/SetKeyDelay.htm
  明确提到部分游戏需要按下到松开的延迟；AHK SendInput 模式不遵循 SetKeyDelay。这里采用两次原生 SendInput 之间异步等待，不是依赖 AHK 设置。
- PyDirectInput 源码：https://github.com/learncodebygaming/pydirectinput/blob/master/pydirectinput/__init__.py
  使用扫描码及 SendInput；替换成这个库不意味着获得硬件输入，也不保证剑三兼容。
- 用户视频：https://www.bilibili.com/video/BV1MQsb6tEcg/
  已读取 B 站元数据：江影任浮生《【剑网3】大肥鱼按键5.4版本教程（新增宏录制功能）》。简介没有源码；本轮未观看视频或取得其实现，不能确认作者使用的时长及发送参数。

## 下一步对照
同一绑定按键，在前台普通窗口、剑三分别测试。记录客户端版本、窗口模式、工具与客户端权限是否一致、日志 Down/Up 和实际动作。先按当前默认 50ms 测试；如果普通窗口成功而游戏不响应，仍需区分权限、焦点、游戏键位及读取策略。API 成功不等于游戏收到。
不把 PostMessage 当作硬件输入替代；不安装来源不明驱动或下载执行视频作者的未知软件。

## 验证范围
Core 测试覆盖保持时间（调用层）及按住期间取消后释放。游戏客户端接收效果未验证。


## 成熟实现复核与本轮落地

实际读取 AutoHotkey alpha 分支 `source/hotkey.cpp`（RegisterHotKey 调用）和 `source/keyboard_mouse.cpp`（SendInput 与旧事件发送实现）：
- https://github.com/AutoHotkey/AutoHotkey/blob/alpha/source/hotkey.cpp
- https://github.com/AutoHotkey/AutoHotkey/blob/alpha/source/keyboard_mouse.cpp
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey

项目采用上述系统热键路线，未复制 AutoHotkey 源码或引入其运行时。WPF HWND 接收 WM_HOTKEY；注册 MOD_NOREPEAT；配置切换和按键录入后重新注册；关闭时注销。F12 被微软保留，旧 F12 配置仍用 Hook，新增 Ctrl+Shift+F10 系统停止键；Ctrl+Shift+F12 旧 Hook 停止保持兼容。注册冲突写入日志并在界面提示回退。序列不能包含自身开始/停止键，防止合成输入触发热键。

SendInput 保持 50ms 扫描码模式；日志新增前台 HWND、PID、进程名称，以及工具/目标完整性级别 RID（8192 中、12288 高）。无法读取时明确 unknown/error，不猜测权限。API 返回成功只代表插入系统输入流，不能代表游戏接收。

`Start-InputProbe-Admin.cmd` 为显式管理员启动入口，由 Windows UAC 决定是否授权；普通启动入口保持原样。测试前关闭旧实例，避免互相抢占热键。管理员启动用于权限对照，不保证游戏兼容。

验证：Windows 真实 RegisterHotKey 测试覆盖成功注册、重复占用失败、Clear/Dispose 释放后重新注册；7 项 Core 测试通过。独立接收窗口测试工具在 `tests/InputProbe.Windows.Smoke`，实际执行两次均因无法取得前台焦点中止，没有向其他窗口发送测试键，也没有测得实际按键时序。不能将该测试记为通过。

重复测试：先构建 App 到隔离目录 `.artifacts/hotkey-review`（没有用户配置），再运行：
`dotnet run --project tests/InputProbe.Windows.Smoke -- .artifacts/hotkey-review/InputProbe.App.exe`
运行时接收窗口必须获得焦点，任何失焦都会终止发送；测试启动/停止独立 App 实例，不保存配置。

游戏内尚待验证：WM_HOTKEY 是否到达；toolIntegrity 与 targetIntegrity 是否相同；SendInput 是否返回错误；返回成功但客户端无动作的情况另行记录。尚未取得博主源码，不能声称本项目已经复现其客户端结果。


## 用户本轮实际日志结论（2026-11-24 04:47）

读取正式输出目录 input-probe.log，未修改配置、未向游戏发送额外输入。

- 04:47:36.205：WM_HOTKEY id=1 到达，前台为 JX3ClientX64，随后进入启动序列。
- 工具和目标完整性级别均为 12288；本轮不是低权限工具向高权限目标发送。
- 04:47:36.211 至 04:47:46.027 前台切回其他应用之前，记录 63 次 D1 Down 和 63 次 D1 Up，未记录发送失败。样本 Down/Up 调用日志间隔约 62ms；这不是游戏接收时间。
- 04:47:47.271：其他应用前台时 WM_HOTKEY id=2 到达，执行停止。
- 磁盘 config.json 的序列为 D8，本次实际运行日志为 D1。可能是界面未保存的编辑；不能认定是发送映射错误。

已缩小范围：本轮开始热键和发送 API 调用成功；用户观察游戏没有响应，故尚待查明游戏绑定/场景或对合成事件的处理。不能凭 API 成功断定游戏收到，也不能凭现象确认反作弊。
下一项必要对照是同一客户端场景下物理主键盘数字 1 是否触发相同动作，以及 D1 是否为用户预期键。未经该确认不再变更发送参数或宣称复现视频方案。
