# 1.3.7：初始化页完整显示

- 初始化窗口由 500×320 改为固定 560×400（WPF 逻辑单位），容纳页头、准备说明、按钮和状态提示；初始化完成后的工作区尺寸逻辑不变。
- 回归测试直接加载 MainWindow.xaml（跳过硬件及个人配置初始化），测量首次使用、准备中、失败提示三种状态下每个文本和按钮的边界，确保位于窗口内容区内。
- 验证：`dotnet test key_flow.sln --filter "FullyQualifiedName!~ScreenColorTests"`，核心 55/55、Windows 45/45 通过。检查了初始化页渲染截图。未执行取色测试或安装/卸载生命周期复测。
- 安装包：`dist/key_flow_setup_1.3.7_x64.exe`，使用现有 NSIS 构建脚本生成，配套 `.sha256` 文件。
