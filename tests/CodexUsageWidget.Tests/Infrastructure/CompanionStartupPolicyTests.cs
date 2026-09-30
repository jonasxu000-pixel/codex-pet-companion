using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Tests.Infrastructure;

public sealed class CompanionStartupPolicyTests
{
    private static CompanionAnchor Anchor(bool desktop, bool pet) => new(desktop, pet, 0, 0, 100, 100, 0, 0, 1920, 1080);

    [Fact]
    public void SlowElectronWindowDoesNotLoseActivation()
    {
        var elapsed = TimeSpan.FromSeconds(150);
        Assert.Equal(CompanionStartupDecision.Wait, CompanionStartupPolicy.Decide(Anchor(true, true), false, elapsed, true));
        Assert.Equal(CompanionStartupDecision.Ready, CompanionStartupPolicy.Decide(Anchor(true, true), true, elapsed, true));
    }

    [Fact]
    public void WaitsForPetAfterDesktopWindowAppears()
    {
        Assert.Equal(CompanionStartupDecision.Wait, CompanionStartupPolicy.Decide(Anchor(true, false), true, TimeSpan.FromSeconds(8), true));
        Assert.Equal(CompanionStartupDecision.Ready, CompanionStartupPolicy.Decide(Anchor(true, true), true, TimeSpan.FromSeconds(15), true));
    }

    [Fact]
    public void StopsWhenDesktopExitsDuringStartup()
    {
        Assert.Equal(CompanionStartupDecision.DesktopClosed, CompanionStartupPolicy.Decide(Anchor(false, false), false, TimeSpan.FromSeconds(4), true));
    }

    [Fact]
    public void WaitNeverBecomesAnIdleWatcher()
    {
        Assert.Equal(CompanionStartupDecision.TimedOut, CompanionStartupPolicy.Decide(Anchor(false, false), false, TimeSpan.FromSeconds(20), false));
        Assert.Equal(CompanionStartupDecision.TimedOut, CompanionStartupPolicy.Decide(Anchor(true, false), true, TimeSpan.FromMinutes(5), true));
        Assert.Equal(CompanionStartupDecision.TimedOut, CompanionStartupPolicy.Decide(Anchor(true, false), true, TimeSpan.FromSeconds(20), true));
    }

    [Fact]
    public void QuotaCliIsNotTheDesktopApplication()
    {
        Assert.False(CompanionDesktopTracker.IsDesktopExecutable(@"C:\Users\test\AppData\Local\OpenAI\Codex\bin\version\codex.exe"));
        Assert.True(CompanionDesktopTracker.IsDesktopExecutable(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.928_x64\app\ChatGPT.exe"));
    }
}
