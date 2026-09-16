# Codex Pet Companion / Codex 宠物额度助手

基于 [ognjeeen/codex-usage-widget](https://github.com/ognjeeen/codex-usage-widget) 的非官方 Windows 辅助工具。
保留 Codex 官方宠物，在它旁边显示额度和本地任务活动；不修改官方程序或宠物素材。

## 第一版功能

- 宠物旁常驻五小时剩余额度；悬停宠物或数字展开详情，点击数字固定/收起。
- 显示周额度、重置倒计时、额度更新时间；查询失败保留上次值并明确标为“上次”。
- 通过本机 Codex app-server 查询真实额度，每分钟刷新，也可手动刷新。
- 读取近七天内最多 24 个最近本地任务的日志尾部，跟随最新活动；可选择具体任务。
- 区分任务开始、工具调用/返回、文件修改、完成与错误；超过 90 秒未见新活动显示“状态待确认”，不判定卡死。
- 只展示明确标注为公开进度说明的内容，不展示内部推理或工具输出。
- Codex 关闭时隐藏，重新打开后恢复；官方宠物未开启时显示独立卡片。
- 托盘右键可隐藏、刷新、退出，或启用“登录 Windows 后待命”。

## 使用

解压完整 Windows 发布包，双击 **install.cmd**。它会安装到 `%LOCALAPPDATA%\Programs\CodexPetCompanion\版本号`，注册当前用户的 Windows 登录启动项并启动小助手，无需管理员权限。安装完成后可以移动下载目录；发布包自带 .NET 运行时。

也可以直接运行 **CodexPetCompanion.exe** 临时试用；仅双击 EXE 不会自动注册启动项。
需要已经登录的 Codex 桌面版；优先自动发现桌面版捆绑的 CLI，其次检查 PATH。
非标准安装可设置 `CODEX_USAGE_WIDGET_CODEX_PATH` 指向实际的 Codex CLI。

先在 Codex 中开启官方宠物。本工具不会自动修改 Codex 设置。
右键系统托盘中的小助手图标可以退出或调整启动选项。登录后小助手先在托盘待命，每秒检查 Codex 是否打开，不需要发送聊天消息。主动选择“退出小助手”后，本次登录内需手动重开；下次 Windows 登录按启动项运行。

0.1.1 使用暖白界面与统一的任务选择器，额度条保持原位，详情单独淡入展开。宠物靠左时额度条自动放在右边。动画遵循 Windows 的界面动画开关。

启动日志位于 `%LOCALAPPDATA%\CodexPetCompanion\logs`，记录版本、启动方式和 Codex 出现/关闭，不记录对话内容。Windows 若在任务管理器中禁用了启动项，需要在那里重新启用。

## 数据与限制

- 不读取 `auth.json` 或登录令牌。认证由本机 Codex CLI 处理。
- 只读 `.codex-global-state.json` 中的宠物位置、`sessions` 中近期任务记录和 `session_index.jsonl` 中的任务标题；不改写这些文件。
- 无模型生成请求、遥测、第三方后端或对话上传。额度查询仍需要 Codex 的网络连接。
- 任务标题和公开进度可能包含你的私人信息，只在本地显示；分享截图前请自行检查。
- 额度错误不等于任务断网；日志安静不等于任务停止。第一版不尝试读取内部思维或控制官方宠物动作。
- 新版本 Codex 改动本地日志或位置格式后，可能需要适配。暂不承诺所有多显示器/DPI组合、远程/云端任务和长时间未更新的旧任务都能完整识别。
- Windows 登录待命项：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexPetCompanion`。关闭该选项、退出程序，再删除程序目录即可卸载；不影响 Codex。

## 开发与验证

使用 `global.json` 指定的 .NET 10 SDK：

```powershell
dotnet restore CodexUsageWidget.slnx
dotnet build CodexUsageWidget.slnx -c Release --no-restore
dotnet test CodexUsageWidget.slnx -c Release --no-build
.\scripts\publish.ps1 -Runtime win-x64
```

正常启动使用新的宠物伴随界面；`--classic` 保留上游经典额度窗口供排查。
`CodexPetCompanion.exe --diagnose report.json` 写入额度/位置/活动计数诊断，不含令牌或对话内容。

主要新增模块：
- `Infrastructure/Codex/CompanionActivityReader.cs`：本地活动读取。
- `Infrastructure/Windows/CompanionDesktopTracker.cs`：桌面进程、宠物位置、启动项。
- `Views/CompanionWindow.xaml`：伴随浮窗。

## 来源与许可

本项目基于 Ognjen Marinković 的 **Codex Usage Widget**，上游快照 `fdfe6f2`（原版本 1.7.0）。
复用了额度通信、解析、部分 Windows 基础设施和测试，保留原始 [MIT 许可证](LICENSE)。
新增代码同样使用 MIT 许可。

宠物旁悬停交互参考了 [LongfeiLi1/codex-pet-quota](https://github.com/LongfeiLi1/codex-pet-quota) 的公开设计，相关新增实现独立编写，未复制其源文件。
官方宠物由用户已经安装的 Codex 提供，本仓库不分发官方宠物素材，也不代表 OpenAI。

上游原始说明保留在 [docs/UPSTREAM_README.md](docs/UPSTREAM_README.md)。
