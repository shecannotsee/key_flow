# Apple 风格界面规范

本项目参考 Apple Human Interface Guidelines（HIG）整理，官方入口：

- https://developer.apple.com/design/human-interface-guidelines/
- https://developer.apple.com/design/human-interface-guidelines/layout
- https://developer.apple.com/design/human-interface-guidelines/color
- https://developer.apple.com/design/human-interface-guidelines/typography
- https://developer.apple.com/design/human-interface-guidelines/controls

## 采用的原则

1. **清晰**：页面只保留当前任务需要的信息，标题、分组和状态有稳定层级。
2. **内容优先**：控件服务于配置和运行状态，减少装饰、边框和无意义的面板嵌套。
3. **一致**：统一 8pt 间距节奏、控件高度、圆角、状态色和交互反馈。
4. **直接操作**：启动、暂停、停止、保存等常用操作放在当前上下文附近，避免深层菜单。
5. **反馈**：每次启用、停止、失焦和保存都给出可读的状态文本，不只依赖颜色。
6. **容错**：危险操作明显区分；停止操作随时可见；窗口关闭时先清理输入状态。
7. **可访问性**：正文使用系统无衬线字体，文字与背景保持足够对比，状态不只用颜色表达。

## 本项目的 Windows 适配

- WPF 没有 SF Pro，使用 `Segoe UI Variable Text`，不存在时回退到 `Segoe UI`。
- 使用浅色中性背景和蓝色主操作色，保持桌面工具的可读性。
- 卡片仅用于真正独立的功能分组，圆角 12px；页面不使用层层嵌套卡片。
- 主窗口保持三列信息架构：配置、编辑、状态；不使用大面积装饰图或渐变。
- 颜色语义：蓝色为主操作，绿色为已就绪，橙色为等待/需要确认，红色只用于停止和异常。
- 输入序列表格使用紧凑行高，方便重复配置和扫描。

## 组件基线

| 项目 | 基线 |
|---|---|
| 页面边距 | 24px |
| 模块间距 | 16px |
| 控件内边距 | 10px / 14px |
| 常规控件高度 | 36px |
| 小圆角 | 8px |
| 卡片圆角 | 12px |
| 正文 | 14px |
| 页面标题 | 28px |
| 辅助文字 | 12px |

## 不采用的做法

- 不复制 Apple 商标、图标或专有视觉资产。
- 不使用纯装饰性渐变、发光、过度阴影和大面积拟物效果。
- 不把“SendInput 调用成功”伪装成“游戏执行成功”，诊断信息必须准确。
