# 界面、热键接收与启动排查

2026-11-24，实际读取官方网页（HTTP 200）：
- https://www.opautoclicker.com/ ：重复、后台热键、保存设置。
- https://www.macrocreator.com/ ：可编辑操作、多配置、循环和可定制快捷键。
- https://learn.microsoft.com/en-us/windows/win32/inputdev/using-raw-input ：Raw Input 注册和读取。

借鉴分区和操作组织，而不是照搬完整录制软件：配置选择、重复列表、快捷键、固定启停区；浅色卡片、低对比边框、明确的主按钮。实际以 WPF 渲染预览检查，修正初始列宽和内容被遮挡；预览位于 .artifacts/ui-preview/editor.png，仅包含假配置，不初始化 DD、不发送输入。

所有自动输出仍使用 DD_key。DD 的当前公开接口没有物理键盘监听接口，不能把监听声称为 DD。新增 RawKeyboardInput，以 RIDEV_INPUTSINK 注册键盘，接收 WM_INPUT，通过按下边沿匹配热键，和现有系统热键、轮询去重。仅接收，不拦截、不改变游戏输入。注册结果写入日志。单元测试验证本机注册成功、模拟 Raw 键盘事件分派、按住去重和清理；没有完成游戏前台实测。

用户报告 CMD 打开时类似按下 Win：脚本及应用输出映射未发现主动发送 Win 的路径，现有证据不能确定原因。改用 DeferredDdKeyboard：窗口打开、未开始即关闭均不加载 DD DLL。第一次真正发送按键时初始化，记录初始化前后 Win/Ctrl/Alt/Shift 状态。该记录只是瞬时状态，不代表完整按键事件轨迹，也不能单凭它证明厂商 DLL 行为。Start-8.cmd 仍是立即发送程序，打开它等同开始执行。

验证：29 项测试通过，增加 Raw Input 接收状态测试及延迟初始化测试。测试没有声称修复游戏兼容性或复现 Win 现象；需对比新窗口打开与首次开始两个时点。
