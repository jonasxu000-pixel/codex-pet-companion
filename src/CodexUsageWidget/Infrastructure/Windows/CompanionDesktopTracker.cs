using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace CodexUsageWidget.Infrastructure.Windows;

public sealed record CompanionAnchor(bool DesktopOpen, bool PetOpen, double X, double Y,
    double Width, double Height, double ScreenX, double ScreenY, double ScreenWidth, double ScreenHeight);

public static class CompanionDesktopTracker
{
    private static int _desktopProcessId;
    private static DateTimeOffset _desktopStarted;
    private static CompanionAnchor? _cachedAnchor;
    private static string? _cachedStatePath, _cachedScreens;
    private static long _cachedStateLength;
    private static DateTime _cachedStateWrite;
    private static DateTime _lastValidStateRead;

    public static string CodexHome => Environment.GetEnvironmentVariable("CODEX_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public static bool ShouldRun(CompanionAnchor anchor) => anchor.DesktopOpen && anchor.PetOpen;

    public static CompanionAnchor Read()
    {
        var open = DesktopStartTimes().Count > 0;
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var fallback = new CompanionAnchor(open, false, screen.Right - 220, screen.Bottom - 120, 0, 0,
            screen.X, screen.Y, screen.Width, screen.Height);
        if (!open) return fallback;
        try
        {
            var statePath = Path.Combine(CodexHome, ".codex-global-state.json");
            var metadata = new FileInfo(statePath);
            var screens = string.Join(";", System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds.ToString() + s.WorkingArea));
            if (_cachedAnchor is not null && statePath == _cachedStatePath && screens == _cachedScreens &&
                metadata.Length == _cachedStateLength && metadata.LastWriteTimeUtc == _cachedStateWrite)
            {
                _lastValidStateRead = DateTime.UtcNow;
                return _cachedAnchor;
            }
            using var file = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(file);
            var root = doc.RootElement;
            var atom = root.TryGetProperty("electron-persisted-atom-state", out var a) ? a : root;
            var petOpen = Property(root, atom, "electron-avatar-overlay-open");
            var bounds = Property(root, atom, "electron-avatar-overlay-bounds");
            if (petOpen.ValueKind != JsonValueKind.True || bounds.ValueKind != JsonValueKind.Object)
            {
                _cachedAnchor = null;
                return fallback;
            }
            var x = Number(bounds, "x"); var y = Number(bounds, "y");
            double width = 100, height = 100;
            if (bounds.TryGetProperty("mascot", out var mascot))
            {
                x += Number(mascot, "left"); y += Number(mascot, "top");
                width = Number(mascot, "width", 100); height = Number(mascot, "height", 100);
            }
            // Electron persists DIP coordinates. Convert relative to its recorded display;
            // screen matching also works for negative monitor coordinates.
            if (bounds.TryGetProperty("displayBounds", out var display))
            {
                var dx = Number(display, "x"); var dy = Number(display, "y");
                var dw = Number(display, "width", 1920); var dh = Number(display, "height", 1080);
                var monitor = System.Windows.Forms.Screen.AllScreens
                    .OrderBy(s => Math.Abs(s.Bounds.X - dx) + Math.Abs(s.Bounds.Y - dy)).First();
                var sx = monitor.Bounds.Width / dw; var sy = monitor.Bounds.Height / dh;
                x = monitor.Bounds.X + (x - dx) * sx; y = monitor.Bounds.Y + (y - dy) * sy;
                width *= sx; height *= sy; screen = monitor.WorkingArea;
            }
            _cachedStatePath = statePath; _cachedScreens = screens;
            _cachedStateLength = metadata.Length; _cachedStateWrite = metadata.LastWriteTimeUtc;
            _lastValidStateRead = DateTime.UtcNow;
            _cachedAnchor = new CompanionAnchor(open, true, x, y, width, height, screen.X, screen.Y, screen.Width, screen.Height);
            return _cachedAnchor;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Atomic state-file replacement and short read failures are not a pet-close event.
            if (_cachedAnchor is not null && DateTime.UtcNow - _lastValidStateRead < TimeSpan.FromSeconds(10))
                return _cachedAnchor;
            return fallback;
        }
    }

