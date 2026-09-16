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
    public static string CodexHome => Environment.GetEnvironmentVariable("CODEX_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public static CompanionAnchor Read()
    {
        var open = DesktopStartTimes().Count > 0;
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var fallback = new CompanionAnchor(open, false, screen.Right - 220, screen.Bottom - 120, 0, 0,
            screen.X, screen.Y, screen.Width, screen.Height);
        if (!open) return fallback;
        try
        {
            using var file = new FileStream(Path.Combine(CodexHome, ".codex-global-state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(file);
            var root = doc.RootElement;
            var atom = root.TryGetProperty("electron-persisted-atom-state", out var a) ? a : root;
            var petOpen = Property(root, atom, "electron-avatar-overlay-open");
            var bounds = Property(root, atom, "electron-avatar-overlay-bounds");
            if (petOpen.ValueKind != JsonValueKind.True || bounds.ValueKind != JsonValueKind.Object) return fallback;
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
            return new CompanionAnchor(open, true, x, y, width, height, screen.X, screen.Y, screen.Width, screen.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    public static IReadOnlyList<DateTimeOffset> DesktopStartTimes()
    {
        var starts = new List<DateTimeOffset>();
        using var current = Process.GetCurrentProcess();
        foreach (var name in new[] { "ChatGPT", "Codex" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == current.SessionId && process.MainWindowHandle != IntPtr.Zero &&
                        (process.MainModule?.FileName.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) == true ||
                         name == "Codex")) starts.Add(process.StartTime.ToUniversalTime());
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return starts;
    }

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
