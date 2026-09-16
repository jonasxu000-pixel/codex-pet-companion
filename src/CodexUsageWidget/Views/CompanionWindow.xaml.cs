using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageWidget.Application;
using CodexUsageWidget.Domain;
using CodexUsageWidget.Infrastructure.Codex;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Views;

public partial class CompanionWindow : Window, IAsyncDisposable
{
    private readonly UsageMonitor _usage = new(new CodexUsageProvider(new CodexAppServerSession()), requestTimeout: TimeSpan.FromSeconds(20));
    private readonly CompanionActivityReader _activity = new(CompanionDesktopTracker.CodexHome);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private CompanionAnchor? _anchor;
    private UsageSnapshot? _snapshot;
    private IReadOnlyList<CompanionActivity> _sessions = [];
    private DateTime _lastRead, _lastQuota, _lastHover;
    private bool _busy, _refreshing, _pinned, _expanded, _hidden, _closing;
    private string? _selectedId;
    private string? _quotaError;

    public CompanionWindow()
    {
        InitializeComponent();
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Codex 小助手 · 等待 Codex",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information,
            Visible = true
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(() => { _hidden = !_hidden; Tick(); }));
        menu.Items.Add("固定 / 收起详情", null, (_, _) => Dispatcher.Invoke(TogglePin));
        menu.Items.Add("刷新额度", null, (_, _) => Dispatcher.Invoke(() => _ = RefreshQuotaAsync()));
        var startup = new System.Windows.Forms.ToolStripMenuItem("登录 Windows 后待命") { Checked = CompanionDesktopTracker.StartupEnabled(), CheckOnClick = true };
        startup.Click += (_, _) =>
        {
            try { CompanionDesktopTracker.SetStartup(startup.Checked); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { startup.Checked = !startup.Checked; System.Windows.MessageBox.Show("无法保存启动设置。", "Codex 小助手"); }
        };
        menu.Items.Add(startup);
        menu.Items.Add("退出小助手", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(() => { _hidden = false; TogglePin(); }); };
        _usage.SnapshotUpdated += snapshot => Dispatcher.InvokeAsync(() => { _snapshot = snapshot; _quotaError = null; Render(); });
        _usage.RefreshFailed += _ => Dispatcher.InvokeAsync(() => { _quotaError = "额度暂未刷新 · 请检查连接或登录"; Render(); });
        _timer.Tick += (_, _) => Tick();
        Closed += async (_, _) =>
        {
            await DisposeAsync();
            System.Windows.Application.Current.Shutdown();
        };
    }

    public void Start() { _timer.Start(); Tick(); }

    private async void Tick()
    {
        if (_closing) return;
        if (!_busy && DateTime.UtcNow - _lastRead > TimeSpan.FromSeconds(1))
        {
            _busy = true; _lastRead = DateTime.UtcNow;
            try
            {
                var result = await Task.Run(() => (Anchor: CompanionDesktopTracker.Read(), Sessions: _activity.Read()));
                if (_closing) return;
                _anchor = result.Anchor; _sessions = result.Sessions;
                UpdateTasks(); Render();
            }
            finally { _busy = false; }
        }
        if (_anchor is null) return;
        if (!_anchor.DesktopOpen || _hidden) { Hide(); return; }
        if (!IsVisible) Show();
        if (!_refreshing && DateTime.UtcNow - _lastQuota > TimeSpan.FromSeconds(60)) _ = RefreshQuotaAsync();
        var cursor = CompanionDesktopTracker.CursorPosition();
        var overPet = _anchor.PetOpen && cursor.X >= _anchor.X - 8 && cursor.X <= _anchor.X + _anchor.Width + 8 && cursor.Y >= _anchor.Y - 8 && cursor.Y <= _anchor.Y + _anchor.Height + 8;
        if (overPet || IsMouseOver || Tasks.IsDropDownOpen) _lastHover = DateTime.UtcNow;
        SetExpanded(_pinned || DateTime.UtcNow - _lastHover < TimeSpan.FromMilliseconds(650));
        Position();
    }

    private void UpdateTasks()
    {
        var selection = _selectedId;
        var choices = new[] { new CompanionActivity { Id = "__auto__", Title = "自动跟随最近任务" } }.Concat(_sessions.Take(12)).ToArray();
        if (Tasks.ItemsSource is not CompanionActivity[] old || !old.Select(a => a.Id).SequenceEqual(choices.Select(a => a.Id)))
        {
            Tasks.ItemsSource = choices;
            Tasks.SelectedItem = choices.FirstOrDefault(a => a.Id == selection) ?? choices[0];
        }
    }

