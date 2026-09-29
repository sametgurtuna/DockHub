using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomDock.Core;

/// <summary>A saved dock setup: its items and appearance. The active profile's items live in <see cref="AppConfig.Items"/>.</summary>
public sealed class DockProfile
{
    public string Id { get; set; } = DockItem.NewId();

    public string Name { get; set; } = "";

    /// <summary>Items as JSON (only stored for inactive profiles; the active one is the live config).</summary>
    public JsonArray? Items { get; set; }

    public JsonObject? Appearance { get; set; }

    /// <summary>Switch to this profile automatically when this many displays are connected (null = never).</summary>
    public int? AutoDisplayCount { get; set; }

    /// <summary>Switch in while an app with this executable name (e.g. "steam") has a window open.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AutoApp { get; set; }

    /// <summary>Switch in between these times ("HH:mm"); the range may pass midnight.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AutoTimeFrom { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AutoTimeTo { get; set; }

    /// <summary>The time rule only applies Monday to Friday.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoWeekdaysOnly { get; set; }

    internal bool HasTimeRule => ProfileRules.TryParseTime(AutoTimeFrom, out _) && ProfileRules.TryParseTime(AutoTimeTo, out _);
}

/// <summary>Matching logic of automatic profile switches (kept free of UI and shell types for tests).</summary>
public static class ProfileRules
{
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(text)
               && TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out time)
               && time < TimeSpan.FromDays(1);
    }

    /// <summary>Is <paramref name="now"/> inside the profile's time window?</summary>
    public static bool InTimeWindow(DockProfile profile, DateTime now)
    {
        if (!TryParseTime(profile.AutoTimeFrom, out var from) || !TryParseTime(profile.AutoTimeTo, out var to) || from == to) return false;
        // A window passing midnight belongs to the day it started.
        var day = now.TimeOfDay >= from || from < to ? now.DayOfWeek : now.AddDays(-1).DayOfWeek;
        if (profile.AutoWeekdaysOnly && day is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        var t = now.TimeOfDay;
        return from < to ? t >= from && t < to : t >= from || t < to;
    }

    /// <summary>Normalizes "Steam.exe", "steam" and full paths to "steam".</summary>
    public static string NormalizeApp(string app)
    {
        var name = app.Trim().Split('\\', '/').Last();
        return (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name).ToLowerInvariant();
    }

    /// <summary>
    /// The profile the rules ask for: an app rule wins over a time rule (a game started during work hours).
    /// Returns null when no rule applies.
    /// </summary>
    public static DockProfile? Pick(IEnumerable<DockProfile> profiles, IReadOnlySet<string> runningApps, DateTime now)
    {
        var list = profiles.ToList();
        return list.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.AutoApp) && runningApps.Contains(NormalizeApp(p.AutoApp!)))
               ?? list.FirstOrDefault(p => InTimeWindow(p, now));
    }
}

/// <summary>
/// Dock profiles (e.g. "Work", "Gaming", "Laptop"): each keeps its own items and look. Switching is one undoable
/// step. A profile can switch in by itself when a given number of displays is connected.
/// </summary>
public sealed class ProfileService
{
    private readonly ConfigService _service;

    public ProfileService(ConfigService service) => _service = service;

    private AppConfig Config => _service.Config;

    public IReadOnlyList<DockProfile> Profiles => Config.Profiles;

    public DockProfile? Active => Config.Profiles.FirstOrDefault(p => p.Id == Config.ActiveProfileId);

    public event Action? Changed;

    /// <summary>Profile that was active before a rule switched another one in; returned to when the rule ends.</summary>
    private string? _returnProfileId;
    private string? _ruleProfileId;
    private Func<IReadOnlySet<string>>? _runningApps;

    /// <summary>Starts automatic switching by app and time (called once the shell runs).</summary>
    public void StartRules(Func<IReadOnlySet<string>> runningApps)
    {
        _runningApps = runningApps;
        EvaluateRules();
    }

    /// <summary>Applies app and time rules. Cheap; called when apps start or stop and every minute.</summary>
    public void EvaluateRules()
    {
        if (_runningApps is null || Config.Profiles.Count < 2) return;
        var wanted = ProfileRules.Pick(Config.Profiles, _runningApps(), DateTime.Now);

        if (wanted is not null)
        {
            if (wanted.Id == Config.ActiveProfileId) return;
            // Remember where to go back to, unless a rule had already switched (then keep the original).
            if (_ruleProfileId is null) _returnProfileId = Config.ActiveProfileId;
            _ruleProfileId = wanted.Id;
            Log.Info($"Profile rule: switching to {wanted.Name}");
            SwitchToCore(wanted.Id);
        }
        else if (_ruleProfileId is not null && _ruleProfileId == Config.ActiveProfileId)
        {
            // The rule ended: back to the profile the user had.
            var back = _returnProfileId;
            _ruleProfileId = null;
            _returnProfileId = null;
            if (back is not null && Config.Profiles.Any(p => p.Id == back))
            {
                Log.Info("Profile rule ended: switching back");
                SwitchToCore(back);
            }
        }
        else
        {
            _ruleProfileId = null;
        }
    }

