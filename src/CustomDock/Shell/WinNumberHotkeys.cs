using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

public enum AppShortcutMode { Activate, NewInstance, RunAsAdmin, JumpList }

/// <summary>
/// Win+1..9 / Win+0 for dock apps, like the Windows taskbar. Explorer keeps those shortcuts even while its taskbar
/// is hidden, so RegisterHotKey can't take them; a low-level keyboard hook intercepts them instead
/// (only in replace mode). Holding Win shows the numbers on the dock.
/// </summary>
public sealed class WinNumberHotkeys : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const uint LLKHF_INJECTED = 0x10;
    /// <summary>Unassigned virtual key sent before Win is released so the Start menu doesn't open.</summary>
    private const ushort VK_DUMMY = 0xE8;
    private static readonly TimeSpan NumberHintDelay = TimeSpan.FromMilliseconds(800);

    private readonly Action<int, AppShortcutMode> _onShortcut;
    private readonly Action<bool> _showNumbers;
    private readonly Dispatcher _dispatcher;
    private readonly LowLevelKeyboardProc _proc;
    private readonly DispatcherTimer _hintTimer;
    private IntPtr _hook;
    private bool _winDown;
    private bool _consumed;
    private bool _numbersShown;

    public WinNumberHotkeys(Action<int, AppShortcutMode> onShortcut, Action<bool> showNumbers)
    {
        _onShortcut = onShortcut;
        _showNumbers = showNumbers;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookProc; // kept alive while hooked
        _hintTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = NumberHintDelay };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            if (_winDown && !_numbersShown) SetNumbers(true);
        };
    }

    public bool IsEnabled => _hook != IntPtr.Zero;

    public void Enable()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero) Log.Warn($"Keyboard hook for Win+number failed ({Marshal.GetLastWin32Error()}).");
        else Log.Info("Win+number shortcuts enabled.");
    }

    public void Disable()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _winDown = false;
        SetNumbers(false);
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int message = wParam.ToInt32();
            bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = message is WM_KEYUP or WM_SYSKEYUP;
            int vk = (int)data.vkCode;

            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                if (vk is VK_LWIN or VK_RWIN)
                {
                    if (down && !_winDown)
                    {
                        _winDown = true;
                        _consumed = false;
                        _dispatcher.BeginInvoke(() => _hintTimer.Start());
                    }
                    else if (up)
                    {
                        _winDown = false;
                        _dispatcher.BeginInvoke(() => { _hintTimer.Stop(); SetNumbers(false); });
                        // Win+number was used: tap a dummy key so releasing Win doesn't open Start.
                        if (_consumed) SendDummyKey();
                        _consumed = false;
                    }
                }
                else if (_winDown && down && vk is >= 0x30 and <= 0x39)
                {
                    int index = vk == 0x30 ? 9 : vk - 0x31;
                    bool shift = IsPressed(VK_SHIFT), ctrl = IsPressed(VK_CONTROL), alt = IsPressed(VK_MENU);
                    var mode = alt ? AppShortcutMode.JumpList
                        : ctrl && shift ? AppShortcutMode.RunAsAdmin
                        : shift ? AppShortcutMode.NewInstance
                        : ctrl ? AppShortcutMode.Activate
                        : AppShortcutMode.Activate;
                    _consumed = true;
                    _dispatcher.BeginInvoke(() => { _hintTimer.Stop(); SetNumbers(false); _onShortcut(index, mode); });
                    return new IntPtr(1);
                }
                else if (_winDown && down)
                {
                    // Any other Win combination (Win+E, Win+Tab...) hides the numbers.
                    _dispatcher.BeginInvoke(() => { _hintTimer.Stop(); SetNumbers(false); });
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void SetNumbers(bool show)
    {
        if (_numbersShown == show) return;
        _numbersShown = show;
        _showNumbers(show);
    }

    private static bool IsPressed(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void SendDummyKey()
    {
        var inputs = new[]
        {
            new INPUT { type = 1, ki = new KEYBDINPUT { wVk = VK_DUMMY } },
            new INPUT { type = 1, ki = new KEYBDINPUT { wVk = VK_DUMMY, dwFlags = KEYEVENTF_KEYUP } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => Disable();

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
