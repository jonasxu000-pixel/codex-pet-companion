using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using CodexUsageWidget.Infrastructure;
using CodexUsageWidget.Infrastructure.Codex.Hooks;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Windows;
using CodexUsageWidget.Localization;

namespace CodexUsageWidget;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Screen caches its first bounds. Set DPI before preflight, diagnostics or WPF touch it.
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        if (args.Length == 2 && args[0] == "--diagnose")
        {
            return Infrastructure.Codex.CompanionDiagnostics.RunAsync(args[1]).GetAwaiter().GetResult();
        }

        if (CodexActivityCommandLine.IsCommandMode(args))
        {
            return CodexActivityCommandLine.RunAsync(args).GetAwaiter().GetResult();
        }

        try
        {
            if (TryRelaunchFromStableLocation(args))
            {
                return 0;
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            _ = new AppLanguageController(new LanguagePreferenceStore());
            System.Windows.MessageBox.Show(
                Strings.Format("App_InstallFailure", ex.Message),
                Strings.Get("App_Name"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return -1;
        }

        if (!args.Contains("--classic", StringComparer.OrdinalIgnoreCase))
        {
            // No WPF window, task logs or quota CLI before both Codex and its pet are open.
            var anchor = CompanionDesktopTracker.Read();
            if (args.Contains("--recover", StringComparer.OrdinalIgnoreCase))
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!anchor.DesktopOpen && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(500);
                    anchor = CompanionDesktopTracker.Read();
                }
            }
            if (!CompanionDesktopTracker.ShouldRun(anchor)) return 0;
        }

        var application = new App();
        if (args.Contains("--classic", StringComparer.OrdinalIgnoreCase)) application.InitializeComponent();
        else application.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        return application.Run();
    }

    private static bool TryRelaunchFromStableLocation(IReadOnlyCollection<string> arguments)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var temporaryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath(),
            Path.Combine(localApplicationData, "Temp")
        };
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        if (!PortableAppInstaller.TryInstallFromTemporaryLocation(
                currentPath,
                temporaryRoots,
                AppPaths.LocalDataDirectory,
                version,
                out var installedPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(installedPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installedPath)!
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (Process.Start(startInfo) is null)
        {
            throw new Win32Exception("Windows did not start the installed application.");
        }

        return true;
    }
}
