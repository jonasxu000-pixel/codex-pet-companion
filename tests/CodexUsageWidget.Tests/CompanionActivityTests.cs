using CodexUsageWidget.Infrastructure.Codex;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class CompanionActivityTests
{
    [Fact]
    public void ReasoningAndTokenTelemetryAreNotPublicProgress()
    {
        var activity = new CompanionActivity();
        CompanionActivityReader.ApplyLine(activity, """{"type":"response_item","timestamp":"2026-09-15T10:00:00Z","payload":{"type":"reasoning","summary":[{"text":"private"}]}}""");
        CompanionActivityReader.ApplyLine(activity, """{"type":"event_msg","timestamp":"2026-09-15T10:01:00Z","payload":{"type":"token_count"}}""");
        Assert.Empty(activity.Progress);
        Assert.Equal(default, activity.LastActivity);
    }

    [Fact]
    public void LateCompletionCannotFinishNewTurn()
    {
        var activity = new CompanionActivity();
        CompanionActivityReader.ApplyLine(activity, """{"type":"event_msg","timestamp":"2026-09-15T10:00:00Z","payload":{"type":"task_started","turn_id":"new"}}""");
        CompanionActivityReader.ApplyLine(activity, """{"type":"event_msg","timestamp":"2026-09-15T10:01:00Z","payload":{"type":"task_complete","turn_id":"old"}}""");
        Assert.Equal("active", activity.State);
        CompanionActivityReader.ApplyLine(activity, """{"type":"event_msg","timestamp":"2026-09-15T10:02:00Z","payload":{"type":"task_complete","turn_id":"new"}}""");
        Assert.Equal("done", activity.State);
    }

    [Fact]
    public void OnlyCommentaryBecomesProgress()
    {
        var activity = new CompanionActivity();
        CompanionActivityReader.ApplyLine(activity, """{"type":"response_item","timestamp":"2026-09-15T10:00:00Z","payload":{"type":"message","role":"assistant","channel":"analysis","content":[{"type":"output_text","text":"private"}]}}""");
        Assert.Empty(activity.Progress);
        CompanionActivityReader.ApplyLine(activity, """{"type":"response_item","timestamp":"2026-09-15T10:01:00Z","payload":{"type":"message","role":"assistant","channel":"commentary","content":[{"type":"output_text","text":"正在检查文件"}]}}""");
        Assert.Equal("正在检查文件", activity.Progress);
    }

    [Fact]
    public void ConcurrentlyWrittenPartialLineIsIgnoredWithoutCorruptingState()
    {
        var activity = new CompanionActivity { State = "done" };
        CompanionActivityReader.ApplyLine(activity, "{\"type\":\"event_msg\"");
        Assert.Equal("done", activity.State);
    }
}
