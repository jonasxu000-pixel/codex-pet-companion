using System.Text.Json;
using CodexUsageWidget.Infrastructure.Codex;

namespace CodexUsageWidget.Tests.Infrastructure;

public sealed class CompanionUsageProviderTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("cancel")]
    public async Task QueryAlwaysReleasesSession(string outcome)
    {
        var session = new Session(outcome);
        await using var provider = new CompanionUsageProvider(() => session);
        if (outcome == "error") await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ReadUsageAsync());
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ReadUsageAsync());
        else Assert.Single((await provider.ReadUsageAsync()).GeneralWindows);
        Assert.True(session.Disposed);
        Assert.Equal(["account/rateLimits/read"], session.Methods);
    }

    [Fact]
    public async Task EachRefreshGetsItsOwnSession()
    {
        var sessions = new List<Session>();
        await using var provider = new CompanionUsageProvider(() =>
        {
            var session = new Session("success");
            sessions.Add(session);
            return session;
        });
        await provider.ReadUsageAsync();
        await provider.ReadUsageAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.True(session.Disposed));
    }

    private sealed class Session(string outcome) : ICodexAppServerSession
    {
        public bool Disposed { get; private set; }
        public List<string> Methods { get; } = [];
        public event EventHandler<string>? NotificationReceived { add { } remove { } }
        public event EventHandler<string>? DiagnosticMessage { add { } remove { } }
        public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Methods.Add(method);
            if (outcome == "error") throw new InvalidOperationException("offline");
            if (outcome == "cancel") throw new OperationCanceledException();
            using var document = JsonDocument.Parse("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":30,"windowDurationMins":300}}}""");
            return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
