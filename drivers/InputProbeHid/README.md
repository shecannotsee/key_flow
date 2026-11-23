# VHF 键盘设备源码原型（未构建、未安装）

这是独立的微软 VHF/KMDF HID source driver 原型。它提交标准键盘 HID report，而不是调用 SendInput；它仍是虚拟设备，不是物理 USB 键盘，也不保证目标应用接受。

Driver.c 实现 VhfCreate/VhfStart/VhfReadReportSubmit、管理员及 SYSTEM 访问控制、独占客户端、8 字节输入长度校验，以及客户端句柄清理时全键释放。设备删除时销毁 VHF 对象。不安装 Hook，不修改游戏进程，不使用第三方键盘身份。

开发环境已更新：确认已有 VS 2022 Build Tools 17.14、MSVC 14.44；项目本地恢复微软官方 WDK/SDK NuGet 10.0.26100.6584。Driver.c 已通过真实 x64 内核编译，输出 .artifacts/driver-build/Driver.obj。当前没有可安装 SYS、INF 或签名目录，设备尚未运行。

后续必须完成：
1. 编译依赖已就绪，下一步建立完整 KMDF 驱动链接及打包项目。
2. 添加自有 Root 设备 INF、Vhf lower filter 配置及规范驱动签名。
3. 在独立测试环境编译、静态分析、Driver Verifier 和真实键盘接收测试。
4. 补充挂起客户端超时释放、多线程卸载竞态和电源生命周期测试。
5. 通过上述验证后再添加 IKeySender 适配器。当前 UI 不显示未实现后端。

通信协议草案：本机管理员打开 \\.\InputProbeHid，DeviceIoControl 使用 CTL_CODE(FILE_DEVICE_UNKNOWN,0x800,METHOD_BUFFERED,FILE_WRITE_ACCESS)，输入 8 字节 [modifier,reserved,key1..key6]，全零释放全部按键；HID usage 与 Windows VK 不同，必须显式映射。

官方参考：https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-
已执行 Driver.c 编译检查；未执行设备安装或输入测试。现有 SendInput 路径与用户配置未改变。

重新恢复依赖并验证：
`powershell -NoProfile -ExecutionPolicy Bypass -File tools/driver-env/Build-Check.ps1`
已恢复依赖时可附加 `-SkipRestore`。
