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

    /// <summary>Switches to another profile (current setup is stored in the active profile first).</summary>
    public void SwitchTo(string profileId)
    {
        var target = Config.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (target is null || target.Id == Config.ActiveProfileId) return;

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
            SwitchTo(match.Id);
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
