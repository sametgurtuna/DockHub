using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Shell;

/// <summary>
/// Handles executables that live in versioned install folders, so pins and window matching survive app updates:
/// <list type="bullet">
/// <item>Squirrel apps (Discord, Slack, Teams classic...): <c>%LocalAppData%\App\app-1.2.3\App.exe</c>, next to <c>Update.exe</c>.</item>
/// <item>MSIX packaged apps: <c>C:\Program Files\WindowsApps\Name_1.2.3.0_x64__publisher\...</c>.</item>
/// </list>
/// </summary>
public static class AppPathResolver
{
    private static readonly Regex SquirrelRx = new(
        @"^(?<root>.+?)\\app-\d+(?:\.\d+){1,3}(?:-[^\\]+)?\\(?<rest>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MsixRx = new(
        @"^(?<prefix>.+?\\WindowsApps)\\(?<name>[^_\\]+)_(?<version>[\d.]+)_(?<arch>[^_\\]*)_(?<resource>[^_\\]*)_(?<publisher>[^_\\]+)\\(?<rest>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProcessStartRx = new(
        @"--processStart(?:AndWait)?\s+(?:""(?<exe>[^""]+)""|(?<exe>\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string?> PackageAumidCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> SquirrelRootCache = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Parsing (pure)

    /// <summary>Squirrel install root and the path inside the versioned folder, or null.</summary>
    public static (string Root, string RelativePath)? ParseSquirrel(string path)
    {
        var match = SquirrelRx.Match(path);
        return match.Success ? (match.Groups["root"].Value, match.Groups["rest"].Value) : null;
    }

    /// <summary>Package family name (Name_PublisherId) and the path inside the package, or null.</summary>
    public static (string FamilyName, string RelativePath)? ParseMsix(string path)
    {
        var match = MsixRx.Match(path);
        return match.Success
            ? ($"{match.Groups["name"].Value}_{match.Groups["publisher"].Value}", match.Groups["rest"].Value)
            : null;
    }

    /// <summary>Executable named by Squirrel's <c>Update.exe --processStart App.exe</c> arguments, or null.</summary>
    public static string? ParseProcessStart(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        var match = ProcessStartRx.Match(arguments);
        return match.Success ? match.Groups["exe"].Value : null;
    }

    public static bool IsSquirrelUpdater(string path)
        => Path.GetFileName(path).Equals("Update.exe", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Matching keys

    /// <summary>
    /// Version-independent identity of an executable (lower case). Squirrel apps map to <c>root\app-*\exe</c>,
    /// whether the path points into a version folder or at the root stub; MSIX apps map to <c>msix:family\relative</c>.
    /// </summary>
    public static string NormalizeExe(string exePath, string? arguments = null)
    {
        if (ParseSquirrel(exePath) is { } squirrel)
            return $@"{squirrel.Root}\app-*\{squirrel.RelativePath}".ToLowerInvariant();

        if (ParseMsix(exePath) is { } msix)
            return $@"msix:{msix.FamilyName}\{msix.RelativePath}".ToLowerInvariant();

        string? directory = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(directory))
        {
            if (IsSquirrelUpdater(exePath) && ParseProcessStart(arguments) is { } started)
                return $@"{directory}\app-*\{started}".ToLowerInvariant();

            // Root stub (e.g. %LocalAppData%\Discord\Discord.exe) that starts the newest version folder.
            if (IsSquirrelRoot(directory) && !IsSquirrelUpdater(exePath))
                return $@"{directory}\app-*\{Path.GetFileName(exePath)}".ToLowerInvariant();
        }

        return exePath.ToLowerInvariant();
    }

    private static bool IsSquirrelRoot(string directory)
    {
        if (SquirrelRootCache.TryGetValue(directory, out bool cached)) return cached;
        bool result = false;
        try
        {
            result = File.Exists(Path.Combine(directory, "Update.exe")) &&
                     Directory.EnumerateDirectories(directory, "app-*").Any();
        }
        catch
        {
            // Access denied etc.
        }
        SquirrelRootCache[directory] = result;
        return result;
    }

    // ------------------------------------------------------------------ Pinning

    /// <summary>
    /// Path (and arguments) to store when pinning a running executable, avoiding versioned folders that
    /// disappear on the next update. <paramref name="aumid"/> is the window's AppUserModelID, if known.
    /// </summary>
    public static (string Path, string? Arguments) StablePinPath(string exePath, string? aumid)
    {
        if (ParseSquirrel(exePath) is { } squirrel && !squirrel.RelativePath.Contains('\\'))
        {
            string stub = Path.Combine(squirrel.Root, squirrel.RelativePath);
            if (File.Exists(stub)) return (stub, null);
            string updater = Path.Combine(squirrel.Root, "Update.exe");
            if (File.Exists(updater)) return (updater, $"--processStart \"{squirrel.RelativePath}\"");
        }

        if (ParseMsix(exePath) is { } msix)
        {
            string? appId = !string.IsNullOrWhiteSpace(aumid) ? aumid : PackageAumid(msix.FamilyName);
            if (!string.IsNullOrWhiteSpace(appId)) return (AppKeys.AppsFolderPrefix + appId, null);
        }

        return (exePath, null);
    }

    /// <summary>AppUserModelID of a packaged app when its package has exactly one app entry.</summary>
    public static string? PackageAumid(string familyName)
    {
        if (PackageAumidCache.TryGetValue(familyName, out var cached)) return cached;
        string? result = null;
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            var package = manager.FindPackagesForUser(string.Empty, familyName).FirstOrDefault();
            if (package is not null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                var entries = package.GetAppListEntries();
                if (entries.Count == 1)
                    result = entries[0].AppUserModelId;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Package lookup failed for {familyName}: {ex.Message}");
        }
        PackageAumidCache[familyName] = result;
        return result;
    }

    /// <summary>Current path of an executable whose versioned folder no longer exists, or null.</summary>
    public static string? Repair(string path)
    {
        try
        {
            if (File.Exists(path)) return path;

            if (ParseSquirrel(path) is { } squirrel)
                return NewestSquirrelFile(squirrel.Root, squirrel.RelativePath);

            if (ParseMsix(path) is { } msix)
            {
                var manager = new Windows.Management.Deployment.PackageManager();
                var package = manager.FindPackagesForUser(string.Empty, msix.FamilyName).FirstOrDefault();
                if (package?.InstalledLocation?.Path is { } location)
                {
                    string candidate = Path.Combine(location, msix.RelativePath);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not repair path {path}: {ex.Message}");
        }
        return null;
    }

    /// <summary><paramref name="relativePath"/> inside the newest <c>app-x.y.z</c> folder of a Squirrel install, or null.</summary>
    public static string? NewestSquirrelFile(string root, string relativePath)
    {
        try
        {
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateDirectories(root, "app-*")
                .Select(dir => (Dir: dir, Version: ParseVersion(Path.GetFileName(dir)[4..])))
                .Where(d => d.Version is not null && File.Exists(Path.Combine(d.Dir, relativePath)))
                .OrderByDescending(d => d.Version)
                .Select(d => Path.Combine(d.Dir, relativePath))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Version? ParseVersion(string text)
    {
        int dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];
        return Version.TryParse(text, out var version) ? version : null;
    }
}
