# VHF 键盘设备开发原型（已构建、未安装）

这是独立的微软 VHF/KMDF HID source driver 原型。它提交标准键盘 HID report，而不是调用 SendInput；它仍是虚拟设备，不是物理 USB 键盘，也不保证目标应用接受。

Driver.c 实现 VhfCreate/VhfStart/VhfReadReportSubmit、管理员及 SYSTEM 访问控制、独占客户端、8 字节输入长度校验，以及客户端句柄清理时全键释放。设备删除时销毁 VHF 对象。不安装 Hook，不修改游戏进程，不使用第三方键盘身份。

开发环境已更新：确认已有 VS 2022 Build Tools 17.14、MSVC 14.44；项目本地恢复微软官方 WDK/SDK NuGet 10.0.26100.6584。Driver.c 已通过真实 x64 内核编译，输出 .artifacts/driver-build/Driver.obj。已生成 SYS、通过 INF 校验，并生成开发签名目录 CAT；本地开发证书不受系统信任，设备尚未安装或运行。

后续必须完成：
1. 链接、打包及自有 Root 设备 INF 已完成；仍需要受目标系统接受的内核签名。
2. 在测试环境创建设备节点并安装、验证 Vhf lower filter 配置。仅使用 PnPUtil 添加驱动包不会创建 Root 设备节点。
3. 在独立测试环境编译、静态分析、Driver Verifier 和真实键盘接收测试。
4. 补充挂起客户端超时释放、多线程卸载竞态和电源生命周期测试。
5. IKeySender 适配器已接入，仍需通过真实设备验证。默认 UI 保留 SendInput。

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

剩余：完整驱动运行时审查、独立测试机验证、正式分发签名。默认 App 仍使用原 SendInput 后端。只把 SYS 放在应用旁边不会使新后端生效。

## 用户态适配及本机验证（2026-11-24）

调用链：App（显式 `--hid` 参数）→ RuntimeController → SequenceRunner → HidDeviceKeySender → HidDeviceTransport → DeviceIoControl → VHF 驱动。默认启动仍使用 SendInput。设备打开失败会显示 Win32 错误并退出，不会静默切换后端。

适配器跟踪 HID usage 状态，支持六个普通键及修饰键，提交成功后才更新状态；关闭时发送全零报告，并在发送失败时仍关闭设备句柄。窗口退出时即使 Stop 失败也执行 Shutdown。

在已经正确安装驱动的测试环境，以管理员身份运行 `src/InputProbe.App/bin/Debug/net8.0-windows/InputProbe.App.exe --hid`。本机尚不满足该前提。

实测 `dotnet test InputProbe.sln --no-restore`：Core 7 项、Windows 6 项通过。其中新增 4 项使用假传输验证报告编码、非法按键和容量限制、失败状态及资源清理、调度取消释放；不代表操作系统收到输入。`dotnet build InputProbe.sln --no-restore`：零警告、零错误。

`Check-Host.ps1` 实测：当前进程非管理员，Secure Boot 注册表状态为开启，内存完整性运行，驱动 Authenticode 检查返回不受信任根证书。此检查不是内核加载测试。未安装驱动、未添加信任证书、未修改启动安全设置；设备接收、时序、长时间运行和游戏兼容性均未验证。
