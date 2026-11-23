# VHF 键盘设备开发原型（已构建、未安装）

这是独立的微软 VHF/KMDF HID source driver 原型。它提交标准键盘 HID report，而不是调用 SendInput；它仍是虚拟设备，不是物理 USB 键盘，也不保证目标应用接受。

Driver.c 实现 VhfCreate/VhfStart/VhfReadReportSubmit、管理员及 SYSTEM 访问控制、独占客户端、8 字节输入长度校验，以及客户端句柄清理时全键释放。设备删除时销毁 VHF 对象。不安装 Hook，不修改游戏进程，不使用第三方键盘身份。

开发环境已更新：确认已有 VS 2022 Build Tools 17.14、MSVC 14.44；项目本地恢复微软官方 WDK/SDK NuGet 10.0.26100.6584。Driver.c 已通过真实 x64 内核编译，输出 .artifacts/driver-build/Driver.obj。已生成 SYS、通过 INF 校验，并生成开发签名目录 CAT；本地开发证书不受系统信任，设备尚未安装或运行。

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


## 驱动包构建与开发签名

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/driver-env/Build-Package.ps1 -SkipRestore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/driver-env/Sign-Development.ps1
```

输出目录 `.artifacts/driver-package`：InputProbeHid.sys、InputProbeHid.inf、InputProbeHid.cat。
INF 使用独立 Root\InputProbeHid 标识，Vhf 设备 lower filter，不更改现有物理键盘设备的过滤器。目标为 x64 Windows 11 22621 及以上，KMDF 1.33。驱动功能仍是原型，不能直接用于生产系统。

本轮实际验证：
- MSVC /kernel /W4 /WX 编译及 x64 内核链接成功。
- WDK InfVerif /u 返回 0。
- dumpbin 确认为 PE32+ x64、Native subsystem；导入 WDFLDR.SYS 和 ntoskrnl.exe。
- SDK MakeCat 生成 CAT；SignTool 使用 SHA256 为 SYS 和 CAT 添加本地开发签名。
- SignTool verify /kp /c 找到目录成员，但因开发证书根不受信任返回失败（不是加载通过）。Get-AuthenticodeSignature 显示 UnknownError，详情同为不受信任的根证书。
- Test-FileCatalog 未能打开此 MakeCat 目录，未计为通过。包 SHA256 清单另外保存在 driver-build/package-hashes.json。
- NuGet WDK 不含 Inf2Cat 可执行文件；本轮使用 SDK MakeCat，不声称完成 Inf2Cat 的平台签名适用性校验。

签名脚本仅在 CurrentUser/My 创建或复用 Input Probe Local Development 非导出私钥证书，有效期六个月；仅导出公钥到 `.artifacts/driver-build/InputProbeDevelopment.cer`。未添加 Root/TrustedPublisher 信任，未修改启动设置。密钥和构建产物不提交 Git。开发签名没有可信时间戳。

剩余：完整驱动运行时审查、独立测试机验证、正式分发签名，以及用户态 IKeySender 设备适配器。当前 App 仍使用原 SendInput 后端。只把 SYS 放在应用旁边不会使新后端生效。
