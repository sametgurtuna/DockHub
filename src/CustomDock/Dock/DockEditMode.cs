using CustomDock.Core;

namespace CustomDock.Dock;

/// <summary>A dock that can show its items in edit mode.</summary>
public interface IEditableDock
{
    /// <summary>Shows the edit decorations (remove badges, resize handles, add tile, Done button).</summary>
    void BeginEditing();

    /// <summary>Removes them again.</summary>
    void EndEditing();
}

/// <summary>
/// The dock's edit mode: one dock at a time, and everything done meanwhile (moving, removing, resizing and adding
/// items) is a single undo step. Opening and closing it without a change leaves no step behind.
/// </summary>
public static class DockEditMode
{
    private static HistorySession? s_session;
    private static AppConfig? s_config;

    /// <summary>The dock being edited, or null.</summary>
    public static IEditableDock? Current { get; private set; }

    public static bool IsActive => Current is not null;

    /// <summary>Raised after edit mode starts (true) or ends (false).</summary>
    public static event Action<bool>? Changed;

    public static void Enter(IEditableDock dock) => Enter(dock, AppServices.ConfigService.History, AppServices.Config);

    /// <summary>Starts editing <paramref name="dock"/>; a dock that was being edited finishes first.</summary>
    public static void Enter(IEditableDock dock, ConfigHistory history, AppConfig config)
    {
        if (ReferenceEquals(Current, dock)) return;
        Exit();
        s_config = config;
        s_session = history.BeginSession(config, L.T("Edited the dock"));
        Current = dock;
        try
        {
            dock.BeginEditing();
        }
        catch
        {
            Exit();
            throw;
        }
        Changed?.Invoke(true);
    }

    /// <summary>
    /// Finishes editing. Returns true when the dock changed: that is one undo step, shown with an undo toast unless
    /// <paramref name="announce"/> is false (the dock is closing).
    /// </summary>
    public static bool Exit(bool announce = true)
    {
        if (Current is not { } dock) return false;
        Current = null;
        bool changed = false;
        try
        {
            dock.EndEditing();
        }
        finally
        {
            if (s_session is { } session && s_config is { } config) changed = session.End(config, announce);
            s_session = null;
            s_config = null;
            Changed?.Invoke(false);
        }
        return changed;
    }
}
