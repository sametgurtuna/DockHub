using System.Windows.Input;

namespace CustomDock.Core;

/// <summary>A global keyboard shortcut such as "Ctrl+Alt+D" (modifiers in RegisterHotKey form plus a virtual key).</summary>
public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint modifiers = 0;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "windows": modifiers |= MOD_WIN; break;
                default:
                    if (key is not null) return null;
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (Enum.TryParse<Key>(raw, ignoreCase: true, out var parsed)) key = parsed;
                    else return null;
                    break;
            }
        }
        if (key is not { } k || modifiers == 0) return null;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        return vk == 0 ? null : new HotkeyGesture(modifiers, vk);
    }

    public static HotkeyGesture? FromKeys(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.System) return null;
        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;
        // A shortcut needs Ctrl, Alt or Win; Shift alone would hijack normal typing.
        if ((mods & (MOD_CONTROL | MOD_ALT | MOD_WIN)) == 0) return null;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk == 0 ? null : new HotkeyGesture(mods, vk);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
        if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        var key = KeyInterop.KeyFromVirtualKey((int)VirtualKey);
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>An action that can be bound to a global shortcut.</summary>
public sealed record HotkeyAction(string Id, string Name, string Description, string? DefaultGesture);

public static class HotkeyActions
{
    public const string ToggleDock = "toggle-dock";
    public const string FocusDock = "focus-dock";
    public const string OpenSettings = "open-settings";
    public const string PinApp = "pin-app";
    public const string ToggleAutoHide = "toggle-auto-hide";
    public const string ToggleMute = "toggle-mute";
    public const string VolumeUp = "volume-up";
    public const string VolumeDown = "volume-down";
    public const string NextProfile = "next-profile";
    public const string ClipboardHistory = "clipboard-history";

    public static IReadOnlyList<HotkeyAction> All { get; } = new[]
    {
        new HotkeyAction(ToggleDock, "Show the dock", "Brings the dock up (or hides it when auto-hide is on).", "Ctrl+Alt+D"),
        new HotkeyAction(FocusDock, "Move focus to the dock", "Use the arrow keys, Enter and the menu key on the dock.", "Win+Alt+T"),
        new HotkeyAction(OpenSettings, "Open DockHub settings", "", null),
        new HotkeyAction(PinApp, "Pin an application", "Opens the app picker.", null),
        new HotkeyAction(ToggleAutoHide, "Toggle auto-hide", "", null),
        new HotkeyAction(ToggleMute, "Mute or unmute", "Default audio output.", null),
        new HotkeyAction(VolumeUp, "Volume up", "", null),
        new HotkeyAction(VolumeDown, "Volume down", "", null),
        new HotkeyAction(NextProfile, "Switch to the next profile", "", null),
        new HotkeyAction(ClipboardHistory, "Clipboard history", "Opens the first Clipboard widget on the dock.", null),
    };

    public static HotkeyAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
