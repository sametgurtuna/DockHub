using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CustomDock.Core;
using Windows.Media.Control;
using WinRtSessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using WinRtSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace CustomDock.Services;

public sealed class MediaState
{
    public static readonly MediaState Empty = new();

    public bool HasSession { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string SourceApp { get; init; } = "";
    public ImageSource? Thumbnail { get; init; }
    public bool IsPlaying { get; init; }
    public bool CanPlayPause { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset PositionUpdatedAt { get; init; }

    /// <summary>Estimated playback position based on time elapsed since last update.</summary>
    public TimeSpan EstimatedPosition
    {
        get
        {
            if (!IsPlaying || PositionUpdatedAt == default) return Position;
            var estimate = Position + (DateTimeOffset.Now - PositionUpdatedAt);
            return Duration > TimeSpan.Zero && estimate > Duration ? Duration : estimate;
        }
    }
}

/// <summary>
/// Monitors the active media session via Windows System Media Transport Controls (SMTC).
/// Works with Spotify, YouTube Music (browser), Apple Music, VLC, etc. or any SMTC-compliant player.
/// </summary>
public sealed class MediaService
{
    private WinRtSessionManager? _manager;
    private WinRtSession? _session;
    private Task? _initTask;
    private int _refreshVersion;
    private string _thumbnailKey = "";
    private ImageSource? _thumbnail;

    public MediaState Current { get; private set; } = MediaState.Empty;

    /// <summary>Fired on the UI thread.</summary>
    public event Action? Changed;

    public Task EnsureStartedAsync() => _initTask ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            _manager = await WinRtSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => OnUi(SelectSession);
            _manager.SessionsChanged += (_, _) => OnUi(SelectSession);
            SelectSession();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize SMTC");
        }
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    private void SelectSession()
    {
        if (_manager is null) return;

        WinRtSession? session = null;
        try
        {
            session = _manager.GetCurrentSession();
            if (session is null)
            {
                var sessions = _manager.GetSessions();
                session = sessions.FirstOrDefault(s =>
                              s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                          ?? sessions.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get SMTC session");
        }

        if (!ReferenceEquals(session, _session))
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = session;
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }

        _ = RefreshAsync();
    }

    private void OnMediaPropertiesChanged(WinRtSession sender, MediaPropertiesChangedEventArgs args) => OnUi(() => _ = RefreshAsync());

    private void OnPlaybackInfoChanged(WinRtSession sender, PlaybackInfoChangedEventArgs args) => OnUi(() => _ = RefreshAsync());

    private void OnTimelineChanged(WinRtSession sender, TimelinePropertiesChangedEventArgs args) => OnUi(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        int version = ++_refreshVersion;
        var session = _session;
        if (session is null)
        {
            Publish(MediaState.Empty);
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _refreshVersion) return;

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var controls = playback?.Controls;

            string key = $"{session.SourceAppUserModelId}|{props?.Title}|{props?.Artist}|{props?.AlbumTitle}";
            if (key != _thumbnailKey)
            {
                _thumbnailKey = key;
                _thumbnail = props?.Thumbnail is { } thumbRef ? await LoadThumbnailAsync(thumbRef) : null;
                if (version != _refreshVersion) return;
            }
            else if (_thumbnail is null && props?.Thumbnail is { } retryRef)
            {
                // Some players send the cover image a few hundred ms later.
                _thumbnail = await LoadThumbnailAsync(retryRef);
                if (version != _refreshVersion) return;
            }

            var duration = timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime;
            Publish(new MediaState
            {
                HasSession = true,
                Title = props?.Title ?? "",
                Artist = string.IsNullOrWhiteSpace(props?.Artist) ? props?.AlbumArtist ?? "" : props!.Artist,
                Album = props?.AlbumTitle ?? "",
                SourceApp = FriendlyAppName(session.SourceAppUserModelId),
                Thumbnail = _thumbnail,
                IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = controls?.IsPlayPauseToggleEnabled ?? controls?.IsPlayEnabled ?? false,
                CanNext = controls?.IsNextEnabled ?? false,
                CanPrevious = controls?.IsPreviousEnabled ?? false,
                Position = timeline?.Position ?? TimeSpan.Zero,
                Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
                PositionUpdatedAt = timeline?.LastUpdatedTime ?? default,
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read media info");
        }
    }

    private void Publish(MediaState state)
    {
        Current = state;
        Changed?.Invoke();
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var winrtStream = await reference.OpenReadAsync();
            using var stream = winrtStream.AsStreamForRead();
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            if (memory.Length == 0) return null;
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 512;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load album artwork");
            return null;
        }
    }

    private static string FriendlyAppName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("zunemusic") || lower.Contains("media")) return "Media Player";
        if (lower.Contains("applemusic") || lower.Contains("itunes")) return "Apple Music";
        if (lower.Contains("vlc")) return "VLC";
        var name = aumid.Split('!')[0];
        name = Path.GetFileNameWithoutExtension(name);
        return name.Length > 20 ? name[..20] : name;
    }

