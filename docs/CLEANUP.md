# 项目整理记录

2026-11-24：根目录合并为一个 Start-InputProbe.cmd，默认 DD，可通过 Backend 和 Elevated 参数选择原 SendInput 或管理员启动。启动逻辑集中到 tools/Start-InputProbe.ps1。

DD DLL 从临时研究目录复制到 dependencies/dd，已实际使用新路径运行 DdProbe，初始化及六个键码转换成功。原发布包保留用于依赖追溯和重新安装。

八份根目录设计、审计和研究文档移到 docs。删除没有外部调用引用的 LowLevelKeyboardHook 类和 ProfileSidebar 控件；保留所有现用输入后端、SDK、驱动和用户配置。

验证：普通输出目录构建因运行中的 InputProbe.App 锁定 DLL 而失败；使用 `dotnet test InputProbe.sln --artifacts-path .artifacts/cleanup-validation --verbosity quiet` 隔离构建，23 项测试通过。统一脚本 DD 默认参数及 SendInput+Elevated 的 CheckOnly 检查通过，未额外启动应用实例。

未完成：历史测试副本批量删除被执行工具策略拒绝，副本仍在 .artifacts。未绕过该拒绝。运行中的 App 没有被关闭，因此默认输出目录尚未更新删除旧控件后的程序集；关闭 App 后执行 dotnet build InputProbe.sln 即可更新。启动脚本和 DLL 新路径已生效。
