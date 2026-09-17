// Bu dosyadaki IImmersiveLauncher/IImmersiveMonitor tanımları ve Başlat menüsünü açma yöntemi
// RetroBar'dan (https://github.com/dremin/RetroBar, Apache-2.0) uyarlanmıştır.
using System.Runtime.InteropServices;
using CustomDock.Core;
using CustomDock.Native;
using ManagedShell.UWPInterop;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

/// <summary>Windows Başlat menüsünü, belirli bir monitörde, klavye simülasyonu olmadan açar.</summary>
public static class StartMenuLauncher
{
    private static readonly Guid CLSID_ImmersiveMonitorManager = new("47094e3a-0cf2-430f-806f-cf9e4f0f12dd");
    private static readonly Guid IID_ImmersiveMonitorManager = new("4d4c1e64-e410-4faa-bafa-59ca069bfec2");
    private static readonly Guid CLSID_ImmersiveLauncher = new("6f86e01c-c649-4d61-be23-f1322ddeca9d");
    private static readonly Guid IID_ImmersiveLauncher = new("d8d60399-a0f1-f987-5551-321fd1b49864");

    public static void Show(IntPtr anchorWindow)
    {
        try
        {
            // Explorer'ın odağı almasına izin ver
            GetWindowThreadProcessId(FindWindow("Progman", "Program Manager"), out uint explorerPid);
            AllowSetForegroundWindow((int)explorerPid);

            var launcher = GetLauncher(anchorWindow);
            if (launcher is not null)
            {
                bool visible = launcher.IsVisible(out bool isVisible) == 0 && isVisible;
                int hr = visible
                    ? launcher.Dismiss(ImmersiveLauncherDismissMethod.Generic)
                    : launcher.ShowStartView(ImmersiveLauncherShowMethod.StartButton, ImmersiveLauncherShowFlags.IgnoreSetForegroundError);
                if (hr == 0) return;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Başlat menüsü IImmersiveLauncher ile açılamadı");
        }

        // Yedek: Windows tuşu
        InputHelper.SendKeyCombo(InputHelper.VK_LWIN);
    }

    private static IImmersiveLauncher? GetLauncher(IntPtr anchorWindow)
    {
        var shell = ImmersiveShellHelper.GetImmersiveShell();
        if (shell is null) return null;

        var launcherClsid = CLSID_ImmersiveLauncher;
        var launcherIid = IID_ImmersiveLauncher;
        if (shell.QueryService(ref launcherClsid, ref launcherIid, out object launcherObj) != 0) return null;
        var launcher = (IImmersiveLauncher)launcherObj;

        var managerClsid = CLSID_ImmersiveMonitorManager;
        var managerIid = IID_ImmersiveMonitorManager;
        if (shell.QueryService(ref managerClsid, ref managerIid, out object managerObj) == 0)
        {
            var manager = (IImmersiveMonitorManager)managerObj;
            var hMonitor = MonitorFromWindow(anchorWindow, MONITOR_DEFAULTTONEAREST);
            if (manager.GetFromHandle(hMonitor, out var monitor) == 0 && monitor is not null)
                launcher.ConnectToMonitor(monitor);
        }

        return launcher;
    }

    private enum ImmersiveLauncherShowMethod
    {
        StartButton = 0xB,
    }

    [Flags]
    private enum ImmersiveLauncherShowFlags
    {
        None = 0,
        IgnoreSetForegroundError = 0x4,
    }

    private enum ImmersiveLauncherDismissMethod
    {
        Generic = 0x7,
    }

    [ComImport, Guid("880b26f8-9197-43d0-8045-8702d0d72000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImmersiveMonitor
    {
    }

    [ComImport, Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImmersiveMonitorManager
    {
        int GetCount(out uint count);
        int GetConnectedCount(out uint count);
        int GetAt(uint index, out IImmersiveMonitor monitor);
        int GetFromHandle(IntPtr hMonitor, out IImmersiveMonitor monitor);
    }

    [ComImport, Guid("d8d60399-a0f1-f987-5551-321fd1b49864"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImmersiveLauncher
    {
        int ShowStartView(ImmersiveLauncherShowMethod showMethod, ImmersiveLauncherShowFlags showFlags);
        int Dismiss(ImmersiveLauncherDismissMethod dismissMethod);
        int Dismiss2(ImmersiveLauncherDismissMethod dismissMethod);
        int DismissSynchronouslyWithoutTransition();
        int IsVisible(out bool visible);
        int OnStartButtonPressed(ImmersiveLauncherShowMethod showMethod, ImmersiveLauncherDismissMethod dismissMethod);
        int SetForeground();
        int ConnectToMonitor(IImmersiveMonitor monitor);
    }
}
