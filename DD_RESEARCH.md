# DD 社区来源与依赖核验

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
