using System.Runtime.InteropServices;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

/// <summary>
/// Explorer görev çubuğunu gizler ve geri getirir.
/// <list type="bullet">
/// <item>Görev çubuğu "otomatik gizle" durumuna alınır (çalışma alanı serbest kalır) ve pencereleri gizlenir.</item>
/// <item>Explorer görev çubuğunu kendiliğinden gösterirse (Explorer yeniden başlatma, ayar değişikliği) tekrar gizlenir;
///       ancak Başlat / Arama / Hızlı ayarlar açıkken beklenir, aksi halde bu menüler açılamaz.</item>
/// <item>Orijinal durum session.json'a yazılır; uygulama çökse bile sonraki açılışta geri yüklenir.</item>
/// </list>
/// </summary>
public sealed class TaskbarController : IDisposable
{
    private static readonly HashSet<string> ShellFlyoutClasses = new(StringComparer.Ordinal)
    {
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", "ControlCenterWindow",
        "Shell_InputSwitchTopLevelWindow",
    };

    private readonly Func<IntPtr> _ownTrayProvider;
    private readonly Func<bool> _launcherVisible;
    private readonly DispatcherTimer _monitor;
    private readonly EventHandler _onTick;
    private int _originalState;
    private int _visibleTicks;

    public TaskbarController(Func<IntPtr> ownTrayProvider, Func<bool> launcherVisible)
    {
        _ownTrayProvider = ownTrayProvider;
        _launcherVisible = launcherVisible;
        _onTick = (_, _) => Enforce();
        _monitor = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _monitor.Tick += _onTick;
    }

    public bool IsHidden { get; private set; }

    private IntPtr ExplorerTray => ManagedShell.Common.Helpers.WindowHelper.FindWindowsTray(_ownTrayProvider());

    public void Hide()
    {
        if (IsHidden) return;
        var tray = ExplorerTray;
        if (tray == IntPtr.Zero)
        {
            Log.Info("Explorer görev çubuğu bulunamadı.");
            return;
        }

        _originalState = GetState(tray);
        SessionState.Save(new SessionState { TaskbarHidden = true, OriginalTaskbarState = _originalState });

        SetState(tray, ABS_AUTOHIDE | (_originalState & ABS_ALWAYSONTOP));
        SetVisible(tray, false);
        IsHidden = true;
        _monitor.Start();
        Log.Info($"Windows görev çubuğu gizlendi (orijinal durum {_originalState}).");
    }

    public void Restore()
    {
        if (IsHidden)
        {
            IsHidden = false;
            var tray = ExplorerTray;
            if (tray != IntPtr.Zero)
                SetState(tray, _originalState);
            SetVisible(tray, true);
            SessionState.Clear();
            Log.Info("Windows görev çubuğu geri getirildi.");
        }

        try
        {
            if (_monitor.Dispatcher.CheckAccess()) _monitor.Stop();
            else _monitor.Dispatcher.BeginInvoke(_monitor.Stop);
        }
        catch
        {
            // kapanış
        }
    }

    private void Enforce()
    {
        if (!IsHidden) return;

        var tray = ExplorerTray;
        bool anyVisible = (tray != IntPtr.Zero && IsWindowVisible(tray)) || AnySecondaryTrayVisible();

        if (!anyVisible)
        {
            _visibleTicks = 0;
            return;
        }

        // Başlat / Arama / hızlı ayarlar açılırken Explorer görev çubuğunu gösterir; araya girme.
        if (_launcherVisible() || ShellFlyoutClasses.Contains(GetClassName(GetForegroundWindow())))
        {
            _visibleTicks = 0;
            return;
        }

        // Kısa bir tolerans: menü açılışının ilk anlarında da bekle.
        if (++_visibleTicks < 2) return;

        _visibleTicks = 0;
        if ((GetState(tray) & ABS_AUTOHIDE) == 0)
            SetState(tray, ABS_AUTOHIDE | (_originalState & ABS_ALWAYSONTOP));
        SetVisible(tray, false);
    }

    private static bool AnySecondaryTrayVisible()
    {
        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(secondary)) return true;
        }
        return false;
    }

    private static void SetVisible(IntPtr tray, bool visible)
    {
        uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW);
        if (tray != IntPtr.Zero)
            SetWindowPos(tray, visible ? IntPtr.Zero : HWND_BOTTOM, 0, 0, 0, 0, visible ? flags | SWP_NOZORDER : flags);

        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            SetWindowPos(secondary, visible ? IntPtr.Zero : HWND_BOTTOM, 0, 0, 0, 0, visible ? flags | SWP_NOZORDER : flags);
        }
    }

    // ------------------------------------------------------------------ Çökme kurtarma (ManagedShell başlamadan önce çalışır)

    /// <summary>Önceki oturum görev çubuğu gizliyken kapandıysa geri yükler.</summary>
    public static void RecoverFromPreviousSession()
    {
        var session = SessionState.Load();
        if (!session.TaskbarHidden) return;
        Log.Info("Önceki oturumdan kalan gizli görev çubuğu geri yükleniyor.");
        ForceRestore(session.OriginalTaskbarState);
    }

    /// <summary>Acil durum: görev çubuğunu her koşulda görünür yapar.</summary>
    public static void ForceShow()
    {
        var session = SessionState.Load();
        ForceRestore(session.TaskbarHidden ? session.OriginalTaskbarState : null);
    }

    private static void ForceRestore(int? state)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero && state is int s)
            SetState(tray, s);
        SetVisible(tray, true);
        SessionState.Clear();

        // Uygulamalar tepsi ikonlarını Explorer'a yeniden kaydetsin (normal kapanışta ManagedShell bunu yapar;
        // çökmeden sonra ikonlar kaybolmasın).
        SendNotifyMessage(HWND_BROADCAST, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
    }

    private static int GetState(IntPtr tray)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray };
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    private static void SetState(IntPtr tray, int state)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray, lParam = new IntPtr(state) };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    /// <summary>Explorer görev çubuğuna "masaüstünü göster" komutunu gönderir.</summary>
    public void ToggleDesktop()
    {
        var tray = ExplorerTray;
        if (tray != IntPtr.Zero)
            SendMessage(tray, WM_COMMAND, new IntPtr(407), IntPtr.Zero);
    }

    public void Dispose()
    {
        Restore();
        _monitor.Tick -= _onTick;
    }
}

/// <summary>Çökme güvenliği için oturum durumu (session.json).</summary>
public sealed class SessionState
{
    public bool TaskbarHidden { get; set; }

    public int OriginalTaskbarState { get; set; }

    public static SessionState Load() => JsonStore.Load<SessionState>(AppPaths.SessionFile);

    public static void Save(SessionState state) => JsonStore.Save(AppPaths.SessionFile, state);

    public static void Clear()
    {
        try { File.Delete(AppPaths.SessionFile); } catch { /* yoksay */ }
    }
}
