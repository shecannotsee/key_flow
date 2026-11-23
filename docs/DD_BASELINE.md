# DD 基准重构记录

2026-11-24：用户反馈 Start-8.cmd 成功，选定 DD HID 为唯一后端。

调用链：WPF 编辑器 → RuntimeController（5 秒启动等待）→ SequenceRunner（快照 + Task.Run）→ PulseLoop → DdKeySender → DD_key。命令行也直接调用 PulseLoop。

PulseLoop 沿用成功程序的 Stopwatch、可取消 WaitHandle 等待、KeyDown/finally KeyUp 和按下到按下间隔计算。不追赶积压时序。默认按住 30ms、总间隔 80ms。原生 DD 适配不换实现。

旧 v1 配置读入后保留 50ms 按住、旧等待+50ms 总间隔；不自动覆盖磁盘。v2 支持 HoldMs，WaitAfterMs 字段保留序列化名称但语义改为总间隔。保存通过临时文件及 File.Replace，保留 .bak。

按用户要求删除 SendInput、自有 VHF、目标进程限制、未接入的暂停命令、过期驱动工具及相关专属测试；原型和历史资料可在 Git 历史中查看。厂商 DD 包和当前配置保留。

验证：18 项测试覆盖时序下限/顺序、运行快照、长按取消、倒计时取消、迁移及备份、DD 故障和释放、快捷键冲突。命令行真实初始化成功，数字 8 的 DD 键码为 208。重构期间旧独立接收窗口测试因失焦中止，未获得实测接收结果；此依赖旧 SendInput 触发器的测试工程已随旧后端移除。界面版目标应用效果待用户复测。
