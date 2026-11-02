using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// Hotkey binding parsing (S6): "Modifier+...+Key" strings from the app config
/// become Win32 modifier flags + virtual key codes for RegisterHotKey.
/// </summary>
public class HotkeyBindingTests
{
    [Fact]
    public void Parses_Default_Popup_Binding()
    {
        Assert.True(HotkeyBinding.TryParse("Ctrl+E", out var b));
        Assert.Equal(HotkeyBinding.ModControl, b.Modifiers);
        Assert.Equal(0x45, b.Key); // 'E'
    }

    [Theory]
    [InlineData("ctrl+shift+1", HotkeyBinding.ModControl | HotkeyBinding.ModShift, '1')]
    [InlineData("Alt+F5", HotkeyBinding.ModAlt, 0x74)] // VK_F5
    [InlineData("Win+Ctrl+E", HotkeyBinding.ModWin | HotkeyBinding.ModControl, 0x45)]
    [InlineData("SHIFT+a", HotkeyBinding.ModShift, 0x41)]
    public void Parses_Modifiers_And_Keys_CaseInsensitive(
        string text, int expectedModifiers, int expectedKey)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var b));
        Assert.Equal(expectedModifiers, b.Modifiers);
        Assert.Equal(expectedKey, b.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("E")]          // bare key, no modifier
    [InlineData("Ctrl")]       // modifier only, no key
    [InlineData("Ctrl+")]      // dangling plus
    [InlineData("++E")]        // empty parts
    [InlineData("Ctrl+E+X")]   // two keys
    [InlineData("Foo+E")]      // unknown part
    [InlineData("Ctrl+F25")]   // F-key out of range
    public void Rejects_Invalid_Bindings(string text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out _));
    }
}
