# 驱动开发环境

已复用 VS 2022 Build Tools 17.14.37628.2、MSVC 14.44.35207、MSBuild 17.14.51。此前仅凭 PATH 中没有 cl/msbuild 判断未安装是不准确的，现通过 vswhere 发现。

微软官方 NuGet 包固定为 10.0.26100.6584：
- Microsoft.Windows.WDK.x64
- Microsoft.Windows.SDK.CPP
- Microsoft.Windows.SDK.CPP.x64

包保存在项目 .artifacts/driver-packages，不修改系统 SDK、不需要管理员权限。此版本按微软 VS 2022 兼容建议选取；不混用面向 VS 2026 的 28000 工具集。

运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/driver-env/Build-Check.ps1` 恢复并编译验证。使用 -SkipRestore 可离线重编译。恢复项目仅负责下载 native 包，不构建 .NET 驱动。

验证实测：dotnet restore 成功；cl /kernel /W4 /WX 编译 Driver.c 成功，生成 x64 Driver.obj；MSBuild -version 成功。KMDF 选用 1.33。抑制 C4324（官方 WDF 头文件显式对齐导致的结构填充），其余警告作为错误。

SYS 链接及开发签名已完成（见下文）。未验证：INF 安装、设备启动、游戏输入。尚未更改 Secure Boot、测试签名或系统驱动服务。

官方来源：
- https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install-the-wdk-using-nuget
- https://api.nuget.org/v3/index.json


## 后续包构建

`Build-Package.ps1` 在编译基础上调用内核链接器、InfVerif 和 dumpbin；每次重建删除过期 CAT，避免新二进制搭配旧目录。
`Sign-Development.ps1` 使用本机 SDK 的 MakeCat/SignTool 为原型生成开发签名，具体信任失败与边界见 drivers/InputProbeHid/README.md。本轮已实际生成 .sys/.inf/.cat，尚未安装或加载。

## 本机就绪检查

运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/driver-env/Check-Host.ps1`，读取权限、Secure Boot 注册表状态、内存完整性及驱动签名，保存到 `.artifacts/driver-build/host-readiness.json`。只写本地报告，不修改系统设置。

2026-11-24 实测：非管理员、Secure Boot 开启、内存完整性运行、开发证书根不受信任。当前包不能视为可加载的驱动；管理员启动本身不能解决内核签名问题。用户态适配已实现，使用方法及测试范围见驱动 README。
