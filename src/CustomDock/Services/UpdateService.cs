using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>A published DockHub release.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Notes, string PageUrl, string? InstallerUrl, long InstallerSize, string? ChecksumsUrl, string? InstallerSha256 = null);

/// <summary>
/// Checks GitHub Releases once a day (when enabled) and can download and run the installer silently.
/// The installer closes DockHub with --exit (the taskbar comes back) and starts the new version.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/sametgurtuna/DockHub/releases/latest";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = CreateClient();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private UpdateState _state;

    public UpdateService()
    {
        _state = JsonStore.LoadData<UpdateState>("update-state");
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync(userInitiated: false).ConfigureAwait(true);
        };
    }

    public static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version is { } v
        ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(0, 0, 0);

    /// <summary>Newer release found by the last check (null when up to date or not checked yet).</summary>
    public ReleaseInfo? Available { get; private set; }

    public DateTime? LastChecked => _state.LastCheck;

    public event Action<ReleaseInfo>? UpdateAvailable;

    public void Start()
    {
        if (!AppServices.Config.CheckForUpdates) return;
        var due = _state.LastCheck is { } last ? last + CheckInterval - DateTime.Now : TimeSpan.Zero;
        _timer.Interval = due < FirstCheckDelay ? FirstCheckDelay : due;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Asks GitHub for the latest release. Returns it when it's newer than this version.</summary>
    public async Task<ReleaseInfo?> CheckAsync(bool userInitiated)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl).ConfigureAwait(true);
            _state.LastCheck = DateTime.Now;
            JsonStore.SaveData("update-state", _state);
            if (release is null || release.Draft || (release.Prerelease && !AppServices.Config.IncludePrereleases)) return null;

            var info = ToInfo(release);
            if (info is null || info.Version <= CurrentVersion)
            {
                Available = null;
                return null;
            }

            Available = info;
            if (userInitiated || _state.SkippedVersion != info.Tag)
                UpdateAvailable?.Invoke(info);
            return info;
        }
        catch (Exception ex)
        {
            // Offline or rate-limited: try again at the next interval.
            Log.Warn($"Update check failed: {ex.Message}");
            if (userInitiated) throw;
            return null;
        }
    }

    public void Skip(ReleaseInfo release)
    {
        _state.SkippedVersion = release.Tag;
        JsonStore.SaveData("update-state", _state);
    }

    /// <summary>Downloads the installer (verifying its checksum when published) and runs it silently.</summary>
    public async Task DownloadAndInstallAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellation = default)
    {
        if (release.InstallerUrl is null) throw new InvalidOperationException("This release has no installer.");
        string target = Path.Combine(Path.GetTempPath(), $"DockHub-Setup-{release.Version}-x64.exe");

        using (var response = await Http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(true))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.InstallerSize;
            await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(true);
            await using var file = File.Create(target);
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellation).ConfigureAwait(true)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(true);
                read += count;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        // GitHub publishes a sha256 digest per asset; older releases may ship SHA256SUMS.txt instead.
        string expected = release.InstallerSha256 ?? "";
        if (expected.Length == 0 && release.ChecksumsUrl is not null)
        {
            string sums = await Http.GetStringAsync(release.ChecksumsUrl, cancellation).ConfigureAwait(true);
            expected = sums.Split('\n')
                .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1].Equals(Path.GetFileName(new Uri(release.InstallerUrl).LocalPath), StringComparison.OrdinalIgnoreCase))
                ?[0] ?? "";
        }
        if (expected.Length > 0)
        {
            string actual;
            await using (var stream = File.OpenRead(target))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(true)).ToLowerInvariant();
            if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidOperationException("The downloaded installer doesn't match its published checksum.");
            }
        }

        Log.Info($"Starting installer for {release.Version}");
        Process.Start(new ProcessStartInfo(target, "/SILENT /SP- /NOCANCEL") { UseShellExecute = true });
    }

    private static ReleaseInfo? ToInfo(GitHubRelease release)
    {
        if (!Version.TryParse(release.TagName.TrimStart('v', 'V').Split('-')[0], out var version)) return null;
        var installer = release.Assets.FirstOrDefault(a => a.Name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase));
        var sums = release.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        return new ReleaseInfo(version, release.TagName, release.Name ?? release.TagName, release.Body ?? "", release.HtmlUrl,
            installer?.BrowserDownloadUrl, installer?.Size ?? 0, sums?.BrowserDownloadUrl,
            installer?.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DockHub/{CurrentVersion} (+https://github.com/sametgurtuna/DockHub)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed class UpdateState
    {
        public DateTime? LastCheck { get; set; }
        public string? SkippedVersion { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
