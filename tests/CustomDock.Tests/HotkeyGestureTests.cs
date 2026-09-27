using CustomDock.Core;

namespace CustomDock.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotkeyGesture.MOD_CONTROL | HotkeyGesture.MOD_ALT, 0x44)]
    [InlineData("win + shift + 5", HotkeyGesture.MOD_WIN | HotkeyGesture.MOD_SHIFT, 0x35)]
    [InlineData("Control+F12", HotkeyGesture.MOD_CONTROL, 0x7B)]
    [InlineData("Windows+Alt+Space", HotkeyGesture.MOD_WIN | HotkeyGesture.MOD_ALT, 0x20)]
    public void Shortcuts_are_parsed(string text, uint modifiers, uint virtualKey)
    {
        Assert.Equal(new HotkeyGesture(modifiers, virtualKey), HotkeyGesture.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("D")]           // no modifier
    [InlineData("Ctrl+D+E")]    // two keys
    [InlineData("Ctrl+Banana")] // unknown key
    [InlineData("Ctrl+Alt")]    // no key
    public void Invalid_shortcuts_are_rejected(string? text)
    {
        Assert.Null(HotkeyGesture.Parse(text));
    }

    [Theory]
    [InlineData("Ctrl+Alt+D")]
    [InlineData("Win+Shift+F5")]
    public void Text_round_trips(string text)
    {
        var gesture = HotkeyGesture.Parse(text)!;
        Assert.Equal(gesture, HotkeyGesture.Parse(gesture.ToString()));
    }
}
