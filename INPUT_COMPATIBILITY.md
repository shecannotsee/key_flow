# 输入兼容性对照：2026-11-24

## 本轮修改
序列执行路径改为 KeyDown -> 可取消 Task.Delay(50) -> finally KeyUp -> 原步骤间隔。
50ms 是请求的保持时间，线程调度会带来误差，不是操作系统或游戏实测接收时间。
保留扫描码 SendInput 后端；即时 Press API 仍保留，但序列不再调用它。
旧配置及间隔不变，无需迁移。按住阶段停止会取消等待并释放按键。

## 公开资料
- Microsoft SendInput：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
  只允许向同级或更低完整性级别应用注入；返回值及 GetLastError 无法直接指出 UIPI 阻止原因。现有物理键状态也会影响输入。
- AutoHotkey SetKeyDelay：https://doggy8088.github.io/AutoHotkeyDocs/docs/lib/SetKeyDelay.htm
  明确提到部分游戏需要按下到松开的延迟；AHK SendInput 模式不遵循 SetKeyDelay。这里采用两次原生 SendInput 之间异步等待，不是依赖 AHK 设置。
- PyDirectInput 源码：https://github.com/learncodebygaming/pydirectinput/blob/master/pydirectinput/__init__.py
  使用扫描码及 SendInput；替换成这个库不意味着获得硬件输入，也不保证剑三兼容。
- 用户视频：https://www.bilibili.com/video/BV1MQsb6tEcg/
  已读取 B 站元数据：江影任浮生《【剑网3】大肥鱼按键5.4版本教程（新增宏录制功能）》。简介没有源码；本轮未观看视频或取得其实现，不能确认作者使用的时长及发送参数。

## 下一步对照
同一绑定按键，在前台普通窗口、剑三分别测试。记录客户端版本、窗口模式、工具与客户端权限是否一致、日志 Down/Up 和实际动作。先按当前默认 50ms 测试；如果普通窗口成功而游戏不响应，仍需区分权限、焦点、游戏键位及读取策略。API 成功不等于游戏收到。
不把 PostMessage 当作硬件输入替代；不安装来源不明驱动或下载执行视频作者的未知软件。

## 验证范围
Core 测试覆盖保持时间（调用层）及按住期间取消后释放。游戏客户端接收效果未验证。
