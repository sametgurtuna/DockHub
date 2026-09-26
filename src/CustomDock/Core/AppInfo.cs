namespace CustomDock.Core;

/// <summary>User-facing product name and derived text (centralized for renaming).</summary>
public static class AppInfo
{
    public const string Name = "DockHub";

    /// <summary>Pin command in context menus (Explorer and dock).</summary>
    public const string PinLabel = "Pin to DockHub";

    /// <summary>Product version (major.minor.patch).</summary>
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
