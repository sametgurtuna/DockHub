using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Shell;

namespace CustomDock.Dock;

public sealed partial class AppButton
{
    // ------------------------------------------------------------------ Pinned icon (with retries)

    /// <summary>
    /// Delays between attempts when a pinned app's icon can't be loaded (shell not ready at sign-in,
    /// file being replaced by an updater). Until then the generic app icon is shown instead of an empty button.
    /// </summary>
    private static readonly int[] IconRetryDelaysMs = { 1000, 3000, 10000, 30000, 120000 };
    private DispatcherTimer? _iconRetryTimer;
    private int _iconRetryAttempt;
    private bool _iconIsFallback;

    private void LoadPinnedIcon()
    {
        if (Item is null) return;
        _icon.Source = AppIcons.For(Item, IconSizing.Pixels, out _iconIsFallback);
        _iconRetryAttempt = 0;
        if (_iconIsFallback) ScheduleIconRetry();
    }

    /// <summary>Retries a missing pinned icon now (after resume, display or Explorer changes).</summary>
    public void RefreshIcon()
    {
        if (Item is null || !_iconIsFallback) return;
        _iconRetryAttempt = 0;
        RetryIcon();
    }

    private void ScheduleIconRetry()
    {
        if (_iconRetryAttempt >= IconRetryDelaysMs.Length)
        {
            Log.Debug($"Icon still missing after {IconRetryDelaysMs.Length} retries: {Item?.Path}");
            return;
        }
        _iconRetryTimer ??= CreateIconRetryTimer();
        _iconRetryTimer.Interval = TimeSpan.FromMilliseconds(IconRetryDelaysMs[_iconRetryAttempt++]);
        _iconRetryTimer.Start();
    }

    private DispatcherTimer CreateIconRetryTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RetryIcon();
        };
        return timer;
    }

    private void RetryIcon()
    {
        _iconRetryTimer?.Stop();
        if (Item is null) return;
        AppIcons.Invalidate(Item);
        if (AppIcons.TryFor(Item, IconSizing.Pixels) is { } image)
        {
            _icon.Source = image;
            _iconIsFallback = false;
            Log.Debug($"Icon recovered: {Item.Path}");
            return;
        }
        ScheduleIconRetry();
    }
}
