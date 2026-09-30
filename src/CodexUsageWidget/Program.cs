using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using CodexUsageWidget.Infrastructure;
using CodexUsageWidget.Infrastructure.Codex.Hooks;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Windows;
using CodexUsageWidget.Infrastructure.Logging;
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
            var logger = new FileLogger(AppPaths.LogDirectory);
            logger.Info($"Startup preflight; version {typeof(Program).Assembly.GetName().Version}; desktop={anchor.DesktopOpen}, pet={anchor.PetOpen}.");
            if (args.Contains("--recover", StringComparer.OrdinalIgnoreCase))
            {
                var timer = Stopwatch.StartNew();
                var desktopSeen = anchor.DesktopOpen;
                CompanionStartupDecision decision;
                while ((decision = CompanionStartupPolicy.Decide(anchor,
                    CompanionDesktopTracker.HasDesktopWindow(), timer.Elapsed, desktopSeen)) == CompanionStartupDecision.Wait)
                {
                    Thread.Sleep(1000);
                    anchor = CompanionDesktopTracker.Read();
                    desktopSeen |= anchor.DesktopOpen;
                }
                logger.Info($"Startup preflight finished: {decision}; elapsed={timer.Elapsed.TotalSeconds:0.0}s; desktop={anchor.DesktopOpen}, pet={anchor.PetOpen}.");
                if (decision != CompanionStartupDecision.Ready) return 0;
            }
            if (!CompanionDesktopTracker.ShouldRun(anchor))
            {
                logger.Info("Startup skipped: Codex or pet is not open.");
                return 0;
            }
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