    public static IReadOnlyList<DateTimeOffset> DesktopStartTimes()
    {
        var starts = new List<DateTimeOffset>();
        using var current = Process.GetCurrentProcess();
        if (_desktopProcessId != 0)
        {
            try
            {
                using var known = Process.GetProcessById(_desktopProcessId);
                if (known.SessionId == current.SessionId && known.StartTime.ToUniversalTime() == _desktopStarted &&
                    !known.HasExited) return [_desktopStarted];
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            _desktopProcessId = 0;
        }
        foreach (var name in new[] { "ChatGPT", "Codex" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (process.SessionId == current.SessionId && path is not null && IsDesktopExecutable(path) &&
                        (process.MainWindowHandle != IntPtr.Zero || IsRootDesktopProcess(process.Id)) &&
                        !process.HasExited)
                    {
                        _desktopProcessId = process.Id;
                        _desktopStarted = process.StartTime.ToUniversalTime();
                        starts.Add(_desktopStarted);
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return starts;
    }

    public static bool IsDesktopExecutable(string path) =>
        path.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) ||
        File.Exists(Path.Combine(Path.GetDirectoryName(path) ?? "", "resources", "app.asar"));

    public static bool HasDesktopWindow()
    {
        if (DesktopStartTimes().Count == 0) return false;
        try
        {
            using var process = Process.GetProcessById(_desktopProcessId);
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }

    private static bool IsRootDesktopProcess(int processId)
    {
        // Electron renderers have a Codex parent. The root survives window hiding,
        // recreation and slow startup; no WMI process watcher is required.
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return false;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) return false;
            do
            {
                if (entry.ProcessId != processId) continue;
                try
                {
                    using var parent = Process.GetProcessById((int)entry.ParentProcessId);
                    return parent.ProcessName is not ("ChatGPT" or "Codex");
                }
                catch (ArgumentException) { return true; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
            } while (Process32Next(snapshot, ref entry));
            return false;
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private static JsonElement Property(JsonElement root, JsonElement atom, string name) =>
        root.TryGetProperty(name, out var value) ? value : atom.TryGetProperty(name, out value) ? value : default;
    private static double Number(JsonElement element, string name, double fallback = 0) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;

    public static (double Left, double Top) BadgePosition(CompanionAnchor anchor, double width, double height, double scaleX, double scaleY)
    {
        var left = anchor.PetOpen ? anchor.X - width - 12 * scaleX : anchor.X;
        if (anchor.PetOpen && left < anchor.ScreenX + 8)
            left = anchor.X + anchor.Width + 12 * scaleX;
        var top = anchor.PetOpen ? anchor.Y + Math.Min(anchor.Height / 2, 32 * scaleY) : anchor.Y;
        left = Math.Clamp(left, anchor.ScreenX + 8, Math.Max(anchor.ScreenX + 8, anchor.ScreenX + anchor.ScreenWidth - width - 8));
        top = Math.Clamp(top, anchor.ScreenY + 8, Math.Max(anchor.ScreenY + 8, anchor.ScreenY + anchor.ScreenHeight - height - 8));
        return (left, top);
    }

    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var point); return (point.X, point.Y);
    }

    public static bool StartupEnabled()
    {
        try
        {
            if (CompanionScheduledStartup.GetEnabled() is { } enabled) return enabled;
        }
        catch (COMException) { }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("CodexPetCompanion") is string;
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (CompanionScheduledStartup.TrySetEnabled(enabled))
        {
            key.DeleteValue("CodexPetCompanion", false);
            return;
        }
        if (enabled) key.SetValue("CodexPetCompanion", "\"" + Environment.ProcessPath + "\" --startup");
        else key.DeleteValue("CodexPetCompanion", false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
