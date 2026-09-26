using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CustomDock.Native;

/// <summary>Keyboard layouts, registry change notifications and the Windows Notification Facility.</summary>
internal static partial class NativeMethods
{
    // --- Keyboard layouts ---
    public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[]? list);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string source, System.Text.StringBuilder output, int size, IntPtr reserved);

    // --- Registry change notifications ---
    public const int REG_NOTIFY_CHANGE_NAME = 0x1;
    public const int REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    [DllImport("advapi32.dll")]
    public static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        int notifyFilter, SafeWaitHandle eventHandle, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    // --- Windows Notification Facility (focus / quiet hours state) ---
    public const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;

    [DllImport("ntdll.dll")]
    public static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope,
        out uint changeStamp, out int buffer, ref uint bufferSize);

    // --- Window cloaking ---
    public const int DWMWA_CLOAKED_ATTRIBUTE = 14;
    public const int DWM_CLOAKED_SHELL = 0x2;
    public const uint EVENT_SYSTEM_FOREGROUND_EVENT = 0x0003;
}
