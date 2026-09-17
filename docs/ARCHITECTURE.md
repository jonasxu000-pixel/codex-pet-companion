## Companion 0.1.3：运行资源边界

- CompanionUsageProvider 仅按需启动一次额度请求，使用 await using 在成功、异常和取消路径释放 app-server 会话；不查询未显示的 token activity，也不保留额度查询子进程。

- 替换 0.1.2 的定时、登录、解锁触发器，只订阅 Windows 已有的 Codex 激活事件：TWinUI 1621 / ApplicationId、AppModel-Runtime 201 / ApplicationName；固定匹配商店版 AUMID，不开启新的进程审计。
- 正常模式在创建 WPF、读取任务日志、启动额度 CLI 前检查 Codex 与宠物。事件模式只为启动窗口提供有上限的 20 秒等待，未开启宠物则结束。
- 运行中连续三个检测周期不满足 Codex+宠物条件即关闭，取消未完成的额度请求并释放自有 CLI；不留下后台监控进程。经典 --classic 模式保持上游行为，安装任务不使用它。
- 伴随界面不加载经典界面的资源字典；收起时不读取任务日志。活动初始尾部及增量积压限制 256 KiB，标题索引按修改时间/长度缓存；桌面 PID 和宠物状态文件也缓存，避免不必要的重复扫描与解析。
- 任务的事件订阅由 Windows 任务计划服务承担，没有本工具的常驻 watcher。官方宠物在已打开的 Codex 内手动开启可能不产生系统激活事件，此场景需要手动启动助手，不能宣称无轮询下覆盖所有入口。

## Companion 0.1.2

