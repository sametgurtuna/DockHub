using System.Diagnostics;
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

/// <summary>
/// Claude Code aboneliğinin kullanım oranını izler. "claude -p /usage" komutunu görünmez bir
/// arka plan işleminde (pencere açılmadan) çalıştırıp çıktısını ayrıştırır. Yalnızca abone varken çalışır.
/// </summary>
public sealed class AIUsageService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    private static readonly Regex SessionRx = new(
        @"current\s+session[^\r\n%]*?(\d{1,3})%\s*used[^\r\n(]*?resets\s+([^\r\n(]+?)\s*(?:\(|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WeekRx = new(
        @"current\s+week[^\r\n%]*?(\d{1,3})%\s*used[^\r\n(]*?resets\s+([^\r\n(]+?)\s*(?:\(|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private EventHandler<AIUsageData>? _updated;
    private bool _refreshing;
    private int _consecutiveErrors;

    public AIUsageService()
    {
        _timer.Interval = PollInterval;
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public AIUsageData Current { get; private set; } = new();

    public string? Error { get; private set; }

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

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!IsClaudeExecutablePresent())
            {
                Error = "Claude CLI bulunamadı";
                _timer.Interval = TimeSpan.FromHours(1);
                return;
            }

            var output = await RunClaudeUsageAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(output))
            {
                var parsed = Parse(output);
                if (parsed.SessionPercent is null && parsed.WeekPercent is null)
                {
                    // Süreç çalıştı ama beklenen satırlar bulunamadı (ör. "claude" bulunamadı, giriş gerekiyor).
                    Error = output.Length > 160 ? output[..160].Trim() + "…" : output.Trim();
                    ApplyBackoff();
                }
                else
                {
                    Current = parsed;
                    Error = null;
                    _consecutiveErrors = 0;
                    _timer.Interval = PollInterval;
                }
            }
            else
            {
                Error = "Yanıt alınamadı";
                ApplyBackoff();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Claude kullanım bilgisi alınamadı");
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
        _timer.Interval = _consecutiveErrors switch
        {
            1 => TimeSpan.FromMinutes(10),
            2 => TimeSpan.FromMinutes(20),
            _ => TimeSpan.FromHours(1),
        };
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
            catch { /* erişim izinleri vb. */ }
        }
        return false;
    }

    /// <summary>"claude -p /usage" komutunu tamamen gizli (pencere göstermeden) çalıştırır ve çıktısını döner.</summary>
    private static async Task<string?> RunClaudeUsageAsync()
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
        // DockHub genelde oturum açılışında başlar; "claude" komutu daha sonra kurulmuş olabilir.
        // Sürecin PATH'i kalıntı (stale) olabileceğinden, kayıt defterindeki güncel PATH ile birleştirilir.
        psi.EnvironmentVariables["Path"] = BuildAugmentedPath();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };
        if (!process.Start()) return null;
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
            try { process.Kill(true); } catch { /* zaten kapanmış olabilir */ }
            return null;
        }

        string stdout = await stdoutTask.ConfigureAwait(true);
        string stderr = await stderrTask.ConfigureAwait(true);
        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    /// <summary>Sürecin kendi PATH'ini, kullanıcı ve makine PATH kayıtlarıyla (tekrarsız) birleştirir.</summary>
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

    private static AIUsageData Parse(string text)
    {
        var data = new AIUsageData { FetchedAt = DateTime.Now };

        var sessionMatch = SessionRx.Match(text);
        if (sessionMatch.Success)
        {
            data.SessionPercent = double.Parse(sessionMatch.Groups[1].Value);
            data.SessionResets = sessionMatch.Groups[2].Value.Trim();
        }

        var weekMatch = WeekRx.Match(text);
        if (weekMatch.Success)
        {
            data.WeekPercent = double.Parse(weekMatch.Groups[1].Value);
            data.WeekResets = weekMatch.Groups[2].Value.Trim();
        }

        return data;
    }
}