    /// <summary>
    /// Media session of an app, for the play/pause buttons under its window preview. Matched by the window's
    /// AppUserModelID, or by the executable name appearing in the session's id ("Spotify.exe", "Chrome", "MSEdge").
    /// </summary>
    public MediaAppSession? FindSession(string? processName, string? aumid)
    {
        if (_manager is null) return null;
        try
        {
            foreach (var session in _manager.GetSessions())
            {
                string id = session.SourceAppUserModelId ?? "";
                if (id.Length == 0) continue;
                bool match = !string.IsNullOrEmpty(aumid) && string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase);
                if (!match && !string.IsNullOrEmpty(processName))
                {
                    string exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
                    match = exe.Length > 2 && id.Contains(exe, StringComparison.OrdinalIgnoreCase);
                }
                if (match) return new MediaAppSession(session);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Media sessions could not be read: {ex.Message}");
        }
        return null;
    }

    public async Task PlayPauseAsync()
    {
        if (_session is not null) await Try(() => _session.TryTogglePlayPauseAsync().AsTask());
    }

    public async Task NextAsync()
    {
        if (_session is not null) await Try(() => _session.TrySkipNextAsync().AsTask());
    }

    public async Task PreviousAsync()
    {
        if (_session is not null) await Try(() => _session.TrySkipPreviousAsync().AsTask());
    }

    public async Task SeekAsync(TimeSpan position)
    {
        if (_session is not null)
        {
            await Try(() => _session.TryChangePlaybackPositionAsync(position.Ticks).AsTask());
        }
    }

    private static async Task Try(Func<Task<bool>> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Media command failed");
        }
    }
}

/// <summary>One app's media session: its current track and transport buttons.</summary>
public sealed class MediaAppSession
{
    private readonly WinRtSession _session;

    internal MediaAppSession(WinRtSession session) => _session = session;

    public bool IsPlaying
    {
        get
        {
            try { return _session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }
    }

    public bool CanPrevious => Controls?.IsPreviousEnabled ?? false;

    public bool CanNext => Controls?.IsNextEnabled ?? false;

    private GlobalSystemMediaTransportControlsSessionPlaybackControls? Controls
    {
        get
        {
            try { return _session.GetPlaybackInfo()?.Controls; }
            catch { return null; }
        }
    }

    /// <summary>Current track title (used to pick the right browser window).</summary>
    public async Task<string> GetTitleAsync()
    {
        try { return (await _session.TryGetMediaPropertiesAsync())?.Title ?? ""; }
        catch { return ""; }
    }

    public Task PlayPauseAsync() => Run(() => _session.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() => Run(() => _session.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() => Run(() => _session.TrySkipPreviousAsync().AsTask());

    private static async Task Run(Func<Task<bool>> action)
    {
        try { await action(); }
        catch (Exception ex) { Log.Error(ex, "Media command failed"); }
    }
}
