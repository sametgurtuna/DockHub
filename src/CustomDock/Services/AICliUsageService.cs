using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>A place the AI usage widget reads limits from (Claude Code, Codex or Gemini CLI).</summary>
public interface IAIUsageSource
{
    AIUsageData Current { get; }

    AIUsageStatus Status { get; }

    bool IsRefreshing { get; }

    event EventHandler<AIUsageData> Updated;

    void RequestInterval(object owner, TimeSpan? interval);

    Task RefreshAsync();
}

/// <summary>
/// Usage read from files an AI CLI writes on this PC. Reading is cheap (no process starts), so it follows the
/// widget's interval, at most once a minute, and only while a widget is subscribed.
/// </summary>
public abstract class LocalUsageSource : IAIUsageSource
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
    private readonly Dictionary<object, TimeSpan> _intervals = new();
    private EventHandler<AIUsageData>? _updated;

    protected LocalUsageSource()
    {
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public AIUsageData Current { get; private set; } = new();

    public AIUsageStatus Status { get; private set; } = AIUsageStatus.Pending;

    public bool IsRefreshing { get; private set; }

    public event EventHandler<AIUsageData> Updated
    {
        add
        {
            _updated += value;
            if (_timer.IsEnabled) return;
            _ = RefreshAsync();
            _timer.Start();
        }
        remove
        {
            _updated -= value;
            if (_updated is null) _timer.Stop();
        }
    }

    public void RequestInterval(object owner, TimeSpan? interval)
    {
        if (interval is { } value) _intervals[owner] = value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value;
        else _intervals.Remove(owner);
        _timer.Interval = _intervals.Count == 0 ? TimeSpan.FromMinutes(5) : _intervals.Values.Min();
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var (status, data) = await Task.Run(Read);
            Status = status;
            data.FetchedAt = DateTime.Now;
            Current = data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"{GetType().Name} failed");
            Status = AIUsageStatus.Unknown;
        }
        finally
        {
            IsRefreshing = false;
        }
        _updated?.Invoke(this, Current);
    }

    protected abstract (AIUsageStatus Status, AIUsageData Data) Read();

    protected static string ResetText(DateTime resetsAt)
    {
        var culture = CultureInfo.CurrentCulture;
        return resetsAt.Date == DateTime.Today ? resetsAt.ToString("t", culture) : resetsAt.ToString("ddd t", culture);
    }

    /// <summary>The last lines of a (possibly large) log file.</summary>
    protected static IEnumerable<string> TailLines(string path, int maxBytes = 512 * 1024)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        if (start > 0) reader.ReadLine(); // partial line
        while (reader.ReadLine() is { } line) lines.Add(line);
        lines.Reverse();
        return lines;
    }
}

/// <summary>
/// OpenAI Codex CLI: its session logs (~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl) record the account's rate limits
/// with every "token_count" event: a primary (5-hour) and a secondary (weekly) window with the percentage used.
/// </summary>
public sealed class CodexUsageService : LocalUsageSource
{
    private static string Home => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    protected override (AIUsageStatus, AIUsageData) Read()
    {
        var sessions = Path.Combine(Home, "sessions");
        if (!Directory.Exists(Home)) return (AIUsageStatus.CliNotFound, new AIUsageData { Title = L.T("Codex usage") });
        foreach (var file in RecentRollouts(sessions))
        {
            foreach (var line in TailLines(file))
            {
                if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal) || !line.Contains("token_count", StringComparison.Ordinal)) continue;
                if (TryParse(line, out var data)) return (AIUsageStatus.Ok, data);
            }
        }
        return (AIUsageStatus.ParseFailed, new AIUsageData { Title = L.T("Codex usage") });
    }

    /// <summary>Newest rollout files first, from the last two weeks of day folders.</summary>
    private static IEnumerable<string> RecentRollouts(string sessions)
    {
        if (!Directory.Exists(sessions)) return Array.Empty<string>();
        var files = new List<FileInfo>();
        try
        {
            foreach (var year in Directory.EnumerateDirectories(sessions).OrderByDescending(d => d).Take(2))
            foreach (var month in Directory.EnumerateDirectories(year).OrderByDescending(d => d).Take(2))
            foreach (var day in Directory.EnumerateDirectories(month).OrderByDescending(d => d).Take(14))
                files.AddRange(new DirectoryInfo(day).EnumerateFiles("rollout-*.jsonl"));
            // Older versions wrote straight into the sessions folder.
            files.AddRange(new DirectoryInfo(sessions).EnumerateFiles("rollout-*.jsonl"));
        }
        catch (IOException) { /* folder changing */ }
        catch (UnauthorizedAccessException) { /* skip */ }
        return files.OrderByDescending(f => f.LastWriteTimeUtc).Take(5).Select(f => f.FullName);
    }

    /// <summary>Reads one rollout line: {"type":"event_msg","payload":{"type":"token_count","rate_limits":{...}}}.</summary>
    internal static bool TryParse(string line, out AIUsageData data)
    {
        data = new AIUsageData { Title = L.T("Codex usage") };
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var payload = root.TryGetProperty("payload", out var p) ? p : root;
            if (!payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object) return false;
            DateTime loggedAt = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                && DateTime.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToLocalTime() : DateTime.Now;

            bool any = false;
            if (Window(limits, "primary", loggedAt) is { } primary)
            {
                (data.SessionPercent, data.SessionResets, data.PrimaryLabel) = primary;
                any = true;
            }
            if (Window(limits, "secondary", loggedAt) is { } secondary)
            {
                (data.WeekPercent, data.WeekResets, data.SecondaryLabel) = secondary;
                any = true;
            }
            return any;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static (double Percent, string? Resets, string Label)? Window(JsonElement limits, string name, DateTime loggedAt)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) return null;
        if (!window.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number) return null;
        double percent = used.GetDouble();

        DateTime? resetsAt = null;
        if (window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number)
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(at.GetInt64()).LocalDateTime;
        else if (window.TryGetProperty("resets_in_seconds", out var inSeconds) && inSeconds.ValueKind == JsonValueKind.Number)
            resetsAt = loggedAt.AddSeconds(inSeconds.GetInt64());

        // The log is only written while Codex runs; once the window has reset, nothing of it is used any more.
        if (resetsAt is { } reset && reset <= DateTime.Now) percent = 0;

        long minutes = window.TryGetProperty("window_minutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt64() : 0;
        string label = minutes switch
        {
            >= 10080 - 60 and <= 10080 + 60 => L.T("Weekly"),
            > 0 and < 1440 when minutes % 60 == 0 => L.T("{0}-hour", minutes / 60),
            _ => name == "primary" ? L.T("5-hour") : L.T("Weekly"),
        };
        return (percent, resetsAt is { } r && r > DateTime.Now ? ResetText(r) : null, label);
    }
}

