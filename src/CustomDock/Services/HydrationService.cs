using System.ComponentModel;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

public sealed class HydrationSettings : ObservableObject
{
    private int _dailyGoal = 8;
    private int _intervalMinutes = 60;
    private int _startHour = 9;
    private int _endHour = 22;
    private bool _notificationsEnabled = true;
    private int _glassMl = 250;

    public int DailyGoal { get => _dailyGoal; set => Set(ref _dailyGoal, Math.Clamp(value, 1, 30)); }

    public int IntervalMinutes { get => _intervalMinutes; set => Set(ref _intervalMinutes, Math.Clamp(value, 5, 600)); }

    public int StartHour { get => _startHour; set => Set(ref _startHour, Math.Clamp(value, 0, 23)); }

    public int EndHour { get => _endHour; set => Set(ref _endHour, Math.Clamp(value, 1, 24)); }

    public bool NotificationsEnabled { get => _notificationsEnabled; set => Set(ref _notificationsEnabled, value); }

    public int GlassMl { get => _glassMl; set => Set(ref _glassMl, Math.Clamp(value, 50, 2000)); }
}

public sealed class HydrationState
{
    public string Date { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastEvent { get; set; }
}

/// <summary>Daily water tracker and periodic hydration reminders.</summary>
public sealed class HydrationService
{
    private const string StoreName = "hydration";

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private HydrationState? _state;
    private HydrationSettings _settings = new();
    private bool _running;

    public HydrationService()
    {
        _timer.Tick += (_, _) => OnTimer();
    }

    /// <summary>Triggered when count changes.</summary>
    public event Action? Changed;

    public int Count
    {
        get
        {
            EnsureToday();
            return State.Count;
        }
    }

    public HydrationSettings Settings => _settings;

    /// <summary>Time for the next hydration reminder (interval duration after last event).</summary>
    public DateTime NextReminder => State.LastEvent == default
        ? DateTime.Now + TimeSpan.FromMinutes(_settings.IntervalMinutes)
        : State.LastEvent + TimeSpan.FromMinutes(_settings.IntervalMinutes);

    public TimeSpan Interval => TimeSpan.FromMinutes(_settings.IntervalMinutes);

    private HydrationState State => _state ??= JsonStore.LoadData<HydrationState>(StoreName);

    public void Start(HydrationSettings settings)
    {
        if (!ReferenceEquals(_settings, settings))
        {
            _settings.PropertyChanged -= OnSettingsChanged;
            _settings = settings;
            _settings.PropertyChanged += OnSettingsChanged;
        }

        _running = true;
        if (State.LastEvent == default)
        {
            State.LastEvent = DateTime.Now;
            Save();
        }
        EnsureToday();
        Schedule();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    public void AddGlass()
    {
        EnsureToday();
        State.Count = Math.Min(State.Count + 1, 99);
        State.LastEvent = DateTime.Now;
        Save();
        Schedule();
        Changed?.Invoke();
    }

    public void RemoveGlass()
    {
        EnsureToday();
        if (State.Count == 0) return;
        State.Count--;
        Save();
        Changed?.Invoke();
    }

    public void Reset()
    {
        EnsureToday();
        State.Count = 0;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Resets count when the day changes.</summary>
    public void EnsureToday()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (State.Date == today) return;
        State.Date = today;
        State.Count = 0;
        Save();
        Changed?.Invoke();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Schedule();
        Changed?.Invoke();
    }

    private bool IsActiveHour(DateTime time)
    {
        int start = _settings.StartHour, end = _settings.EndHour;
        return start < end
            ? time.Hour >= start && time.Hour < end
            : time.Hour >= start || time.Hour < end; // interval crosses midnight
    }

    private void OnTimer()
    {
        EnsureToday();
        var now = DateTime.Now;
        if (_settings.NotificationsEnabled && IsActiveHour(now)
            && now - State.LastEvent >= TimeSpan.FromMinutes(_settings.IntervalMinutes) - TimeSpan.FromSeconds(5)
            && State.Count < _settings.DailyGoal)
        {
            AppServices.Notifications.Show(
                "Time for water 💧",
                $"You've had {State.Count}/{_settings.DailyGoal} glasses today. Have another ({_settings.GlassMl} ml)?",
                tag: "hydration",
                new ToastAction("Drank ✓", NotificationService.ActionHydrationDrink));
            State.LastEvent = now;
            Save();
        }
        Schedule();
    }

    private void Schedule()
    {
        _timer.Stop();
        if (!_running || !_settings.NotificationsEnabled) return;

        var now = DateTime.Now;
        var due = State.LastEvent + TimeSpan.FromMinutes(_settings.IntervalMinutes);
        if (due < now) due = now + TimeSpan.FromMinutes(1);

        if (!IsActiveHour(due))
        {
            var next = due.Date.AddHours(_settings.StartHour);
            if (next <= due) next = next.AddDays(1);
            due = next;
        }

        var wait = due - now;
        if (wait > TimeSpan.FromMinutes(30)) wait = TimeSpan.FromMinutes(30); // guard against sleep / time change
        if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
        _timer.Interval = wait;
        _timer.Start();
    }

    private void Save() => JsonStore.SaveData(StoreName, State);
}
