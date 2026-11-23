# SendInput 修正与验证（2026-11-24）

保留默认 SendInput 路线，未安装驱动或修改系统安全设置。

本轮检查调用链：MainViewModel → RuntimeController → SequenceRunner → KeyDown / 50ms 可取消等待 / KeyUp → SendInput。全局快捷键独立使用 RegisterHotKey 和 WM_HOTKEY。当前循环不绑定目标进程。

修正 WindowsInput.cs：
- 使用前台线程的键盘布局调用 MapVirtualKeyExW。
- 虚拟键模式也提供扫描码及扩展键标志；扫描码模式保持 wVk=0。
- 对专用方向键和导航键显式设置扩展标志。本机测试发现布局映射未返回 E0，首次新增测试因此失败，补充处理后通过。
- 按下时保存对应释放事件，避免切换窗口或布局后重新映射出不同的释放扫描码。
- 用真实 MOUSEINPUT 联合成员决定 INPUT 尺寸，替代固定 x64 union 大小。当前进程 ABI 检查通过；未运行 x86 测试。

新增 SendInputEncodingTests：Windows INPUT 尺寸、两种模式下普通键与扩展键的按下/释放编码。最终 `dotnet test InputProbe.sln --no-restore`：Core 7、Windows 11，共 18 项通过。测试不会发送键盘输入。

独立窗口测试使用复制的 App 和默认配置，未覆盖用户配置。两次运行均在发送启动热键之前因接收窗口失焦而退出，退出码 1；未得到接收计数或时序数据。第二次增加 WaitForInputIdle 后仍失败，说明不能把首次失焦仅归因于启动竞态。保留焦点保护，不向未知窗口发送测试按键。

继续使用项目根目录 Start-InputProbe.cmd；需要高权限对照时使用现有 Start-InputProbe-Admin.cmd。重新启动程序以加载新构建。先选择扫描码模式，再对比虚拟键模式。输入日志与 config.json 位于 App 输出目录。

剩余：交互桌面上的真实接收窗口测试、目标应用内测试、长时间稳定性。此次修正尚无证据表明能解决剑三不接收 D1 等普通键的问题；不把成功返回的 SendInput 计数当作目标收到事件。SendInput 仍为系统合成输入，不是物理硬件输入。
