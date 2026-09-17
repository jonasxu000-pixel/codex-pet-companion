using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexUsageWidget.Infrastructure.Codex;

public sealed class CompanionActivity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Codex 任务";
    public string Action { get; set; } = "尚未读取到活动";
    public string Progress { get; set; } = "";
    public string State { get; set; } = "unknown";
    public DateTimeOffset LastActivity { get; set; }
    public string? TurnId { get; set; }
    public override string ToString() => Title;
}

/// <summary>Reads only event metadata and public commentary. Reasoning contents and tool output are ignored.</summary>
public sealed class CompanionActivityReader
{
    private sealed class Cursor
    {
        public long Offset;
        public string Pending = "";
        public CompanionActivity Activity { get; } = new();
    }

    private readonly string _home;
    private readonly Dictionary<string, Cursor> _files = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDiscovery;
    private DateTime _lastTitleWrite;
    private long _lastTitleLength = -1;
    private const int MaxTailBytes = 256 * 1024;
    public string? Error { get; private set; }

    public CompanionActivityReader(string home) => _home = home;

    public IReadOnlyList<CompanionActivity> Read()
    {
        Error = null;
        try
        {
            if (DateTime.UtcNow - _lastDiscovery > TimeSpan.FromSeconds(10))
            {
                var root = Path.Combine(_home, "sessions");
                // Discover recent partitions only; never scan the entire transcript history every tick.
                var candidates = Enumerable.Range(0, 7).Select(offset =>
                    Path.Combine(root, DateTime.Now.AddDays(-offset).ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture)))
                    .Where(Directory.Exists)
                    .SelectMany(dir => Directory.EnumerateFiles(dir, "*.jsonl"))
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(24).ToArray();
                foreach (var path in candidates)
                    if (!_files.ContainsKey(path))
                    {
                        _files[path] = new Cursor();
                        _lastTitleLength = -1;
                    }
                foreach (var path in _files.Keys.Except(candidates).ToArray()) _files.Remove(path);
                _lastDiscovery = DateTime.UtcNow;
            }

            foreach (var (path, cursor) in _files)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < cursor.Offset) { cursor.Offset = 0; cursor.Pending = ""; }
                if (stream.Length == cursor.Offset) continue;
                if (cursor.Offset == 0)
                {
                    using var header = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                    var first = header.ReadLine();
                    if (first is not null) ApplyLine(cursor.Activity, first);
                    stream.Position = Math.Max(0, stream.Length - MaxTailBytes);
                    if (stream.Position > 0)
                    {
                        // Skip the partial first record at a bounded initial tail.
                        int b;
                        do { b = stream.ReadByte(); } while (b != -1 && b != '\n');
                    }
                }
                else
                {
                    stream.Position = cursor.Offset;
                    if (stream.Length - cursor.Offset > MaxTailBytes)
                    {
                        stream.Position = stream.Length - MaxTailBytes;
                        cursor.Pending = "";
                        int b;
                        do { b = stream.ReadByte(); } while (b != -1 && b != '\n');
                    }
                }
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                var text = cursor.Pending + reader.ReadToEnd();
                cursor.Offset = stream.Position;
                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0) { cursor.Pending = text.Length <= MaxTailBytes ? text : ""; continue; }
                foreach (var line in text[..lastNewline].Split('\n')) ApplyLine(cursor.Activity, line);
                cursor.Pending = text[(lastNewline + 1)..];
            }
            ApplyTitles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "活动记录暂时不可读";
        }
        return _files.Values.Select(c => c.Activity).Where(a => a.LastActivity != default)
            .OrderByDescending(a => a.LastActivity).ToArray();
    }

    private void ApplyTitles()
    {
        var index = Path.Combine(_home, "session_index.jsonl");
        if (!File.Exists(index)) return;
        var metadata = new FileInfo(index);
        if (metadata.Length == _lastTitleLength && metadata.LastWriteTimeUtc == _lastTitleWrite) return;
        _lastTitleLength = metadata.Length;
        _lastTitleWrite = metadata.LastWriteTimeUtc;
        using var stream = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = Math.Max(0, stream.Length - 256 * 1024);
        using var reader = new StreamReader(stream);
        if (stream.Position > 0) reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var id = Text(doc.RootElement, "id");
                var title = Text(doc.RootElement, "thread_name");
                if (title.Length == 0) continue;
                foreach (var cursor in _files.Values)
                    if (cursor.Activity.Id == id) cursor.Activity.Title = title;
            }
            catch (JsonException) { /* A concurrently appended partial record is retried next time. */ }
        }
    }

    public static void ApplyLine(CompanionActivity activity, string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var payload)) return;
            var kind = Text(root, "type");
            var type = Text(payload, "type");
            if (kind == "session_meta")
            {
                activity.Id = Text(payload, "id");
                var cwd = Text(payload, "cwd");
                if (cwd.Length > 0) activity.Title = Path.GetFileName(cwd.TrimEnd('\\', '/'));
                return;
            }
            if (!DateTimeOffset.TryParse(Text(root, "timestamp"), out var time)) return;
            if (time < activity.LastActivity) return;
            if (kind == "event_msg")
            {
                switch (type)
                {
                    case "task_started":
                        activity.TurnId = Text(payload, "turn_id");
                        activity.State = "active"; activity.Action = "任务进行中"; activity.Progress = ""; break;
                    case "task_complete": case "task_completed": case "turn_aborted":
                        var turn = Text(payload, "turn_id");
                        if (turn.Length > 0 && !string.IsNullOrEmpty(activity.TurnId) && turn != activity.TurnId) return;
                        activity.State = "done"; activity.Action = type == "turn_aborted" ? "任务已停止" : "任务已完成"; break;
                    case "error": case "stream_error":
                        activity.State = "error"; activity.Action = "任务报告错误，请查看 Codex"; break;
                    case "item_completed":
                        if (!payload.TryGetProperty("item", out var item)) return;
                        var itemType = Text(item, "type");
                        if (itemType.Contains("command", StringComparison.OrdinalIgnoreCase)) activity.Action = "命令已返回";
                        else if (itemType.Contains("patch", StringComparison.OrdinalIgnoreCase) || itemType == "fileChange") activity.Action = "文件修改已完成";
                        else if (itemType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)) activity.Action = "任务有新活动";
                        else return;
                        activity.State = "active"; break;
                    default: return;
                }
            }
            else if (kind == "response_item")
            {
                switch (type)
                {
                    case "function_call": case "custom_tool_call":
                        var name = Text(payload, "name");
                        activity.State = "active";
                        activity.Action = name.Contains("apply_patch", StringComparison.OrdinalIgnoreCase) ? "正在修改文件" :
                            name.Contains("search", StringComparison.OrdinalIgnoreCase) ? "正在检索资料" :
                            name.Contains("request_user_input", StringComparison.OrdinalIgnoreCase) ? "等待你的回答" : "等待工具返回";
                        break;
                    case "function_call_output": case "custom_tool_call_output":
                        activity.State = "active"; activity.Action = "工具已返回，等待下一步"; break;
                    case "message":
                        if (Text(payload, "role") != "assistant") return;
                        // Only explicit public commentary; never display analysis or reasoning blocks.
                        if (Text(payload, "channel") == "commentary" && payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        {
                            var publicText = string.Join(" ", content.EnumerateArray().Where(c => Text(c, "type") is "output_text" or "text").Select(c => Text(c, "text")));
                            activity.Progress = publicText.Length > 180 ? publicText[..180] + "…" : publicText;
                            activity.Action = "更新了进度说明"; activity.State = "active";
                        }
                        else return;
                        break;
                    default: return;
                }
            }
            else return;
            activity.LastActivity = time;
        }
        catch (JsonException) { }
    }

    internal static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
