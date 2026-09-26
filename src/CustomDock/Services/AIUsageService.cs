using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

public sealed class AIUsageData
{
    public double? SessionPercent { get; set; }
    public string? SessionResets { get; set; }
    public double? WeekPercent { get; set; }
    public string? WeekResets { get; set; }
    public DateTime FetchedAt { get; set; }
}

public enum AIUsageStatus
{
    /// <summary>Nothing fetched yet.</summary>
    Pending,
    Ok,
    /// <summary>The <c>claude</c> command isn't installed (or not on PATH).</summary>
    CliNotFound,
    /// <summary>The CLI asks the user to sign in.</summary>
    NotLoggedIn,
    Timeout,
    /// <summary>The CLI answered but the usage lines weren't found (output format changed?).</summary>
    ParseFailed,
    Unknown,
}

/// <summary>
/// Monitors Claude Code subscription usage. Runs "claude -p /usage" in an invisible
/// background process (without opening a window) and parses its output. Only runs while a widget is subscribed.
/// </summary>
public sealed class AIUsageService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    private static readonly Regex SessionRx = new(
        @"current\s+session[^\r\n%]*?(\d{1,3})%\s*used[^\r\n(]*?resets\s+([^\r\n(]+?)\s*(?:\(|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex WeekRx = new(
        @"current\s+week[^\r\n%]*?(\d{1,3})%\s*used[^\r\n(]*?resets\s+([^\r\n(]+?)\s*(?:\(|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex LoginRx = new(
        @"\b(log\s*in|login|sign\s*in|authenticat|/login|not\s+logged)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly Dictionary<object, TimeSpan> _requestedIntervals = new();
    private EventHandler<AIUsageData>? _updated;
    private bool _refreshing;
    private int _consecutiveErrors;

    public AIUsageService()
    {
        _timer.Interval = DefaultInterval;
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public AIUsageData Current { get; private set; } = new();

    public AIUsageStatus Status { get; private set; } = AIUsageStatus.Pending;

    /// <summary>Short technical detail for the last failure (tooltip), or null.</summary>
    public string? Error { get; private set; }

    public bool IsRefreshing => _refreshing;

    /// <summary>Poll interval: the shortest one requested by the subscribed widgets.</summary>
    public TimeSpan PollInterval => _requestedIntervals.Count == 0 ? DefaultInterval : _requestedIntervals.Values.Min();

    public event EventHandler<AIUsageData> Updated
    {
        add
        {
            _updated += value;
            if (!_timer.IsEnabled)
            {
                _ = RefreshAsync();
                _timer.Start();
            }
        }
        remove
        {
            _updated -= value;
            if (_updated is null) _timer.Stop();
        }
    }

    /// <summary>Each widget asks for its own interval; the service polls at the shortest one.</summary>
    public void RequestInterval(object owner, TimeSpan? interval)
    {
        if (interval is { } value) _requestedIntervals[owner] = value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value;
        else _requestedIntervals.Remove(owner);
        if (_consecutiveErrors == 0 && Status != AIUsageStatus.CliNotFound)
            _timer.Interval = PollInterval;
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!IsClaudeExecutablePresent())
            {
                Status = AIUsageStatus.CliNotFound;
                Error = "Claude CLI not found";
                _timer.Interval = TimeSpan.FromHours(1);
                return;
            }

            var (output, timedOut) = await RunClaudeUsageAsync().ConfigureAwait(true);
            if (timedOut)
            {
                Status = AIUsageStatus.Timeout;
                Error = $"No answer within {ProcessTimeout.TotalSeconds:0} seconds";
                ApplyBackoff();
            }
            else if (string.IsNullOrWhiteSpace(output))
            {
                Status = AIUsageStatus.Unknown;
                Error = "No response received";
                ApplyBackoff();
            }
            else
            {
                var parsed = Parse(output);
                if (parsed.SessionPercent is null && parsed.WeekPercent is null)
                {
                    Status = LoginRx.IsMatch(output) ? AIUsageStatus.NotLoggedIn : AIUsageStatus.ParseFailed;
                    Error = output.Length > 160 ? output[..160].Trim() + "…" : output.Trim();
                    ApplyBackoff();
                }
                else
                {
                    Current = parsed;
                    Status = AIUsageStatus.Ok;
                    Error = null;
                    _consecutiveErrors = 0;
                    _timer.Interval = PollInterval;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve Claude usage information");
            Status = AIUsageStatus.Unknown;
            Error = ex.Message;
            ApplyBackoff();
        }
        finally
        {
            _refreshing = false;
            _updated?.Invoke(this, Current);
        }
    }

    private void ApplyBackoff()
    {
        _consecutiveErrors++;
        var backoff = _consecutiveErrors switch
        {
            1 => TimeSpan.FromMinutes(10),
            2 => TimeSpan.FromMinutes(20),
            _ => TimeSpan.FromHours(1),
        };
        _timer.Interval = backoff > PollInterval ? backoff : PollInterval;
    }

    private static bool IsClaudeExecutablePresent()
    {
        var augmentedPath = BuildAugmentedPath();
        var extensions = new[] { ".cmd", ".exe", ".bat", "" };
        foreach (var dir in augmentedPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var ext in extensions)
                {
                    if (File.Exists(Path.Combine(dir, "claude" + ext)))
                        return true;
                }
            }
            catch { /* access permissions etc. */ }
        }
        return false;
    }

    /// <summary>Runs the "claude -p /usage" command completely hidden (without showing a window) and returns its output.</summary>
    private static async Task<(string? Output, bool TimedOut)> RunClaudeUsageAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /c claude -p \"/usage\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        // DockHub typically starts on user login; "claude" command might have been installed afterwards.
        // Since the process PATH could be stale, merge with current PATH from registry.
        psi.EnvironmentVariables["Path"] = BuildAugmentedPath();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };
        if (!process.Start()) return (null, false);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* may already be closed */ }
            // Let the readers finish so the pipes are released before the process object is disposed.
            try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); } catch { /* ignore */ }
            return (null, true);
        }

        string stdout = await stdoutTask.ConfigureAwait(true);
        string stderr = await stderrTask.ConfigureAwait(true);
        return (string.IsNullOrWhiteSpace(stdout) ? stderr : stdout, false);
    }

    /// <summary>Merges process PATH with user and machine PATH entries (without duplicates).</summary>
    private static string BuildAugmentedPath()
    {
        string process = Environment.GetEnvironmentVariable("PATH") ?? "";
        string user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        string machine = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (var segment in $"{process};{user};{machine}".Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(segment)) parts.Add(segment);
        }
        return string.Join(';', parts);
    }

    /// <summary>Parses the "/usage" output of the Claude CLI.</summary>
    internal static AIUsageData Parse(string text)
    {
        var data = new AIUsageData { FetchedAt = DateTime.Now };

        var sessionMatch = SessionRx.Match(text);
        if (sessionMatch.Success && double.TryParse(sessionMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double session))
        {
            data.SessionPercent = Math.Clamp(session, 0, 100);
            data.SessionResets = sessionMatch.Groups[2].Value.Trim();
        }

        var weekMatch = WeekRx.Match(text);
        if (weekMatch.Success && double.TryParse(weekMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double week))
        {
            data.WeekPercent = Math.Clamp(week, 0, 100);
            data.WeekResets = weekMatch.Groups[2].Value.Trim();
        }

        return data;
    }
}
