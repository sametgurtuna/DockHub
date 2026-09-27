namespace CustomDock.Services;

public enum BatteryDeviceKind { Unknown, Headset, Mouse, Keyboard, Controller }

public sealed record BatteryDeviceInfo(string Id, string Name, int BatteryPercent, bool IsCharging, bool IsWirelessDongle,
    BatteryDeviceKind Kind = BatteryDeviceKind.Unknown)
{
    public string FormattedPercent => $"{BatteryPercent}%";

    /// <summary>The known kind, or a guess from the name for devices that don't report one (Bluetooth).</summary>
    public BatteryDeviceKind EffectiveKind => Kind != BatteryDeviceKind.Unknown ? Kind : GuessKind(Name);

    public bool IsHeadset => EffectiveKind == BatteryDeviceKind.Headset;

    public bool IsMouse => EffectiveKind == BatteryDeviceKind.Mouse;

    public bool IsKeyboard => EffectiveKind == BatteryDeviceKind.Keyboard;

    public bool IsController => EffectiveKind == BatteryDeviceKind.Controller;

    internal static BatteryDeviceKind GuessKind(string name)
    {
        if (ContainsAny(name, "cloud", "headset", "kulak", "headphone", "buds", "arctis")) return BatteryDeviceKind.Headset;
        if (ContainsAny(name, "mouse", "fare", "lamzu")) return BatteryDeviceKind.Mouse;
        if (ContainsAny(name, "keyboard", "klavye")) return BatteryDeviceKind.Keyboard;
        if (ContainsAny(name, "controller", "gamepad", "dualsense", "dualshock", "kumanda")) return BatteryDeviceKind.Controller;
        return BatteryDeviceKind.Unknown;
    }

    private static bool ContainsAny(string text, params string[] words)
        => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
