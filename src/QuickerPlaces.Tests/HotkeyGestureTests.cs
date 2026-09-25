using QuickerPlaces.Models;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Control | HotkeyModifiers.Alt, "Space")]
    [InlineData("ctrl + alt + space", HotkeyModifiers.Control | HotkeyModifiers.Alt, "space")]
    [InlineData("Control+Shift+Q", HotkeyModifiers.Control | HotkeyModifiers.Shift, "Q")]
    [InlineData("Win+Q", HotkeyModifiers.Windows, "Q")]
    [InlineData("Alt+F5", HotkeyModifiers.Alt, "F5")]
    [InlineData("Space+Alt", HotkeyModifiers.Alt, "Space")]
    public void Valid_hotkeys_parse(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error), error);
        Assert.Null(error);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData("Space")]            // no modifier: would swallow the key everywhere
    [InlineData("Shift+Q")]          // Shift alone: would swallow capital Q
    [InlineData("Ctrl+Alt")]         // no key
    [InlineData("Ctrl+A+B")]         // two keys
    [InlineData("Ctrl+Ctrl+A")]      // repeated modifier
    [InlineData("Ctrl++A")]          // empty part
    [InlineData("Ctrl+A+")]
    public void Invalid_hotkeys_are_rejected_with_a_reason(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("None")]
    [InlineData("none")]
    public void Empty_or_none_means_disabled(string? text)
    {
        Assert.True(HotkeyGesture.IsDisabled(text));
        Assert.False(HotkeyGesture.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("ctrl+alt+space", "Ctrl+Alt+Space")]
    [InlineData("win+shift+ctrl+alt+q", "Ctrl+Alt+Shift+Win+Q")]
    [InlineData("Alt+f5", "Alt+F5")]
    public void Display_form_is_canonical(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out _));
        Assert.Equal(expected, gesture.ToString());
    }

    [Fact]
    public void Default_hotkey_is_valid()
        => Assert.True(HotkeyGesture.TryParse(HotkeyGesture.Default, out _, out _));
}