    public void SetAutoApp(string profileId, string? app)
    {
        if (Config.Profiles.FirstOrDefault(p => p.Id == profileId) is not { } profile) return;
        profile.AutoApp = string.IsNullOrWhiteSpace(app) ? null : ProfileRules.NormalizeApp(app);
        Save();
        EvaluateRules();
    }

    public void SetAutoTime(string profileId, string? from, string? to, bool weekdaysOnly)
    {
        if (Config.Profiles.FirstOrDefault(p => p.Id == profileId) is not { } profile) return;
        profile.AutoTimeFrom = string.IsNullOrWhiteSpace(from) ? null : from.Trim();
        profile.AutoTimeTo = string.IsNullOrWhiteSpace(to) ? null : to.Trim();
        profile.AutoWeekdaysOnly = weekdaysOnly;
        Save();
        EvaluateRules();
    }

    /// <summary>Saves the current setup as a new profile and makes it active.</summary>
    public DockProfile SaveCurrentAs(string name)
    {
        EnsureDefaultProfile();
        StoreActive();
        var profile = new DockProfile { Name = name.Trim().Length > 0 ? name.Trim() : L.T("Profile {0}", Config.Profiles.Count + 1) };
        Config.Profiles.Add(profile);
        Config.ActiveProfileId = profile.Id;
        Save();
        return profile;
    }

    /// <summary>Switches to another profile by hand (current setup is stored in the active profile first).</summary>
    public void SwitchTo(string profileId)
    {
        // A manual choice wins over rules until they change again.
        _ruleProfileId = null;
        _returnProfileId = null;
        SwitchToCore(profileId);
    }

    private void SwitchToCore(string profileId)
    {
        var target = Config.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (target is null || target.Id == Config.ActiveProfileId) return;

        // An edit of the dock belongs to the profile it was made in.
        Dock.DockEditMode.Exit(announce: false);
        StoreActive();

        var items = target.Items?.Deserialize<List<DockItem>>(JsonStore.Options) ?? new List<DockItem>();
        if (target.Appearance is { } appearance) ConfigHistory.RestoreAppearance(Config, appearance);
        Config.ActiveProfileId = target.Id;
        target.Items = null;
        Config.Items = items;
        Config.NotifyItemsChanged();
        Save();
        Log.Info($"Switched to profile {target.Name}");
    }

    public void SwitchToNext()
    {
        if (Config.Profiles.Count < 2) return;
        int index = Config.Profiles.FindIndex(p => p.Id == Config.ActiveProfileId);
        SwitchTo(Config.Profiles[(index + 1) % Config.Profiles.Count].Id);
    }

    public void Rename(string profileId, string name)
    {
        if (Config.Profiles.FirstOrDefault(p => p.Id == profileId) is not { } profile || name.Trim().Length == 0) return;
        profile.Name = name.Trim();
        Save();
    }

    public void SetAutoDisplayCount(string profileId, int? count)
    {
        if (Config.Profiles.FirstOrDefault(p => p.Id == profileId) is not { } profile) return;
        // One profile per display count.
        if (count is not null)
            foreach (var other in Config.Profiles.Where(p => p.AutoDisplayCount == count)) other.AutoDisplayCount = null;
        profile.AutoDisplayCount = count;
        Save();
    }

    /// <summary>Deletes an inactive profile (the active one can't be deleted).</summary>
    public void Delete(string profileId)
    {
        if (profileId == Config.ActiveProfileId) return;
        Config.Profiles.RemoveAll(p => p.Id == profileId);
        if (Config.Profiles.Count == 1 && Config.Profiles[0].Id == Config.ActiveProfileId && Config.Profiles[0].AutoDisplayCount is null)
        {
            // A single profile is the same as having none.
            Config.Profiles.Clear();
            Config.ActiveProfileId = null;
        }
        Save();
    }

    /// <summary>Called when displays change: switches to the profile set up for this many displays.</summary>
    public void ApplyDisplayRule(int displayCount)
    {
        var match = Config.Profiles.FirstOrDefault(p => p.AutoDisplayCount == displayCount);
        if (match is not null && match.Id != Config.ActiveProfileId)
        {
            Log.Info($"{displayCount} display(s) connected: switching to profile {match.Name}");
            SwitchToCore(match.Id);
        }
    }

    /// <summary>The first profile is the setup the user already had.</summary>
    private void EnsureDefaultProfile()
    {
        if (Config.Profiles.Count > 0 && Active is not null) return;
        var initial = new DockProfile { Name = L.T("Default") };
        Config.Profiles.Insert(0, initial);
        Config.ActiveProfileId = initial.Id;
    }

    /// <summary>Copies the live items and appearance into the active profile.</summary>
    private void StoreActive()
    {
        if (Active is not { } active) return;
        active.Items = JsonSerializer.SerializeToNode(Config.Items, JsonStore.Options) as JsonArray;
        active.Appearance = ConfigHistory.CaptureAppearance(Config);
    }

    private void Save()
    {
        // The active profile doesn't keep a copy of the live items.
        if (Active is { } active) active.Items = null;
        _service.ScheduleSave();
        Changed?.Invoke();
    }
}
