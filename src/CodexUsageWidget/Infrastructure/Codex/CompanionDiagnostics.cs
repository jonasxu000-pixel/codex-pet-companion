using System.IO;
using System.Text.Json;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Infrastructure.Codex;

public static class CompanionDiagnostics
{
    public static async Task<int> RunAsync(string outputPath)
    {
        var anchor = CompanionDesktopTracker.Read();
        var activity = new CompanionActivityReader(CompanionDesktopTracker.CodexHome).Read();
        try
        {
            await using var provider = new CodexUsageProvider(new CodexAppServerSession());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var usage = await provider.ReadUsageAsync(timeout.Token).ConfigureAwait(false);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                ok = true, anchor, usage.FetchedAt,
                windows = usage.GeneralWindows,
                activityCount = activity.Count,
                latestActivity = activity.Count > 0 ? new { activity[0].State, activity[0].Action, activity[0].LastActivity } : null
            })).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new { ok = false, anchor, errorType = ex.GetType().Name })).ConfigureAwait(false);
            return 1;
        }
    }
}
