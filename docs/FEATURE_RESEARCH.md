# 功能调研与本轮变更

2026-11-24 实际读取以下官方页面，网页保存于 .artifacts/features。

| 来源 | 可见功能 | 本项目选择 |
| --- | --- | --- |
| [OP Auto Clicker](https://www.opautoclicker.com/) | 间隔、次数或无限重复、可修改后台热键、保存设置 | 保留间隔/持续重复/热键/保存，不加入用户不需要的次数模式 |
| [Pulover’s Macro Creator](https://www.macrocreator.com/) | 键鼠自动化、录制、脚本生成、多种自动化动作 | 保留可编辑序列；不扩展录制及脚本引擎 |
| [Logitech G HUB](https://www.logitechg.com/en-us/innovation/g-hub.html) | 多配置、分享配置、按应用切换配置 | 新增重命名、深复制、JSON 导入/导出，不恢复目标进程限制 |

这是常见功能归纳，不是市场占有率排名。项目 JSON 不声称兼容厂商宏文件格式。

## 实现

手动初始化：构造窗口只创建 DeferredDdKeyboard；点击初始化后工作线程调用 Initialize → DdNativeKeyboard → DD_btn(0)。成功才设置 IsInitialized、开放编辑与注册快捷键。失败保持锁定且写日志，需重启；关闭期间初始化返回不会重新注册热键。

初始化入口不调用 DD_key，不启动重复。界面和命令均检查可用状态。DD 自身的 Win/鼠标副作用尚未根治。

配置复制包含独立的步骤对象。导入使用已有版本迁移机制，完整校验后追加；导出当前配置、覆盖前对话框确认，禁止选择当前主配置路径。非法按键、间隔、快捷键重复或序列/快捷键冲突均被拒绝。

## 清理

通过 Git 删除 SendInputDemo、Repeat8、DdProbe、两个旧 CMD、Demo 启动脚本，以及被新 README 替代的 DD_BASELINE、HOTKEY_COMPACT_FIX、REPEAT_UI_DESIGN、UI_RAWINPUT_STARTUP。

批量递归删除本地缓存的命令被自动策略拒绝，改用 Git 精确删除受版本控制文件；未清除忽略的 bin/obj 和 .artifacts 缓存。用户配置、日志、DD 依赖、已安装驱动和 SDK 均保留。

## 验证

`dotnet test key_flow.sln --artifacts-path .artifacts/manual-init --verbosity quiet`：Core 19、Windows 25，共 44 项通过。覆盖显式初始化只执行一次且不调用输出、深复制、导入冲突校验、JSON 往返；WPF 编译通过。未实际初始化真实 DD，未测试游戏内输入或手动操作文件对话框。

## 配置区布局调整

保存配置移至“我的配置”右侧，与新建、直接选择卡片放在一起；顶部仅保留置顶。名称及复制/导入/导出/删除集中于配置区，低频操作放在“管理配置”展开项中。快捷键使用紧凑卡片，按键序列紧随其后，去除重复分隔线和多余留白。

界面拆分为 ProfileSelector、HotkeyEditor、ProfileEditor，保留原命令及状态绑定。构建零警告零错误；用假数据渲染实际 WPF 控件检查 700×800 和 540×460 布局，小窗口配置区可滚动，顶部启停与配置区保存可见。本轮仅界面修改，没有初始化 DD 或发送按键。
