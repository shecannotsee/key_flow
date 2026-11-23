# DD 社区来源与依赖核验

本文保留来源、签名和早期测试的历史证据。当前实现与入口以根目录 README.md 和 DD_BASELINE.md 为准；其他输入路线已删除。

核验日期：2026-11-24。仅下载和静态检查，未加载 DLL、联网认证或安装驱动。

## 找到的来源

- 发布仓库：https://github.com/ddxoft/master 。仓库包含 README 和 hid.7xxxx.7z，没有公开内核源码，也没有仓库级开源许可证。README 明确免费版加载时联网认证。
- Go 封装：https://github.com/rpdg/godd ，Apache-2.0。指向上述发布仓库，说明 DLL 与驱动必须按版本配套；其设备实测是作者环境结果，不是本项目测试。
- C# 界面项目：https://github.com/shiningnight/FullScreenKeyboardReborn ，MIT，README 标记 WIP，同样引用 DD 发布仓库。不能据此称 DD 驱动开源或该项目已经成熟稳定。

## 已取得的依赖

下载 URL：https://raw.githubusercontent.com/ddxoft/master/master/hid.7xxxx.7z

本地：`.artifacts/dd-research/hid.7xxxx.7z`

SHA256：`728632DECC9339D780C708A92F6185E86E04DE5E59538AA1ACA9AC68563B1B1F`

包内含 `hid.72000.dll` 和 `hid.drv/ddhid72000.sys/.inf/.cat`，另含 `dd.71000.dll`。两种 DLL 不能当成同一后端混用。已选择性解压 HID 文件和 C# 示例，未运行包内程序。二进制及厂商示例保留在忽略的 artifacts 目录，不加入项目源码或重新分发。

## 实际检查结果

- Get-AuthenticodeSignature：HID DLL、SYS、CAT 均为 Valid。
- DLL 签名主体：重庆貔赑貅软件科技工作室 (王真)。
- SYS 和 CAT 签名主体：Microsoft Windows Hardware Compatibility Publisher。
- Windows SDK SignTool `verify /kp /c ddhid72000.cat ddhid72000.sys`：退出码 0，Successfully verified。
- INF 设备 ID 为 `ddhid72000\HID_DEVICE`，服务为 ddhid72000。
- install.bat 调用包内 ddc.exe，不是仅执行 pnputil。尚未审查或执行安装器。

这些检查确认签名链及目录成员；不证明 Windows 当前安全策略下一定能启动该设备，也不证明程序行为安全或目标游戏接受输入。

## 接入依据

已阅读包内 Example/App_csharp/CDD.cs 和 Form1.cs：动态解析导出；`DD_btn(0)` 初始化，示例要求返回 1；`DD_key(int ddcode, int flag)` 提交键盘事件；`DD_todc(int vkcode)` 用于键码转换。示例中 todc 的委托赋值被注释，不能直接复制后宣称可用。后续需检查真实 DLL 的导出并核实转换及按键调用返回约定。

现有接口可复用：IKeySender → 独立 DD 适配器 → 厂商 DLL → 配套 HID 驱动。停止、循环、间隔继续由 Core 调度。不得静默回退到 SendInput 后把结果当成 DD 成功。

未完成：DD 适配器、安装器审查、设备安装和启动、独立接收窗口测试、剑三兼容性测试。此前自写 VHF 驱动的证书阻塞不能直接套用到此签名包。

## 后续接入及实测（2026-11-24）

上文为研究时点记录；本节更新实际进度。

- 已实现 DdNativeKeyboard / DdKeySender：显式路径加载、DD_btn(0) 初始化、DD_todc 转换、DD_key 按下/释放，保留 DLL 至进程退出；后端每进程仅初始化一次。
- 接入现有 IKeySender / SequenceRunner / RuntimeController。DD 失败不会回退 SendInput。追踪可能已按下的键，释放失败重试并继续清理其他键，运行时记录清理失败而不产生未观察的任务异常。无法保证原生调用挂死、断电或强制终止进程时释放。
- 新入口 `Start-InputProbe-DD.cmd`，使用本地已核验 hid.72000.dll；UI 显示 DD HID。原 SendInput 和自有 VHF 入口保留。
- dumpbin 检查真实 DLL 导出包含 DD_btn、DD_key、DD_todc。
- 安装前真实探针 DD_btn(0) 返回 0，系统未发现 ddhid72000 设备或服务。
- ddc.exe Authenticode 为 Valid，之后以 RunAs 启动厂商安装器。服务 ddhid72000 已 Running；枚举到 HID Keyboard Device 等 9 个 HID 子设备，状态 OK。安装器进程曾继续驻留，未将进程退出码作为成功依据。
- 安装后 `dotnet run --project tools/DdProbe --no-build -- .artifacts/dd-research/hid.72000.dll` 返回 0：初始化成功，VK D1/A/Shift/Ctrl/Alt/Left 对应 DD 201/401/500/600/602/710。此探针没有发送按键。
- `dotnet test InputProbe.sln --no-restore`：Core 7、Windows 16，共 23 项通过。新增 5 项使用假 DD 接口验证组合键、失败转换、释放重试、取消与运行时错误日志，不代表真实键盘输入测试。
- `dotnet build InputProbe.sln --no-restore`：零警告、零错误。
- 独立 WPF 接收窗口支持传递 --dd DLL路径给隔离 App；本次执行因接收窗口失焦在发启动键前终止，退出码 1。真实按键接收、时序和剑三兼容性仍未验证。

已安装的是厂商完整 HID 包，包含键盘、鼠标、触摸等多个集合，并非仅一个键盘设备。没有修改 Secure Boot、内存完整性或测试签名。需要卸载时使用配套厂商 `hid.drv/uninstall.bat`（调用 ddc -u，需要管理员）；未执行卸载。

手工验证：关闭旧 App，运行根目录 Start-InputProbe-DD.cmd，确认界面模式为 DD HID；在记事本中使用配置的开始键（默认 F8），停止键默认 F9，紧急停止 Ctrl+Shift+F10。先确认普通窗口收到按键，再独立测试目标应用。日志位于 App 输出目录 input-probe.log。
