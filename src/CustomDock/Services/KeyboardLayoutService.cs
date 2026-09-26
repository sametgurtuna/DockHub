using System.Globalization;
using System.Text;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using Microsoft.Win32;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Services;

/// <summary>An installed keyboard layout (input language).</summary>
public sealed record KeyboardLayoutInfo(IntPtr Handle, string Abbreviation, string LanguageName, string LayoutName)
{
    public string DisplayName => string.IsNullOrEmpty(LayoutName) || LayoutName == LanguageName ? LanguageName : $"{LanguageName} · {LayoutName}";
}

/// <summary>
/// Input language of the active window and the installed layouts. The Windows taskbar shows this next to the tray;
/// with the taskbar hidden, the dock takes it over. Switching posts WM_INPUTLANGCHANGEREQUEST to the active window.
/// </summary>
public sealed class KeyboardLayoutService
{
    private readonly DispatcherTimer _timer;
    private WinEventProc? _foregroundProc;
    private IntPtr _foregroundHook;
    private int _subscribers;
    private IntPtr _current;

    public KeyboardLayoutService()
    {
        // Win+Space and Alt+Shift raise no window event, so the active layout is checked twice a second while shown.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Poll();
    }

    public IReadOnlyList<KeyboardLayoutInfo> Layouts { get; private set; } = Array.Empty<KeyboardLayoutInfo>();

    public KeyboardLayoutInfo? Current => Layouts.FirstOrDefault(l => l.Handle == _current) ?? (Layouts.Count > 0 ? Describe(_current) : null);

    /// <summary>Raised on the UI thread when the active layout or the installed layouts change.</summary>
    public event Action? Changed;

    public void Subscribe()
    {
        if (_subscribers++ > 0) return;
        ReloadLayouts();
        _current = ActiveLayout();
        _foregroundProc ??= (_, _, _, _, _, _, _) => Poll();
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND_EVENT, EVENT_SYSTEM_FOREGROUND_EVENT, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        _timer.Start();
    }

    public void Unsubscribe()
    {
        if (_subscribers == 0 || --_subscribers > 0) return;
        _timer.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void Poll()
    {
        // Only while a dock is on screen; the value is refreshed as soon as it comes back.
        if (!DockVisibility.IsAnyDockVisible) return;
        var active = ActiveLayout();
        int count = GetKeyboardLayoutList(0, null);
        if (active == _current && count == Layouts.Count) return;
        if (count != Layouts.Count) ReloadLayouts();
        _current = active;
        Changed?.Invoke();
    }

    /// <summary>Layout of the thread that owns keyboard focus in the foreground window.</summary>
    private static IntPtr ActiveLayout()
    {
        var foreground = GetForegroundWindow();
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.hwndFocus != IntPtr.Zero)
            thread = GetWindowThreadProcessId(info.hwndFocus, out _);
        return GetKeyboardLayout(thread);
    }

    private void ReloadLayouts()
    {
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            Layouts = Array.Empty<KeyboardLayoutInfo>();
            return;
        }
        var handles = new IntPtr[count];
        GetKeyboardLayoutList(count, handles);
        var layouts = handles.Select(Describe).ToList();

        // Two layouts of one language (Turkish Q and F) need more than the language to tell them apart:
        // add the last word of the layout name ("TUR Q", "TUR F"), or a number when that doesn't help.
        foreach (var group in layouts.GroupBy(l => l.Abbreviation).Where(g => g.Count() > 1).ToList())
        {
            var suffixes = group.Select(l => LastWord(l.LayoutName)).ToList();
            bool distinct = suffixes.All(s => s.Length > 0) && suffixes.Distinct().Count() == suffixes.Count;
            int index = 0;
            foreach (var layout in group.ToList())
            {
                string suffix = distinct ? suffixes[index] : (index + 1).ToString(CultureInfo.InvariantCulture);
                layouts[layouts.IndexOf(layout)] = layout with { Abbreviation = $"{layout.Abbreviation} {suffix}" };
                index++;
            }
        }
        Layouts = layouts;
    }

    private static string LastWord(string name)
    {
        var word = name.Split(new[] { ' ', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return word.Length <= 3 ? word.ToUpperInvariant() : "";
    }

    private static KeyboardLayoutInfo Describe(IntPtr hkl)
    {
        int languageId = (int)(hkl.ToInt64() & 0xFFFF);
        string abbreviation = "?";
        string language = "";
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            abbreviation = culture.ThreeLetterISOLanguageName.ToUpperInvariant();
            language = culture.DisplayName;
        }
        catch (CultureNotFoundException)
        {
            abbreviation = languageId.ToString("X4");
        }
        return new KeyboardLayoutInfo(hkl, abbreviation, language, LayoutName(hkl));
    }

    /// <summary>Name of the layout (e.g. "Turkish Q") from HKLM\...\Keyboard Layouts.</summary>
    private static string LayoutName(IntPtr hkl)
    {
        try
        {
            long value = hkl.ToInt64();
            int device = (int)((value >> 16) & 0xFFFF);
            int language = (int)(value & 0xFFFF);
            using var layouts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
            if (layouts is null) return "";

            string? klid = null;
            if ((device & 0xF000) == 0xF000)
            {
                // Variant layouts are found by their "Layout Id".
                string layoutId = (device & 0x0FFF).ToString("X4");
                foreach (var name in layouts.GetSubKeyNames())
                {
                    using var key = layouts.OpenSubKey(name);
                    if (string.Equals(key?.GetValue("Layout Id") as string, layoutId, StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(language.ToString("X4"), StringComparison.OrdinalIgnoreCase))
                    {
                        klid = name;
                        break;
                    }
                }
            }
            else if ((device & 0xF000) == 0xE000)
            {
                klid = ((uint)value).ToString("X8");
            }
            else
            {
                klid = device.ToString("X8");
            }
            if (klid is null) return "";

            using var layout = layouts.OpenSubKey(klid);
            if (layout?.GetValue("Layout Display Name") is string indirect && indirect.StartsWith('@'))
            {
                var buffer = new StringBuilder(256);
                if (SHLoadIndirectString(indirect, buffer, buffer.Capacity, IntPtr.Zero) == 0) return buffer.ToString();
            }
            return layout?.GetValue("Layout Text") as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Switches the active window to a layout.</summary>
    public void Activate(IntPtr hkl)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;
        PostMessage(foreground, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        _current = hkl;
        Changed?.Invoke();
        // The window may refuse; the next poll shows the real state.
    }

    public void ActivateNext(bool backwards = false)
    {
        if (Layouts.Count < 2) return;
        int index = Layouts.ToList().FindIndex(l => l.Handle == _current);
        int next = backwards ? (index <= 0 ? Layouts.Count - 1 : index - 1) : (index + 1) % Layouts.Count;
        Activate(Layouts[next].Handle);
    }
}
