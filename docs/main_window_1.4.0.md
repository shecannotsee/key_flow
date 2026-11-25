# 主窗口重构 · 1.4.0

## 范围与调用关系

- `MainWindow.xaml` 保留固定 560×400 初始化页；初始化后的窗口继续使用 980×720、最小 760×560。
- 新增 `Controls/MainWorkspace.xaml(.cs)`：紧凑控制栏、参与标签、配置组导航、40px 虚拟化配置列表、所选配置工具栏、底部状态栏。
- 新增 `Themes/MainWindowTheme.xaml`：仅在 MainWorkspace 加载，白色内容区、中性导航底色、蓝色选中与主要操作、绿色活动标记、红色停止及异常；4px 圆角，32px 按钮，4/8/12/16/20px 间距。编辑窗口和共享 AppleTheme 均未更改。
- 新增 `MainViewModel.MainWindow.cs`：只负责筛选、组导航、展示行及只读状态投影。缓存展示行，只有成员变化时替换列表，状态刷新不重建列表。
- `MainViewModel.cs` 增加展示状态通知和当前执行名称记录，原保存、加载、删除、激活、停止命令及执行顺序不变。无 Core/Windows 层修改，无数据结构迁移。

## 功能对应

| 界面入口 | 现有命令 / 数据 |
| --- | --- |
| 启动所选 / 全部停止 | ArmCommand / StopCommand |
| 新建配置 / 保存全部 / 导入 | UseVerifiedPresetCommand / SaveCommand / ImportCommand |
| 编辑 / 单独激活 / 复制 / 导出 / 删除 | OpenEditorCommand / ToggleActivationPoolCommand / DuplicateProfileCommand / ExportCommand / DeleteProfileCommand |
| 新建组 / 编辑组 / 激活组 / 删除组 | NewGroupCommand / EditGroupCommand / ToggleGroupCommand / DeleteGroupCommand |
| 置顶 | IsPinned |
| 当前执行 / 颜色监听 / 排队统计 | IsRunning、IsMonitoring、现有激活池及监视字典 |

搜索及组选择只过滤显示，不改变激活状态。运行时可继续滚动、搜索、浏览分组；业务仍要求锁定的配置选择和修改操作保持原有约束。隐藏所选配置时，操作栏禁用，避免作用于不可见对象。

当前 ArmCommand 是启动所选配置，不能改名为全局“启动监听”。现有配置组仅有成员、名称和参与状态，没有独立执行规则，本轮不新增。快捷键在初始化后按原逻辑监听，全停不取消激活；底部保留真实快捷键状态。当前执行名称取自实际提交给运行控制器的配置；逐项只能确定执行或排队时，显示“执行 / 排队”，不伪称全部正在执行。

## 验证

- `dotnet build key_flow.sln --no-restore` 成功，0 警告、0 错误。
- `dotnet test key_flow.sln --filter "FullyQualifiedName!~ScreenColorTests"`：核心 55/55、Windows 47/47。
- STA 界面检查：80 配置、14 组，980×720 与 760×560；虚拟化、搜索匹配/空结果、组过滤、空配置/空组、选中项、启动命令绑定、启动前后列表边界一致。生成截图并目视检查。
- 原编辑窗口加载测试及初始化三种状态边界检查继续通过。
- 安装产物：`dist/key_flow_setup_1.4.0_x64.exe` 和 `.sha256`。
- 未验证：真实驱动/游戏输入、屏幕取色、不同系统 DPI 的人工测试、安装卸载生命周期。本轮 UI 测试使用无硬件副作用的数据源，不等同于这些实测。
