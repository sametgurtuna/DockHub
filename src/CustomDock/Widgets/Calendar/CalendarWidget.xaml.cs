using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class CalendarSettings : ObservableObject
{
    private string _feeds = "";
    private int _remindMinutes = 5;

    /// <summary>iCal (.ics) subscription links, one per line.</summary>
    public string Feeds { get => _feeds; set => Set(ref _feeds, value ?? ""); }

    /// <summary>Notification this many minutes before an event (0 = off).</summary>
    public int RemindMinutes { get => _remindMinutes; set => Set(ref _remindMinutes, Math.Clamp(value, 0, 60)); }

    public IReadOnlyList<string> FeedList => Feeds.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Next meeting from ICS calendar feeds with a Join button for Teams/Meet/Zoom links, and today's agenda.</summary>
public partial class CalendarWidget : WidgetBase
{
    public const string Icon = "M5,6 H19 A1.5,1.5 0 0 1 20.5,7.5 V19 A1.5,1.5 0 0 1 19,20.5 H5 A1.5,1.5 0 0 1 3.5,19 V7.5 A1.5,1.5 0 0 1 5,6 Z M3.5,10 H20.5 M8,3.5 V7.5 M16,3.5 V7.5 M7.5,13.5 H9.5 M11,13.5 H13 M14.5,13.5 H16.5 M7.5,17 H9.5 M11,17 H13";

    private static readonly DispatcherTimer FeedTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(15) };
    private static event Action? FeedsDue;
    private static readonly HashSet<string> Notified = new();

    private CalendarSettings _settings = new();
    private List<CalendarEntry> _events = new();
    private string? _error;

    static CalendarWidget()
    {
        FeedTimer.Tick += (_, _) => FeedsDue?.Invoke();
    }

    public CalendarWidget()
    {
        InitializeComponent();
    }

    private CalendarEntry? Next => _events.FirstOrDefault(e => !e.AllDay && e.End > DateTime.Now) ?? _events.FirstOrDefault();

    protected override void OnAttached()
    {
        _settings = GetSettings<CalendarSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.Clock.MinuteTick += OnMinute;
        FeedsDue += OnFeedsDue;
        FeedTimer.Start();
        _ = LoadAsync();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.Clock.MinuteTick -= OnMinute;
        FeedsDue -= OnFeedsDue;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarSettings.Feeds)) _ = LoadAsync();
    }

    private void OnFeedsDue() => _ = LoadAsync();

    private void OnMinute(object? sender, DateTime now)
    {
        Render();
        NotifyUpcoming(now);
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_next, Layout_compact);
        Render();
    }

    private async Task LoadAsync()
    {
        if (IsPreview)
        {
            var at = DateTime.Today.AddHours(DateTime.Now.Hour + 1).AddMinutes(30);
            _events = new() { new(L.T("Design review"), at, at.AddMinutes(45), false, null, "https://meet.google.com/abc-defg-hij"), new(L.T("Lunch"), at.AddHours(2), at.AddHours(3), false, null, null) };
            Render();
            return;
        }
        if (_settings.FeedList.Count == 0)
        {
            _events = new();
            _error = L.T("Add a calendar link in the widget settings.");
            Render();
            return;
        }
        try
        {
            _events = await CalendarService.GetUpcomingAsync(_settings.FeedList);
            _error = null;
        }
        catch (Exception ex)
        {
            _error = L.T("The calendar couldn't be loaded. Check the link.");
            Log.Warn($"Calendar feed failed: {ex.Message}");
        }
        Render();
    }

    private static string Until(CalendarEntry e)
    {
        var now = DateTime.Now;
        if (e.AllDay) return e.Start.Date == now.Date ? L.T("Today") : e.Start.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        if (e.Start <= now) return L.T("Now · until {0}", e.End.ToString("t", CultureInfo.CurrentCulture));
        var left = e.Start - now;
        if (left.TotalMinutes < 60) return L.T("In {0} min", (int)Math.Ceiling(left.TotalMinutes));
        if (e.Start.Date == now.Date) return e.Start.ToString("t", CultureInfo.CurrentCulture);
        if (e.Start.Date == now.Date.AddDays(1)) return L.T("Tomorrow {0}", e.Start.ToString("t", CultureInfo.CurrentCulture));
        return e.Start.ToString("ddd t", CultureInfo.CurrentCulture);
    }

    private void Render()
    {
        var next = Next;
        var day = next?.Start ?? DateTime.Today;
        BadgeMonth.Text = day.ToString("MMM", CultureInfo.CurrentCulture).ToUpperInvariant();
        BadgeDay.Text = day.Day.ToString(CultureInfo.CurrentCulture);
        NextTitle.Text = next?.Title ?? (_error ?? L.T("No upcoming events"));
        NextTime.Text = next is null ? "" : Until(next) + (next.Location is { } loc ? " · " + loc : "");
        JoinButton.Visibility = next?.JoinUrl is not null && !next.AllDay && next.Start - DateTime.Now < TimeSpan.FromMinutes(15)
            ? Visibility.Visible : Visibility.Collapsed;
        CompactTime.Text = next is null ? "—" : Until(next);
        CompactLabel.Text = next?.Title ?? "";
        ToolTip = next is null ? _error ?? L.T("No upcoming events")
            : $"{next.Title}\n{next.Start:g} – {next.End:t}" + (next.Location is { } l ? $"\n{l}" : "");
        SetIdle(next is null);
        if (AgendaPopup.IsOpen) BuildAgenda();
        RefreshCompact();
    }

    private void NotifyUpcoming(DateTime now)
    {
        if (IsPreview || _settings.RemindMinutes <= 0) return;
        foreach (var e in _events.Where(e => !e.AllDay && e.Start > now && e.Start - now <= TimeSpan.FromMinutes(_settings.RemindMinutes)))
        {
            string key = $"{e.Title}|{e.Start:O}";
            if (!Notified.Add(key)) continue;
            AppServices.Notifications.Show(e.Title, $"{Until(e)}" + (e.Location is { } loc ? $" · {loc}" : ""), "calendar");
        }
    }

    private void BuildAgenda()
    {
        AgendaList.Children.Clear();
        var groups = _events.Where(e => e.Start.Date <= DateTime.Today.AddDays(1)).GroupBy(e => e.Start.Date).ToList();
        if (groups.Count == 0)
        {
            AgendaList.Children.Add(new TextBlock { Text = _error ?? L.T("No events today or tomorrow."), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondaryBrush"), Margin = new Thickness(4) });
            return;
        }
        foreach (var group in groups)
        {
            AgendaList.Children.Add(new TextBlock
            {
                Text = group.Key == DateTime.Today ? L.T("Today") : group.Key == DateTime.Today.AddDays(1) ? L.T("Tomorrow") : group.Key.ToString("D", CultureInfo.CurrentCulture),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 6, 4, 4),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });
            foreach (var e in group)
            {
                var time = new TextBlock
                {
                    Text = e.AllDay ? L.T("All day") : $"{e.Start:t}",
                    Width = 64,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                };
                var title = new TextBlock { Text = e.Title, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("TextPrimaryBrush") };
                var row = new DockPanel { Margin = new Thickness(4, 3, 4, 3) };
                DockPanel.SetDock(time, System.Windows.Controls.Dock.Left);
                row.Children.Add(time);
                if (e.JoinUrl is { } url)
                {
                    var join = new Button { Content = L.T("Join"), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0) };
                    join.Click += (_, _) => { ClosePopup(AgendaPopup); OpenUrl(url); };
                    DockPanel.SetDock(join, System.Windows.Controls.Dock.Right);
                    row.Children.Add(join);
                }
                row.Children.Add(title);
                AgendaList.Children.Add(row);
            }
        }
    }

    private void OnJoinClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Next?.JoinUrl is { } url) OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, "Failed to open meeting link"); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged || IsPreview || e.Handled) return;
        BuildAgenda();
        OpenPopup(AgendaPopup);
        e.Handled = true;
    }

    public override bool OnCompactClick()
    {
        BuildAgenda();
        OpenPopup(AgendaPopup);
        return true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(System.Windows.Media.Geometry.Parse(Icon), Next is null ? "TextSecondaryBrush" : "AccentRedBrush");
        tile.Text = Next is { } next ? Until(next) : null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Show agenda"), "", () => { BuildAgenda(); OpenPopup(AgendaPopup); }));
        items.Add(DockMenu.Item(L.T("Refresh now"), "", () => _ = LoadAsync()));
    }
}
