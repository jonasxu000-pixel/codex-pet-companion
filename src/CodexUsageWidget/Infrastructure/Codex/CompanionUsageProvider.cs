using CodexUsageWidget.Application;
using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Infrastructure.Codex;

/// <summary>Each quota request owns a short-lived CLI, released on success, error or cancellation.</summary>
public sealed class CompanionUsageProvider(Func<ICodexAppServerSession>? sessionFactory = null) : IUsageProvider
{
    private readonly Func<ICodexAppServerSession> _sessionFactory = sessionFactory ?? (() => new CodexAppServerSession());
    private bool _disposed;

    // This companion polls explicitly; it does not hold a live notification connection.
    public event EventHandler? RateLimitsChanged { add { } remove { } }
    public event EventHandler<string>? DiagnosticMessage;

    public async Task<UsageSnapshot> ReadUsageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = _sessionFactory();
        session.DiagnosticMessage += ForwardDiagnostic;
        try
        {
            var response = await session.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
            return new UsageSnapshot(CodexRateLimitsParser.Parse(response), null, DateTimeOffset.Now);
        }
        finally { session.DiagnosticMessage -= ForwardDiagnostic; }
    }

    private void ForwardDiagnostic(object? sender, string message) => DiagnosticMessage?.Invoke(this, message);

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