/// <summary>
/// Google Gemini CLI: every model answer is saved in its chat recordings (~/.gemini/tmp/*/chats/session-*.jsonl) with
/// token counts. Today's answers are counted against a daily request limit (1,000 on the free tier).
/// </summary>
public sealed class GeminiUsageService : LocalUsageSource
{
    /// <summary>Requests per day the widget measures against (set from the widget's settings).</summary>
    public int DailyLimit { get; set; } = 1000;

    private static string Home => Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") is { Length: > 0 } custom
        ? Path.Combine(custom, ".gemini")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");

    protected override (AIUsageStatus, AIUsageData) Read()
    {
        var data = new AIUsageData { Title = L.T("Gemini CLI usage"), PrimaryLabel = L.T("Today"), HasSecondary = false };
        var temp = Path.Combine(Home, "tmp");
        if (!Directory.Exists(Home)) return (AIUsageStatus.CliNotFound, data);

        var answers = new Dictionary<string, long>();
        if (Directory.Exists(temp))
        {
            foreach (var project in Directory.EnumerateDirectories(temp))
            {
                var chats = Path.Combine(project, "chats");
                if (!Directory.Exists(chats)) continue;
                foreach (var file in new DirectoryInfo(chats).EnumerateFiles("session-*.json*", SearchOption.AllDirectories))
                {
                    if (file.LastWriteTime.Date != DateTime.Today) continue;
                    try { CountFile(file.FullName, answers); }
                    catch (IOException) { /* being written */ }
                    catch (JsonException) { /* partial line */ }
                }
            }
        }

        int requests = answers.Count;
        long tokens = answers.Values.Sum();
        int limit = Math.Max(1, DailyLimit);
        data.SessionPercent = Math.Min(100, requests * 100.0 / limit);
        data.SessionResets = ResetText(DateTime.Today.AddDays(1));
        data.Detail = L.T("{0} of {1} requests today · {2} tokens", requests, limit, FormatTokens(tokens));
        return (AIUsageStatus.Ok, data);
    }

    /// <summary>Adds today's model answers (id → total tokens) of one chat recording.</summary>
    internal static void CountFile(string path, Dictionary<string, long> answers)
    {
        if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || !line.Contains("\"gemini\"", StringComparison.Ordinal)) continue;
                using var doc = JsonDocument.Parse(line);
                AddMessage(doc.RootElement, answers);
            }
        }
        else
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                foreach (var message in messages.EnumerateArray()) AddMessage(message, answers);
        }
    }

    private static void AddMessage(JsonElement message, Dictionary<string, long> answers)
    {
        if (message.ValueKind != JsonValueKind.Object) return;
        if (!message.TryGetProperty("type", out var type) || type.GetString() != "gemini") return;
        if (!message.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) return;
        if (!message.TryGetProperty("timestamp", out var ts) || !DateTime.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var at) || at.ToLocalTime().Date != DateTime.Today) return;
        long total = message.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object
                     && tokens.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : 0;
        // A message is re-written as it streams; keep its largest token count.
        answers[id.GetString()!] = Math.Max(answers.GetValueOrDefault(id.GetString()!), total);
    }

    private static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000 => (tokens / 1_000_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (tokens / 1_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ => tokens.ToString(CultureInfo.CurrentCulture),
    };
}
