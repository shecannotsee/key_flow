# VHF 键盘设备源码原型（未构建、未安装）

这是独立的微软 VHF/KMDF HID source driver 原型。它提交标准键盘 HID report，而不是调用 SendInput；它仍是虚拟设备，不是物理 USB 键盘，也不保证目标应用接受。

Driver.c 实现 VhfCreate/VhfStart/VhfReadReportSubmit、管理员及 SYSTEM 访问控制、独占客户端、8 字节输入长度校验，以及客户端句柄清理时全键释放。设备删除时销毁 VHF 对象。不安装 Hook，不修改游戏进程，不使用第三方键盘身份。

开发机检查：存在 Windows SDK include 10.0.26100.0；未检测到 WDK build 目录或可用 MSBuild/cl。当前没有可安装 SYS、INF 或签名目录，不能使用本文件声称设备已实现运行。

后续必须完成：
1. 安装匹配的 Visual Studio C++ 工具链和 WDK，建立 KMDF 驱动项目。
2. 添加自有 Root 设备 INF、Vhf lower filter 配置及规范驱动签名。
3. 在独立测试环境编译、静态分析、Driver Verifier 和真实键盘接收测试。
4. 补充挂起客户端超时释放、多线程卸载竞态和电源生命周期测试。
5. 通过上述验证后再添加 IKeySender 适配器。当前 UI 不显示未实现后端。

通信协议草案：本机管理员打开 \\.\InputProbeHid，DeviceIoControl 使用 CTL_CODE(FILE_DEVICE_UNKNOWN,0x800,METHOD_BUFFERED,FILE_WRITE_ACCESS)，输入 8 字节 [modifier,reserved,key1..key6]，全零释放全部按键；HID usage 与 Windows VK 不同，必须显式映射。

官方参考：https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-
本轮仅核查官方接口及写入源码；没有运行驱动编译或设备输入测试。现有 SendInput 路径与用户配置未改变。
