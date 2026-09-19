using System.Collections.ObjectModel;
using System.Windows.Threading;
using CustomDock.Core;
using Microsoft.Win32;

namespace CustomDock.Services;

public sealed class Reminder : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
}

public sealed class ReminderStore
{
    public List<Reminder> Items { get; set; } = new();
}

/// <summary>
/// Stores reminders and shows toast notifications when due.
/// Uses a one-shot timer targeting the nearest reminder instead of polling.
/// </summary>
public sealed class ReminderService : IDisposable
{
    private const string StoreName = "reminders";
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private readonly Dictionary<string, string> _recentlyFired = new();
    private EventHandler? _timeChangedHandler;
    private PowerModeChangedEventHandler? _powerModeChangedHandler;
    private bool _started;

    public ReminderService()
    {
        _timer.Tick += (_, _) => FireDue();
    }

    /// <summary>Pending reminders sorted chronologically.</summary>
    public ObservableCollection<Reminder> Items { get; } = new();

    public void Start()
    {
        if (_started) return;
        _started = true;

        foreach (var item in JsonStore.LoadData<ReminderStore>(StoreName).Items.OrderBy(r => r.Due))
            Items.Add(item);

        var dispatcher = _timer.Dispatcher;
        _timeChangedHandler = (_, _) => dispatcher.BeginInvoke(Schedule);
        _powerModeChangedHandler = (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) dispatcher.BeginInvoke(FireDue);
        };
        SystemEvents.TimeChanged += _timeChangedHandler;
        SystemEvents.PowerModeChanged += _powerModeChangedHandler;

        // Reminders that passed while the app was closed
        var missed = Items.Where(r => r.Due <= DateTime.Now).ToList();
        if (missed.Count > 0)
        {
            foreach (var r in missed) Items.Remove(r);
            Save();
            var body = missed.Count == 1
                ? $"{missed[0].Text} ({missed[0].Due:g})"
                : string.Join("\n", missed.Take(4).Select(r => $"• {r.Text}")) + (missed.Count > 4 ? $"\n+{missed.Count - 4} more" : "");
            AppServices.Notifications.Show(missed.Count == 1 ? "Missed reminder" : $"{missed.Count} missed reminders", body);
        }

        Schedule();
    }

    public Reminder Add(string text, DateTime due)
    {
        var reminder = new Reminder { Text = text.Trim(), Due = due };
        int index = 0;
        while (index < Items.Count && Items[index].Due <= due) index++;
        Items.Insert(index, reminder);
        Save();
        Schedule();
        return reminder;
    }

    public void Remove(Reminder reminder)
    {
        if (Items.Remove(reminder))
        {
            Save();
            Schedule();
        }
    }

    public void Snooze(string id, TimeSpan delay)
    {
        var text = _recentlyFired.TryGetValue(id, out var t) ? t : Items.FirstOrDefault(r => r.Id == id)?.Text;
        if (text is null) return;
        var existing = Items.FirstOrDefault(r => r.Id == id);
        if (existing is not null) Items.Remove(existing);
        Add(text, DateTime.Now + delay);
    }

    private void FireDue()
    {
        var now = DateTime.Now;
        var due = Items.Where(r => r.Due <= now.AddMilliseconds(500)).ToList();
        foreach (var reminder in due)
        {
            Items.Remove(reminder);
            _recentlyFired[reminder.Id] = reminder.Text;
            AppServices.Notifications.Show(
                "Reminder",
                reminder.Text,
                tag: "reminder-" + reminder.Id[..8],
                new ToastAction("Snooze 10m", NotificationService.ActionReminderSnooze, reminder.Id),
                new ToastAction("Done", NotificationService.ActionReminderDone, reminder.Id));
        }

        if (due.Count > 0) Save();
        Schedule();
    }

    private void Schedule()
    {
        _timer.Stop();
        if (Items.Count == 0) return;

        var wait = Items[0].Due - DateTime.Now;
        if (wait < TimeSpan.FromMilliseconds(200)) wait = TimeSpan.FromMilliseconds(200);
        if (wait > MaxWait) wait = MaxWait; // periodic re-evaluation against sleep / system clock change
        _timer.Interval = wait;
        _timer.Start();
    }

    private void Save() => JsonStore.SaveData(StoreName, new ReminderStore { Items = Items.ToList() });

    public void Dispose()
    {
        _timer.Stop();
        if (_timeChangedHandler is not null)
        {
            SystemEvents.TimeChanged -= _timeChangedHandler;
            _timeChangedHandler = null;
        }
        if (_powerModeChangedHandler is not null)
        {
            SystemEvents.PowerModeChanged -= _powerModeChangedHandler;
            _powerModeChangedHandler = null;
        }
    }
}
