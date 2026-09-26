<#
.SYNOPSIS
    Lists the top-level windows of a process with the properties the taskbar uses to decide visibility.
.EXAMPLE
    pwsh -File "C:\path\to\DockHub\plans\tools\window-diag.ps1" -ProcessName Discord
#>
param(
    [Parameter(Mandatory)] [string] $ProcessName,
    [switch] $IncludeHidden
)

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WinDiag
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr hwnd, string name);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    public static List<IntPtr> WindowsOf(HashSet<uint> pids)
    {
        var result = new List<IntPtr>();
        EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (pids.Contains(pid)) result.Add(h); return true; }, IntPtr.Zero);
        return result;
    }

    public static string Text(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
    public static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    public static bool Cloaked(IntPtr h) { int v; return DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out v, 4) == 0 && v != 0; }
}
"@

$procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $procs) { Write-Warning "No running process named '$ProcessName'."; exit 1 }

$pidSet = [System.Collections.Generic.HashSet[uint32]]::new()
foreach ($p in $procs) { [void]$pidSet.Add([uint32]$p.Id) }

$WS_EX_TOOLWINDOW = 0x80; $WS_EX_APPWINDOW = 0x40000; $WS_EX_NOACTIVATE = 0x8000000

$rows = foreach ($h in [WinDiag]::WindowsOf($pidSet)) {
    $visible = [WinDiag]::IsWindowVisible($h)
    if (-not $visible -and -not $IncludeHidden) { continue }
    $ex = [WinDiag]::GetWindowLongPtr($h, -20).ToInt64()
    $owner = [WinDiag]::GetWindow($h, 4) # GW_OWNER
    $deleted = [WinDiag]::GetProp($h, "ITaskList_Deleted") -ne [IntPtr]::Zero
    $windowPid = [uint32]0; [void][WinDiag]::GetWindowThreadProcessId($h, [ref]$windowPid)
    $tool = ($ex -band $WS_EX_TOOLWINDOW) -ne 0
    $app = ($ex -band $WS_EX_APPWINDOW) -ne 0
    $noAct = ($ex -band $WS_EX_NOACTIVATE) -ne 0
    $eligible = $visible -and ($owner -eq [IntPtr]::Zero -or $app) -and (-not $noAct -or $app) -and -not $tool -and -not $deleted -and -not [WinDiag]::Cloaked($h)

    [pscustomobject]@{
        Hwnd            = ('0x{0:X}' -f $h.ToInt64())
        Title           = [WinDiag]::Text($h)
        Class           = [WinDiag]::Class($h)
        Visible         = $visible
        Cloaked         = [WinDiag]::Cloaked($h)
        Owner           = ('0x{0:X}' -f $owner.ToInt64())
        ExStyle         = ('0x{0:X8}' -f $ex)
        ToolWindow      = $tool
        AppWindow       = $app
        NoActivate      = $noAct
        TaskListDeleted = $deleted
        TaskbarEligible = $eligible
        Path            = (Get-Process -Id $windowPid -ErrorAction SilentlyContinue).Path
    }
}

if (-not $rows) { Write-Warning "No windows found (use -IncludeHidden to list hidden ones)."; exit 0 }
$rows | Format-List
Write-Host "TaskbarEligible = ManagedShell's CanAddToTaskbar rule. An eligible window that is missing from the dock points to the ShowInTaskbar cache (D1)."
