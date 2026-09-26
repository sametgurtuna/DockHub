using System.ComponentModel;
using System.Windows.Interop;
using CustomDock.Core;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

/// <summary>
/// Registers the configurable global shortcuts (<see cref="HotkeyActions"/>) on a message-only window and runs
/// their actions. Shortcuts another app already owns are reported through <see cref="Failed"/>.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int FirstId = 0xD100;

    private readonly AppConfig _config;
    private readonly Dictionary<string, Action> _handlers;
    private readonly HwndSource _window;
    private readonly Dictionary<int, string> _registered = new();
    private readonly HashSet<string> _failed = new();

    public HotkeyService(AppConfig config, Dictionary<string, Action> handlers)
    {
        _config = config;
        _handlers = handlers;
        _window = new HwndSource(new HwndSourceParameters("DockHubHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _window.AddHook(WndProc);
        _config.PropertyChanged += OnConfigChanged;
        RegisterAll();
    }

    /// <summary>Actions whose shortcut couldn't be registered (usually taken by another app).</summary>
    public IReadOnlyCollection<string> Failed => _failed;

    public event Action? RegistrationChanged;

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppConfig.Hotkeys)) RegisterAll();
    }

    private void RegisterAll()
    {
        UnregisterAll();
        int id = FirstId;
        foreach (var action in HotkeyActions.All)
        {
            if (!_handlers.ContainsKey(action.Id) || _config.GetHotkey(action.Id) is not { } gesture) continue;
            if (RegisterHotKey(_window.Handle, id, gesture.Modifiers | MOD_NOREPEAT, gesture.VirtualKey))
            {
                _registered[id] = action.Id;
                Log.Info($"Shortcut {gesture} -> {action.Id}");
            }
            else
            {
                _failed.Add(action.Id);
                Log.Warn($"Shortcut {gesture} for {action.Id} is already in use by another app.");
            }
            id++;
        }
        RegistrationChanged?.Invoke();
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
        _failed.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var actionId) &&
            _handlers.TryGetValue(actionId, out var handler))
        {
            handled = true;
            try { handler(); }
            catch (Exception ex) { Log.Error(ex, $"Shortcut action {actionId} failed"); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _config.PropertyChanged -= OnConfigChanged;
        UnregisterAll();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
