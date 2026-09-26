using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using Microsoft.Win32;

namespace CustomDock.Services;

public sealed record VirtualDesktopInfo(Guid Id, int Number, string Name);

/// <summary>
/// Windows virtual desktops: the list and the current one (from Explorer's registry values, watched without
/// polling), whether a window sits on another desktop (documented IVirtualDesktopManager), and switching through the
/// same shortcuts the keyboard uses (Win+Ctrl+Left/Right, Win+Ctrl+D, Win+Ctrl+F4).
/// </summary>
public sealed class VirtualDesktopService
{
    private const string DesktopsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private IVirtualDesktopManager? _manager;
    private RegistryWatcher? _watcher;
    private RegistryWatcher? _sessionWatcher;
    private bool _started;

    public IReadOnlyList<VirtualDesktopInfo> Desktops { get; private set; } = Array.Empty<VirtualDesktopInfo>();

    public VirtualDesktopInfo? Current { get; private set; }

    /// <summary>Raised on the UI thread when desktops are added, removed, renamed or switched.</summary>
    public event Action? Changed;

    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        try { _manager = (IVirtualDesktopManager)new VirtualDesktopManagerCom(); }
        catch (Exception ex) { Log.Debug($"Virtual desktop manager unavailable: {ex.Message}"); }

        Reload();
        _watcher = new RegistryWatcher(RegistryHive.CurrentUser, DesktopsKey, subtree: true);
        _watcher.Changed += QueueReload;
        _watcher.Start();

        // Windows 10 keeps the current desktop per session.
        _sessionWatcher = new RegistryWatcher(RegistryHive.CurrentUser, SessionKey, subtree: true);
        _sessionWatcher.Changed += QueueReload;
        _sessionWatcher.Start();
    }

    private static string SessionKey => $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{Process.GetCurrentProcess().SessionId}\VirtualDesktops";

    private void QueueReload() => Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        var before = (Current?.Id, Desktops.Count, string.Join("|", Desktops.Select(d => d.Name)));
        Reload();
        if (before != (Current?.Id, Desktops.Count, string.Join("|", Desktops.Select(d => d.Name)))) Changed?.Invoke();
    });

    private void Reload()
    {
        var ids = new List<Guid>();
        Guid? current = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopsKey);
            if (key?.GetValue("VirtualDesktopIDs") is byte[] raw)
                for (int i = 0; i + 16 <= raw.Length; i += 16) ids.Add(new Guid(raw.AsSpan(i, 16)));
            current = ReadGuid(key?.GetValue("CurrentVirtualDesktop"));
            if (current is null)
            {
                using var session = Registry.CurrentUser.OpenSubKey(SessionKey);
                current = ReadGuid(session?.GetValue("CurrentVirtualDesktop"));
            }

            var list = new List<VirtualDesktopInfo>();
            for (int i = 0; i < ids.Count; i++)
            {
                using var desktop = key?.OpenSubKey($@"Desktops\{ids[i]:B}");
                string name = desktop?.GetValue("Name") as string ?? "";
                list.Add(new VirtualDesktopInfo(ids[i], i + 1, string.IsNullOrWhiteSpace(name) ? L.T("Desktop {0}", i + 1) : name));
            }
            Desktops = list;
            Current = list.FirstOrDefault(d => d.Id == current) ?? list.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Debug($"Virtual desktops could not be read: {ex.Message}");
        }
    }

    private static Guid? ReadGuid(object? value) => value is byte[] { Length: >= 16 } bytes ? new Guid(bytes.AsSpan(0, 16)) : null;

    /// <summary>True for a window that belongs on the taskbar of another virtual desktop.</summary>
    public bool IsOnOtherDesktop(IntPtr hwnd)
    {
        if (_manager is null) return false;
        try
        {
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED_ATTRIBUTE, out int cloaked, sizeof(int)) != 0
                || (cloaked & NativeMethods.DWM_CLOAKED_SHELL) == 0) return false;
            if (_manager.GetWindowDesktopId(hwnd, out var desktop) != 0 || desktop == Guid.Empty) return false;
            if (_manager.IsWindowOnCurrentVirtualDesktop(hwnd, out int onCurrent) != 0 || onCurrent != 0) return false;
            return Desktops.Count == 0 || Desktops.Any(d => d.Id == desktop);
        }
        catch
        {
            return false;
        }
    }

    public string? DesktopNameOf(IntPtr hwnd)
    {
        if (_manager is null) return null;
        try
        {
            return _manager.GetWindowDesktopId(hwnd, out var id) == 0 ? Desktops.FirstOrDefault(d => d.Id == id)?.Name : null;
        }
        catch { return null; }
    }

    /// <summary>Moves to a desktop by sending Win+Ctrl+Left/Right as many times as needed.</summary>
    public void SwitchTo(VirtualDesktopInfo target)
    {
        if (Current is null) return;
        int steps = target.Number - Current.Number;
        var arrow = steps > 0 ? InputHelper.VK_RIGHT : InputHelper.VK_LEFT;
        for (int i = 0; i < Math.Abs(steps); i++)
            InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_CONTROL, arrow);
    }

    public void Next() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_CONTROL, InputHelper.VK_RIGHT);

    public void Previous() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_CONTROL, InputHelper.VK_LEFT);

    public void CreateDesktop() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_CONTROL, InputHelper.VK_D);

    public void CloseCurrent() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_CONTROL, InputHelper.VK_F4);

    [ComImport]
    [Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerCom
    {
    }

    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }
}
