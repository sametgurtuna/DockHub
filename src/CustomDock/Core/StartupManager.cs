using Microsoft.Win32;

namespace CustomDock.Core;

/// <summary>Manages "Start with Windows" via HKCU\...\Run registry key (no admin privileges required).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DockHub";
    private const string LegacyValueName = "CustomDock";
    public const string StartupArgument = "--startup";

    private static string Command => $"\"{Environment.ProcessPath}\" {StartupArgument}";

    /// <summary>The "Start with Windows" selection from the installer wizard (HKCU\Software\DockHub); null if not set.</summary>
    public static bool? InstallerChoice()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\DockHub");
            return key?.GetValue("StartWithWindows") is int value ? value != 0 : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value
               && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key.GetValue(LegacyValueName) is not null)
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            if (enabled)
                key.SetValue(ValueName, Command);
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update startup registry entry");
        }
    }
}