    private void Render()
    {
        var five = _snapshot?.GeneralWindows.FirstOrDefault(w => w.WindowDurationMinutes == 300);
        var week = _snapshot?.GeneralWindows.FirstOrDefault(w => w.WindowDurationMinutes == 10080);
        var expired = five?.ResetsAt is { } resetAt && resetAt <= DateTimeOffset.Now;
        var stale = _quotaError is not null || (_snapshot is not null && DateTimeOffset.Now - _snapshot.FetchedAt > TimeSpan.FromMinutes(2));
        BadgeLabel.Text = stale ? "5h 上次" : "5h 剩余";
        BadgeValue.Text = expired ? "待刷新" : five is null ? "—" : $"{five.RemainingPercent:0}%";
        BadgeValue.FontSize = expired ? 12 : 20;
        QuotaBar.Value = expired ? 0 : five?.RemainingPercent ?? 0;
        ResetText.Text = five is null ? "五小时窗口暂不可用" : expired ? "已到重置时间 · 等待服务器确认" : "距离重置 " + Remaining(five.ResetsAt);
        WeekText.Text = week is null ? "周额度暂不可用" : $"本周剩余 {week.RemainingPercent:0}%   ·   {week.ResetsAt?.ToLocalTime():M/d HH:mm} 重置";
        var current = _sessions.FirstOrDefault(a => a.Id == _selectedId) ?? (_sessions.Count > 0 ? _sessions[0] : null);
        if (current is null)
        {
            TaskTitle.Text = "";
            ActivityText.Text = "暂未读取到任务活动";
            ActivityAge.Text = "开始一个 Codex 任务后，这里会显示最近活动。";
            ProgressText.Text = "";
        }
        else
        {
            TaskTitle.Text = current.Title;
            var seconds = Math.Max(0, (DateTimeOffset.Now - current.LastActivity).TotalSeconds);
            ActivityText.Text = current.State == "active" && seconds > 90 ? "暂无新活动 · 状态待确认" : current.Action;
            ActivityAge.Text = (current.State == "active" && seconds > 90 ? $"最近活动：{current.Action}\n" : "") + $"{Age(seconds)}前更新 · {_sessions.Count(s => s.State == "active" && DateTimeOffset.Now - s.LastActivity < TimeSpan.FromMinutes(2))} 个近期活动任务";
            ProgressText.Text = current.Progress;
        }
        SourceText.Text = _snapshot is null ? "额度尚未同步" : $"额度更新于 {_snapshot.FetchedAt.ToLocalTime():HH:mm:ss} · 每分钟刷新";
        ErrorText.Text = _quotaError ?? _activity.Error ?? (_anchor?.PetOpen == false ? "宠物未开启 · 暂时显示为独立卡片" : "");
        Dot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_quotaError is not null || expired ? "#F4C88C" : "#74DAC3"));
        _tray.Text = $"Codex 小助手 · 5h {BadgeValue.Text}";
    }

    private static string Remaining(DateTimeOffset? reset)
    {
        if (reset is null) return "时间未知";
        var left = reset.Value - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero) return "等待刷新";
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours} 小时 {left.Minutes} 分钟" : $"{Math.Max(1, (int)left.TotalMinutes)} 分钟";
    }
    private static string Age(double seconds) => seconds < 60 ? $"{(int)seconds} 秒" : seconds < 3600 ? $"{(int)(seconds / 60)} 分钟" : $"{(int)(seconds / 3600)} 小时";

    private async Task RefreshQuotaAsync()
    {
        if (_refreshing || _closing || _anchor?.DesktopOpen != true) return;
        _refreshing = true; _lastQuota = DateTime.UtcNow; RefreshButton.IsEnabled = false;
        try { await _usage.RefreshAsync(); }
        finally { _refreshing = false; if (!_closing) RefreshButton.IsEnabled = true; }
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        Width = expanded ? 306 : 166;
        Details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateLayout();
    }

    private void Position()
    {
        if (_anchor is null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = ActualWidth * dpi.DpiScaleX; var height = ActualHeight * dpi.DpiScaleY;
        // Keep the card to the left of the pet, leaving official quick-chat controls usable.
        var left = _anchor.PetOpen ? _anchor.X - width - 12 * dpi.DpiScaleX : _anchor.X;
        var top = _anchor.PetOpen ? _anchor.Y + Math.Min(_anchor.Height / 2, 32 * dpi.DpiScaleY) : _anchor.Y;
        left = Math.Clamp(left, _anchor.ScreenX + 8, Math.Max(_anchor.ScreenX + 8, _anchor.ScreenX + _anchor.ScreenWidth - width - 8));
        top = Math.Clamp(top, _anchor.ScreenY + 8, Math.Max(_anchor.ScreenY + 8, _anchor.ScreenY + _anchor.ScreenHeight - height - 8));
        Left = left / dpi.DpiScaleX; Top = top / dpi.DpiScaleY;
    }

    private void TogglePin()
    {
        _pinned = !_pinned; PinHint.Text = _pinned ? "已固定" : "悬停展开";
        PinButton.Content = _pinned ? "取消固定" : "固定展开";
        SetExpanded(_pinned); Position();
    }
    private void BadgeClick(object sender, MouseButtonEventArgs e) => TogglePin();
    private void PinClick(object sender, RoutedEventArgs e) => TogglePin();
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshQuotaAsync();
    private void TaskChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tasks.SelectedItem is CompanionActivity selected) _selectedId = selected.Id == "__auto__" ? null : selected.Id;
        if (IsInitialized) Render();
    }

    public async ValueTask DisposeAsync()
    {
        if (_closing) return;
        _closing = true; _timer.Stop(); _tray.Dispose();
        while (_refreshing) await Task.Delay(50);
        await _usage.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
