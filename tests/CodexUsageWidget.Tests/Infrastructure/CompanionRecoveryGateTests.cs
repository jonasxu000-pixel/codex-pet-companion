using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Tests.Infrastructure;

public sealed class CompanionRecoveryGateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "companion-recovery-tests", Guid.NewGuid().ToString("N"));
    private CompanionRecoveryGate Gate => new(Path.Combine(_folder, "pause.txt"));
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingCompanionRecoversWhenCodexIsOpen() => Assert.True(Gate.CanRecover([Noon]));

    [Fact]
    public void DoesNotStartWithoutCodex() => Assert.False(Gate.CanRecover([]));

    [Fact]
    public void ExplicitExitPausesExistingCodexButAllowsReopenedCodex()
    {
        Gate.Pause(Noon);
        Assert.False(Gate.CanRecover([Noon.AddHours(-1)]));
        Assert.True(Gate.CanRecover([Noon.AddHours(1)]));
    }

    [Fact]
    public void ManualLaunchResumesCurrentCodex()
    {
        Gate.Pause(Noon);
        Gate.Resume();
        Assert.True(Gate.CanRecover([Noon.AddHours(-1)]));
    }

    [Fact]
    public void DamagedPauseFileDoesNotPermanentlyBlockRecovery()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "pause.txt"), "incomplete");
        Assert.True(Gate.CanRecover([Noon]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        GC.SuppressFinalize(this);
    }
}
