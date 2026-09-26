using System.Globalization;
using System.Windows;
using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>Notification count and Do Not Disturb next to the clock; the new tray indicators' settings.</summary>
public partial class DockWindow
{
    private bool _notificationSubscribed;

    private void ApplyIndicatorSettings()
    {
        bool tray = HasTray;
        KeyboardLayoutIcon.Allowed = tray && _config.ShowKeyboardLayout;
        MicrophoneStatusIcon.Mode = tray ? _config.MicrophoneIcon : MicrophoneIconMode.Off;
        VirtualDesktopIcon.Allowed = _config.ShowDesktopIndicator;
        _shell.RunningApps.IncludeAllDesktops = _config.RunningAppsAllDesktops;

        bool notifications = _config.ShowClock && _config.ShowNotificationIndicator && !_closing;
        if (notifications && !_notificationSubscribed)
        {
            AppServices.NotificationCenter.EnsureStarted();
            AppServices.NotificationCenter.Changed += UpdateNotificationIndicator;
            _notificationSubscribed = true;
        }
        else if (!notifications && _notificationSubscribed)
        {
            AppServices.NotificationCenter.Changed -= UpdateNotificationIndicator;
            _notificationSubscribed = false;
        }
        UpdateNotificationIndicator();
    }

    private void StopIndicators()
    {
        if (!_notificationSubscribed) return;
        AppServices.NotificationCenter.Changed -= UpdateNotificationIndicator;
        _notificationSubscribed = false;
    }

    private void UpdateNotificationIndicator()
    {
        var center = AppServices.NotificationCenter;
        bool dnd = _notificationSubscribed && center.DoNotDisturb;
        int count = _notificationSubscribed ? center.Count : 0;

        // Windows 11 shows the moon while Do Not Disturb is on and the count otherwise.
        DoNotDisturbGlyph.Visibility = dnd ? Visibility.Visible : Visibility.Collapsed;
        NotificationBadge.Visibility = !dnd && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationCountText.Text = count > 9 ? "9+" : count.ToString(CultureInfo.CurrentCulture);
        NotificationIndicator.Visibility = dnd || count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock(DateTime.Now);
    }

    /// <summary>Date, plus the notification state when the indicator is on.</summary>
    private string ClockToolTip(DateTime now)
    {
        string text = now.ToString("D", CultureInfo.CurrentCulture);
        if (!_notificationSubscribed) return text;
        var center = AppServices.NotificationCenter;
        if (center.DoNotDisturb) text += "\n" + L.T("Do not disturb is on");
        if (center.Count > 0) text += "\n" + (center.Count == 1 ? L.T("1 notification") : L.T("{0} notifications", center.Count));
        return text;
    }
}
