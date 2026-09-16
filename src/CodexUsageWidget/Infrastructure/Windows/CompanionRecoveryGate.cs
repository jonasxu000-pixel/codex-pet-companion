using System.Globalization;
using System.IO;

namespace CodexUsageWidget.Infrastructure.Windows;

// An explicit tray exit pauses recovery for already-running Codex instances only.
public sealed class CompanionRecoveryGate(string pauseFile)
{
    public bool CanRecover(IEnumerable<DateTimeOffset> desktopStartTimes)
    {
        var pausedAt = DateTimeOffset.MinValue;
        if (File.Exists(pauseFile))
            DateTimeOffset.TryParse(File.ReadAllText(pauseFile), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out pausedAt);
        return desktopStartTimes.Any(start => start > pausedAt);
    }

    public void Pause(DateTimeOffset now)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pauseFile)!);
        File.WriteAllText(pauseFile, now.ToString("O", CultureInfo.InvariantCulture));
    }

    public void Resume() => File.Delete(pauseFile);

    public static CompanionRecoveryGate Current => new(Path.Combine(AppPaths.LocalDataDirectory, "recovery-paused-at.txt"));
}
