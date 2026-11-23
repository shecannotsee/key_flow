# 现成键盘监听方案与 DD 初始化问题

2026-11-24，实际读取以下来源：

- MouseKeyHook（MIT）：https://github.com/gmamaladze/globalmousekeyhook 。读取 vNext/MouseKeyHook/WinApi/HookHelper.cs；全局键盘监听使用 WH_KEYBOARD_LL、SetWindowsHookEx 和 CallNextHookEx。本项目通过 NuGet 引用 MouseKeyHook 5.7.1，不复制仓库源码。
- AutoHotkey：https://www.autohotkey.com/docs/v2/lib/_UseHook.htm 。键盘 Hook 是成熟热键工具的一条接收路线，不代表所有应用都允许或可见。
- Microsoft：https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc 。低级键盘回调运行在安装线程；应有消息循环，尽快返回，可使用独立线程；不向目标进程注入 DLL，超时可能被系统静默移除。
- DD 官方 issue：https://github.com/ddxoft/master/issues/106 。报告 dd.2026.EV.HVCI.repack.7z 初始化自动按下 Win。本次查询 comments 为空；没有找到厂商修复说明。当前 hid.72000.dll 与报告中的包不同，不能视为同版本已确认缺陷。
- AutoHotkey：https://www.autohotkey.com/docs/v2/lib/_MenuMaskKey.htm 。工具用掩码按键防止某些 Win/Alt 释放激活菜单；这是额外输入，不能证明能修复 DD 初始化，也不应不加区分地套到厂商初始化上。本次没有加入掩码键、强制关菜单或强制释放用户按键。

## 当前设备证据

本地日志 06:47:00.772：初始化前 RWin=False；06:47:00.896：初始化后 RWin=True，耗时 123.5ms；随后前台变为 SearchHost。此前没有本工具的 DD_key 调用。这支持问题发生在初始化期间，但无法从瞬时采样排除同时存在的其他输入源。新增 before-load、after-load、after-DD_btn(0) 记录进一步区分阶段。

游戏前台时 toolIntegrity=8192，targetIntegrity=unknown(error=5)。读取权限被拒绝不等于已经确认游戏管理员运行。进入游戏时已有 Raw Input 计数不增长的记录，切回其他程序后增长且 WM_HOTKEY 触发成功。这些数据不能单独推断拦截机制。

## 本轮实现

GlobalKeyboardListener 在独立 STA 线程建立 WinForms 消息循环，使用 Hook.GlobalEvents 的 KeyDown/KeyUp，回调仅向 WPF Dispatcher 投递。没有订阅鼠标事件，没有设置 Handled 或 SuppressKeyPress，不记录普通键值。SystemHotkeys 将这一路匹配到的配置热键送入既有 Signal 去重入口，与系统热键/Raw Input/轮询共存。输出链路仍为 Core → DdKeySender → DD DLL。

增加 hookEvents 计数、监听启动状态和快捷键匹配日志。DLL 初始化仍延迟到首次执行，不把后台执行包装成副作用已消失。

## 验证与使用

执行 dotnet test InputProbe.sln --artifacts-path .artifacts/hook-listener --verbosity quiet：Core 19、Windows 12，共 31 项通过。测试真实创建/释放 Hook 监听器，模拟回调验证匹配、去重、Clear 和 Dispose；没有向游戏发送测试键，没有证明实际物理键在游戏中能被监听。

旧进程 PID 51852 仍运行，未强行关闭。关闭旧界面后使用 Start-InputProbe.cmd；启动脚本会构建最新代码。观察 MouseKeyHook 监听：ready，切入游戏尝试配置的开始/停止键后查看 hookEvents 与 KeyboardHook 匹配记录。若监听安装失败会记录错误，既有接收方式仍保留。

未完成：DD Win 副作用修复、剑三前台物理快捷键验证。需要日志与实测继续定位，不能宣称成熟库可以保证兼容。
