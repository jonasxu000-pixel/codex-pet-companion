# Codex Pet Companion / Codex 宠物额度助手

基于 [ognjeeen/codex-usage-widget](https://github.com/ognjeeen/codex-usage-widget) 的非官方 Windows 辅助工具。
保留 Codex 官方宠物，在它旁边显示额度和本地任务活动；不修改官方程序或宠物素材。

## 第一版功能

- 宠物旁常驻五小时剩余额度；悬停宠物或数字展开详情，点击数字固定/收起。
- 显示周额度、重置倒计时、额度更新时间；查询失败保留上次值并明确标为“上次”。
- 通过本机 Codex app-server 查询真实额度，每分钟刷新，也可手动刷新。
- 读取最多 24 个最近更新的本地任务日志尾部，跟随最新活动；可选择具体任务，也能识别数周前创建、今天继续使用的对话。
- 区分任务开始、工具调用/返回、文件修改、完成与错误；超过 90 秒未见新活动显示“状态待确认”，不判定卡死。
- 只展示明确标注为公开进度说明的内容，不展示内部推理或工具输出。
- 仅 Codex 与官方宠物同时开启时运行；关闭 Codex 或宠物后，小助手和自己的额度查询子进程会退出。
- 托盘右键可隐藏、刷新、退出，或启用“自动随 Codex 启动”。

## 使用

解压完整 Windows 发布包，双击 **install.cmd**。它会安装到 `%LOCALAPPDATA%\Programs\CodexPetCompanion\版本号`，注册当前用户的 Windows 计划任务并启动小助手，无需管理员权限。安装完成后可以移动下载目录；发布包自带 .NET 运行时。

也可以直接运行 **CodexPetCompanion.exe** 临时试用；仅双击 EXE 不会自动注册启动项。
需要已经登录的 Codex 桌面版；优先自动发现桌面版捆绑的 CLI，其次检查 PATH。
非标准安装可设置 `CODEX_USAGE_WIDGET_CODEX_PATH` 指向实际的 Codex CLI。

先在 Codex 中开启官方宠物。本工具不会自动修改 Codex 设置。
右键系统托盘中的小助手图标可以退出或调整启动选项。使用 Windows 已有的 Codex 应用激活事件触发，无每分钟轮询、无独立常驻监控、不在登录或解锁时无条件启动。Codex 打开且官方宠物已开启才显示小助手；关闭任一个后，连续约 3 秒确认即退出并释放自己的额度查询进程。

0.1.4 修复慢启动和窗口隐藏造成的漏启动/误退出：收到激活事件后，在创建界面和请求额度前等待窗口与宠物就绪。Codex 根进程已启动但窗口仍在加载时，最多等待 5 分钟；Codex 退出即停止等待，未发现应用或窗口已就绪但宠物关闭时最多等 20 秒。启动等待属于一次有期限的启动过程，不是全天常驻监控。运行中跟踪根进程，不把窗口最小化、隐藏或重建当成应用退出；宠物状态文件临时不可读时，最多沿用 10 秒最近有效位置再确认。计划任务可在异常退出后重试两次，正常关闭和用户主动退出不会触发这类重试。预检查结果和退出条件均写入本地日志。

额度查询子进程仅在刷新时短暂运行，成功、失败或取消后都释放，不在两次刷新之间常驻。

收起详情时不读取任务日志；第一次展开只枚举文件信息以找出最近更新的对话（最多 50,000 个候选文件），读取最多 24 个文件的有限尾部。之后用 Windows 文件变化通知更新候选，通知队列最多 256 个路径，不循环扫描整个历史目录；关闭助手时释放监听。未变化的标题和宠物位置使用缓存。不会强制清空工作集或用内存整理来美化占用数字。

目前启动事件来自 Windows 商店版 Codex 的 TWinUI/AppModel 日志（本机已开启）。若直接运行内部 EXE 绕过系统激活，或在 Codex 已打开后才手动开启宠物，可能没有新激活事件；此时需手动启动小助手或通过系统重新打开 Codex。为保持不常驻，本版不使用后台轮询兜底。

托盘“退出本次，下次打开 Codex 时恢复”会暂停当前 Codex 实例的自动恢复；重新打开 Codex 或手动启动小助手即可恢复。取消“自动随 Codex 启动”会禁用计划任务。

0.1.1 使用暖白界面与统一的任务选择器，额度条保持原位，详情单独淡入展开。宠物靠左时额度条自动放在右边。动画遵循 Windows 的界面动画开关。

0.1.4 延续暖白风格，统一圆角进度条和任务区域；剩余额度不超过 20% / 10% 时用淡琥珀色 / 陶土色提示，不弹出警报。官方宠物形象继续由 Codex 提供。

启动日志位于 `%LOCALAPPDATA%\CodexPetCompanion\logs`，记录版本、启动方式和 Codex 出现/关闭，不记录对话内容。计划任务名为 `CodexPetCompanion-<当前用户 SID>`，不存储密码，不使用管理员权限。

## 数据与限制

- 不读取 `auth.json` 或登录令牌。认证由本机 Codex CLI 处理。
- 只读 `.codex-global-state.json` 中的宠物位置、`sessions` 中近期任务记录和 `session_index.jsonl` 中的任务标题；不改写这些文件。
- 无模型生成请求、遥测、第三方后端或对话上传。额度查询仍需要 Codex 的网络连接。
- 任务标题和公开进度可能包含你的私人信息，只在本地显示；分享截图前请自行检查。
- 额度错误不等于任务断网；日志安静不等于任务停止。第一版不尝试读取内部思维或控制官方宠物动作。
- 新版本 Codex 改动本地日志或位置格式后，可能需要适配。暂不承诺所有多显示器/DPI组合、远程/云端任务和长时间未更新的旧任务都能完整识别。
- 卸载：关闭托盘自动启动选项、退出小助手，在 Windows 任务计划程序中删除本工具的 `CodexPetCompanion-<当前用户 SID>` 任务，再删除程序目录；不影响 Codex。旧版同名 HKCU Run 值在新计划任务注册成功后迁移移除。

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
