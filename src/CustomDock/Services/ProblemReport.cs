using System.Diagnostics;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Shell;

namespace CustomDock.Services;

/// <summary>Opens a prefilled GitHub bug report (Settings › About › Report a problem, and the crash notice).</summary>
public static class ProblemReport
{
    /// <summary>A crash older than this is left out of a report started from Settings (it is probably unrelated).</summary>
    public static readonly TimeSpan RecentCrash = TimeSpan.FromDays(7);

    /// <summary>
    /// Opens the browser. <paramref name="withLastCrash"/>: true adds the last crash, false leaves it out, null (Settings)
    /// adds it when it is recent.
    /// </summary>
    public static void Open(bool? withLastCrash = null)
    {
        var crash = SessionState.Load().LastCrash;
        bool withCrash = crash is not null && (withLastCrash ?? DateTime.Now - crash.Time <= RecentCrash);
        try
        {
            Process.Start(new ProcessStartInfo(IssueReport.BuildUrl(CurrentEnvironment(withCrash ? crash : null))) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warn($"Couldn't open the browser for a bug report: {ex.Message}");
        }
    }

    public static IssueEnvironment CurrentEnvironment(CrashRecord? crash)
    {
        var config = AppServices.Config;
        string language = L.Languages.FirstOrDefault(l => l.Code == L.Code).NativeName is { } name ? $"{name} ({L.Code})" : L.Code;
        return new IssueEnvironment(
            AppInfo.Version,
            WindowsVersionName(),
            config.Language == UiLanguage.System ? $"{language}, following Windows" : language,
            config.TaskbarMode.ToString(),
            MonitorHelper.GetAll().Count,
            ItemDataStore.Flatten(config.Items).Where(i => i.Kind == DockItemKind.Widget && i.Widget is not null).Select(i => i.Widget!).ToList(),
            crash?.Report());
    }

    private static string WindowsVersionName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return IssueReport.WindowsName(Environment.OSVersion.Version.Build, key?.GetValue("DisplayVersion") as string,
                key?.GetValue("UBR") is int revision ? revision : 0);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return IssueReport.WindowsName(Environment.OSVersion.Version.Build, null, 0);
        }
    }
}