- 用当前用户的交互式 Windows 计划任务代替单一 HKCU Run 启动项：登录、解锁触发，另以一分钟间隔补查；单实例、允许电池运行、无运行时长上限、不唤醒电脑。
- `--recover` 在未发现同一 Windows 会话内的 Codex 桌面进程时退出，不请求额度；已有小助手时由互斥锁拒绝重复实例。
- CompanionRecoveryGate 保存用户主动退出时间，当前 Codex 实例不立即重启小助手，新 Codex 实例可恢复。手动启动清除暂停。
- CompanionScheduledStartup 通过系统 Task Scheduler COM 服务管理本工具任务，避免依赖安装进程的虚拟化 HKCU 视图；取消托盘自动启动选项会禁用任务。
- 本机只读对照发现：Codex 内的工具进程与计划任务进程对相同 LocalAppData 路径读到不同文件，前者实际位于应用 LocalCache；同一注册表探针也未在后者可见。不能将 Codex 内部读取 Run 值等同于 Windows 登录自动启动验收。
- Windows 触发机制参考：[LogonTrigger](https://learn.microsoft.com/en-us/windows/win32/taskschd/logontrigger)、[InteractiveToken](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-logontype-principaltype-element)。

## Companion 0.1.1

- CompanionWindow 的固定尺寸额度条与详情 Popup 分离，避免展开时移动额度入口；Popup 保留悬停、固定和任务选择交互。
- CompanionDesktopTracker.BadgePosition 负责边缘避让，左侧空间不足时放到宠物右侧。
- scripts/install.ps1 随原 publish.ps1 流程打包，安装到用户本地 Programs 下的版本目录，只注册本工具的 HKCU Run 项，不修改 Codex。
- App 记录启动方式和版本，监视器记录桌面状态变化；短暂读取错误记录错误类型并在下次轮询重试。

# Architecture

## Companion 0.1.0 extension

Normal startup now composes `CompanionWindow`; `--classic` preserves the upstream window.
The companion reuses `UsageMonitor` and the app-server provider, requesting quota only
while Codex Desktop is detected. `CompanionDesktopTracker` reads the pet position and
owns Windows startup registration. `CompanionActivityReader` reads bounded tails of
recent local sessions and public commentary; it does not display reasoning or tool output.
The new companion does not install lifecycle hooks. The descriptions below cover the
retained upstream modules and classic mode unless noted otherwise.

The solution uses explicit composition in `App.xaml.cs` and keeps framework,
application, domain, and infrastructure responsibilities separate without a
dependency-injection package.

## Project layout

```text
src/CodexUsageWidget/
├── Application/             Refresh orchestration, activity state and presentation formatting
├── Domain/                  Rate-limit, credit, spend-control and activity models
├── Infrastructure/
│   ├── Codex/               App-server integration plus lifecycle-hook parsing and local IPC
│   ├── Logging/             Local file diagnostics
│   ├── Settings/            Persistent preferences and pending reset attempts
│   └── Windows/             Tray icon and taskbar Win32 integration
└── Views/                   WPF shell, presentation models and focused controls
tests/CodexUsageWidget.Tests/ Unit tests for parsing, formatting and persistence
```

## Runtime flow

1. `Program` handles activity-hook/configuration command modes before WPF startup. A normal
   launch from a temporary ZIP location is copied atomically to a versioned per-user app
   directory and relaunched; `App` then acquires the single-instance mutex and constructs
   the widget object graph.
2. `UsageMonitor` owns refresh scheduling, timeout handling and refresh coalescing.
3. `CodexUsageProvider` coordinates required rate-limit reads and optional token-activity reads.
4. `RateLimitResetUseCase` coordinates explicit redemption, normalizes failures for the view,
   and waits for a fresh usage read after every definitive outcome.
5. `CodexRateLimitResetConsumer` owns the app-server request while
   `RateLimitResetAttemptStore` durably keeps one idempotency key for retries until the server
   returns a definitive outcome.
6. `CodexAppServerSession` owns initialized app-server connection lifetime.
7. `JsonRpcConnection` owns stdin/stdout request correlation and process lifetime.
8. Endpoint-specific parsers convert Codex payloads into domain records.
9. A path-independent PowerShell hook bridge forwards minimal lifecycle signals to
   `CodexActivityPipeSignalSource` over a current-user-only named pipe;
   `CodexActivityMonitor` owns one active turn per session and emits only final boolean
   transitions.
10. `CodexActivityHookSetupService` coordinates reviewable hook-file changes and reads
   trust state through `hooks/list`; `CodexHookTrustStatusParser` owns the protocol shape.
11. `ActivityHookSetupControl` presents setup status inside Settings while a separate review
   dialog shows the exact proposed file content before installation or removal.
12. `UsageWidgetViewModel` maps snapshots to immutable presentation state.
13. `AppThemeController` applies the saved system, light, or dark theme plus the selected
    accent palette, and observes Windows theme changes without leaking registry access into
    view code.
14. `AppLanguageController` resolves the saved system, English, or Simplified Chinese
    preference, while standard .NET resources and a notifying WPF binding refresh existing UI.
15. `TimeTextFormatter` applies the saved Windows regional, 24-hour, or 12-hour clock
    preference to user-visible times while protocol and diagnostic timestamps remain unchanged.
16. `MainWindow` remains a window-lifecycle shell while the Settings window coordinates
    activity-hook setup plus immediate theme, accent, time-format, widget-layout,
    displayed-limit, and Windows startup preferences. Focused user controls render compact,
    detailed, and repeated limit-row content.

## Dependency direction

- Domain types do not depend on WPF, WinForms, process APIs, or JSON.
- Application orchestration depends on domain types and the `IUsageProvider` port.
- Infrastructure implements that port and owns OS/external-process details.
- Views consume application/domain state and do not parse protocol payloads.

## Reliability decisions

- All transport awaits use `ConfigureAwait(false)` so shutdown cannot deadlock the
  WPF UI thread.
- Failed app-server startup is disposed before a later refresh reconnects.
- Optional token-activity failures degrade only the detailed activity section; core
  rate-limit monitoring remains available.
- Usage preview mode owns synthetic reset credits and redemption outcomes, so UI tests and
  manual preview checks never consume a real account reset.
- Redemption writes its idempotency key before contacting Codex, preserves it across app
  restarts after an uncertain response, and removes it only after a definitive outcome.
- Post-redemption refresh waits behind an active usage read instead of discarding the request,
  so the view cannot offer a consumed credit again from a stale snapshot.
- A semaphore prevents concurrent refreshes and a mutex prevents duplicate apps.
- Activity hook IPC is bounded and local to the current Windows user. The hook path does not
  depend on the release extraction directory and does not start WPF. Accepted clients are
  consumed in order with a per-client read timeout, while separate pipe instances keep parallel
  Codex sessions connectable. Duplicate events are idempotent, a new turn replaces an orphaned
  turn in the same session, late completion for the replaced turn is ignored, and session end
  removes only that session.
- UI hook setup reuses the same compare-before-write configuration plan as the CLI flow.
  Codex remains the owner of hook trust; the widget only reads trust state and opens the
  interactive CLI for the user's explicit `/hooks` approval.
- Activity state is not persisted or reconstructed with private transcript/database polling.
  A later turn in the same session recovers missing cleanup; a hard Codex termination with no
  later lifecycle event is cleared by restarting the widget.
- Unhandled exceptions and CLI diagnostics are recorded locally for support.
- Publish trimming is disabled because WPF is not a safe trimming boundary.

## Extending the app

- Add another usage source by implementing `IUsageProvider`.
- Add new Codex payload variants to the parser for that endpoint with fixture-based tests.
- Keep reusable presentation state in `Views/ViewModels` and focused visual sections in
  `Views/Controls`; `MainWindow` should not absorb endpoint or rendering responsibilities.
- Keep Win32 calls under `Infrastructure/Windows` and UI rendering under `Views`.
- Extend user preferences through the Settings window. Keep appearance application in
  `AppThemeController` instead of placing theme decisions in individual views.
- Avoid placing persistence, process management, or protocol parsing in code-behind.
