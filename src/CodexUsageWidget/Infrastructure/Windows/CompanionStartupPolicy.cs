namespace CodexUsageWidget.Infrastructure.Windows;

public enum CompanionStartupDecision { Wait, Ready, DesktopClosed, TimedOut }

public static class CompanionStartupPolicy
{
    // Store activation can precede a usable Electron window by several minutes.
    // This wait is bounded and only runs after activation; it never queries quota.
    public static CompanionStartupDecision Decide(CompanionAnchor anchor, bool hasWindow,
        TimeSpan elapsed, bool desktopSeen)
    {
        if (desktopSeen && !anchor.DesktopOpen) return CompanionStartupDecision.DesktopClosed;
        if (CompanionDesktopTracker.ShouldRun(anchor) && hasWindow) return CompanionStartupDecision.Ready;
        if (hasWindow && !anchor.PetOpen && elapsed >= TimeSpan.FromSeconds(20))
            return CompanionStartupDecision.TimedOut;
        if (elapsed >= (desktopSeen ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(20)))
            return CompanionStartupDecision.TimedOut;
        return CompanionStartupDecision.Wait;
    }
}
