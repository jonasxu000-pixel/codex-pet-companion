using System.Globalization;
using CodexUsageWidget.Infrastructure.Codex;

namespace CodexUsageWidget.Tests.Infrastructure;

public sealed class CompanionActivityReadTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "companion-tail-tests", Guid.NewGuid().ToString("N"));
    private string SessionFile
    {
        get
        {
            var path = Path.Combine(_folder, "sessions", DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(path);
            return Path.Combine(path, "sample.jsonl");
        }
    }
    private const string Header = """{"type":"session_meta","payload":{"id":"s1","cwd":"C:/sample"}}""";
    private const string Started = """{"type":"event_msg","timestamp":"2026-09-16T12:00:00Z","payload":{"type":"task_started","turn_id":"t1"}}""";

    [Fact]
    public void InitialLargeTailPreservesSessionIdentityAndLatestActivity()
    {
        File.WriteAllText(SessionFile, Header + "\n" + new string('x', 1024 * 1024) + "\n" + Started + "\n");
        var activity = Assert.Single(new CompanionActivityReader(_folder).Read());
        Assert.Equal("s1", activity.Id);
        Assert.Equal("active", activity.State);
    }

    [Fact]
    public void LargeAppendAndPartialFinalRecordAreBoundedAndRecoverOnNewline()
    {
        File.WriteAllText(SessionFile, Header + "\n");
        var reader = new CompanionActivityReader(_folder);
        Assert.Empty(reader.Read());
        File.AppendAllText(SessionFile, new string('x', 1024 * 1024) + "\n" + Started);
        Assert.Empty(reader.Read());
        File.AppendAllText(SessionFile, "\n");
        Assert.Equal("active", Assert.Single(reader.Read()).State);
    }

    [Fact]
    public void ChangedTitleIndexInvalidatesCache()
    {
        File.WriteAllText(SessionFile, Header + "\n" + Started + "\n");
        var index = Path.Combine(_folder, "session_index.jsonl");
        File.WriteAllText(index, "{\"id\":\"s1\",\"thread_name\":\"Before\"}\n");
        var reader = new CompanionActivityReader(_folder);
        Assert.Equal("Before", Assert.Single(reader.Read()).Title);
        Assert.Equal("Before", Assert.Single(reader.Read()).Title);
        File.WriteAllText(index, "{\"id\":\"s1\",\"thread_name\":\"Changed title\"}\n");
        Assert.Equal("Changed title", Assert.Single(reader.Read()).Title);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        GC.SuppressFinalize(this);
    }
}
