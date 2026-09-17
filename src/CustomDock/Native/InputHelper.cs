using System.Runtime.InteropServices;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Native;

/// <summary>Kabuk kısayollarını (Win+S, Win+Tab...) tetiklemek için klavye girişi üretir.</summary>
public static class InputHelper
{
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_A = 0x41;
    public const ushort VK_N = 0x4E;
    public const ushort VK_S = 0x53;
    public const ushort VK_X = 0x58;

    /// <summary>Tuşlara sırayla basar ve ters sırayla bırakır (ör. Win+S).</summary>
    public static void SendKeyCombo(params ushort[] keys)
    {
        var inputs = new List<INPUT>(keys.Length * 2);
        foreach (var key in keys)
            inputs.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wVk = key } });
        for (int i = keys.Length - 1; i >= 0; i--)
            inputs.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wVk = keys[i], dwFlags = KEYEVENTF_KEYUP } });
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }
}
