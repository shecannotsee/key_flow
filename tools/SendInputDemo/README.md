# SendInput 管理员最小对照测试

独立项目，仅引用 Core 的 PulseLoop，不引用 DD 或 MouseKeyHook，不替换主程序后端。

1. 停止现有按键工具，避免同时输出。
2. 双击根目录 Start-SendInput-Demo.cmd，在 UAC 中允许管理员运行。
3. 控制台应显示 Administrator=True。立即循环输出主键盘数字 8，起始间隔 80ms、按住 30ms。
4. 先在记事本确认，再切换至剑三分别观察输入框/实际绑定动作。不要把记事本结果当作游戏结果。
5. 切回控制台 Ctrl+C 或关闭控制台停止。普通取消使用 finally 释放，窗口关闭使用控制台处理器尝试释放；强制结束/系统终止不能保证清理。

默认 KEYEVENTF_SCANCODE，MapVirtualKey(0x38) 得扫描码。若无效，结束本次后在项目 CMD 执行 `Start-SendInput-Demo.cmd -VirtualKey` 对照 VK=0x38。两种方式都是微软 SendInput，不是硬件设备输入。

每 100 次输出时间、完成计数、前台 HWND/PID；失败显示 SendInput 返回数量及错误码。成功仅代表系统接受插入，不代表目标程序接收或执行。间隔为计划时序，未实测目标处理时间。

构建验证：0 警告、0 错误；分别执行 DLL 的 --check 和 --check --vk（无输入）验证 x64 INPUT=40 字节、数字 8 扫描码=0x09。构建 EXE 内嵌 requireAdministrator 清单检查通过。当前工具进程不是管理员，未运行真实输出或确认 UAC；游戏内结果待用户测试。

没有固定启动延迟，没有全局启停快捷键；本 demo 专门隔离验证输出 API。完整 GUI 不受影响。
