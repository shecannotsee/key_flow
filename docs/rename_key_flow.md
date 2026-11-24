# key_flow 命名统一 · 1.2.1

## 当前命名

- 仓库物理路径：`E:\workspace\projects\key_flow`，Git 历史随目录保留。
- 解决方案：`key_flow.sln`。
- 项目/目录/命名空间：`key_flow_app`、`key_flow_core`、`key_flow_windows`，测试为 `key_flow_core_tests` 和 `key_flow_windows_tests`。
- 应用、日志、图标：`key_flow.exe`、`key_flow.log`、`key_flow.ico`、`key_flow.png`。
- 入口：`start_key_flow.cmd`、`tools/start_key_flow.ps1`。
- 安装工具：`tools/build_installer.ps1`、`tools/test_installer.ps1`、`installer/key_flow.nsi`、`installer/prepare_installation.ps1`。
- 安装目录、开始菜单、快捷方式、卸载登记、应用标识和用户界面均使用 `key_flow`。
- 安装包：`dist/key_flow_setup_1.2.1_x64.exe`，并生成 SHA256 校验文件。

C# 类型/方法和 WPF 控件继续使用原有语言命名惯例；只统一项目标识，不改第三方组件名称或系统 API。历史研究文档中的项目引用统一为当前标识，旧测试记录仍是历史结果。

## 用户数据兼容

当前用户目录为 `%LOCALAPPDATA%/key_flow`。首次打开时优先从旧产品的用户目录复制配置；仅在新配置不存在时复制，不删除来源、不覆盖新配置。迁移代码和测试里的历史名称是兼容性常量，不能作为普通显示文字批量替换。

现有安装副本与旧日志没有被卸载、改写或删除；新旧产品的安装登记独立。确认新配置迁移正常后，可从系统设置卸载旧副本。

## 实际验证

- `dotnet build src/key_flow_app/key_flow_app.csproj -c Release --verbosity quiet`：零警告、零错误。
- `tools/start_key_flow.ps1 -CheckOnly`：程序和本地依赖路径正确，未执行真实按键。
- `dotnet test key_flow.sln --verbosity quiet --logger "trx;LogFilePrefix=rename" --results-directory .artifacts/rename_tests`：Core 43 项通过；Windows 41 项通过、1 项失败，总计 84/85 通过。
- 新增旧配置迁移测试通过，覆盖优先读取旧用户配置、不覆盖新配置、保留来源。
- 未通过项为 `ScreenColorTests.ReadsActualVisibleWindowPixels`：真实屏幕颜色读取后的鼠标点击采样等待超时；单独复测仍失败。未证明该失败与改名有关，也未将失败隐藏或跳过。其余屏幕变化触发、热键及核心逻辑测试通过。
- `git diff --check` 通过，旧名称扫描仅剩兼容代码和测试。
- 本轮没有向 GitHub 推送，也没有执行新版安装/卸载生命周期测试。

## 本地迁移残留

仓库、源码和构建资源已迁移。旧路径下仅剩空缓存目录及空元数据目录；初次目录移动时其中一个缓存目录被进程占用，随后清理空目录被自动审批拒绝，因而保留。这些残留不在新 Git 仓库中，也不影响构建。
