using Rimlight.Platform.Shell;
using Xunit;

namespace Rimlight.Platform.Tests;

// HotkeyGesture (K6, H-009): the settings' "Modifier+…+Key" text and what may be a global shortcut.
public sealed class HotkeyGestureTests
{
    private static readonly HotkeyModifiers[] ModifierSets =
    [
        HotkeyModifiers.Ctrl, HotkeyModifiers.Alt, HotkeyModifiers.Win,
        HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift,
        HotkeyModifiers.Alt | HotkeyModifiers.Shift, HotkeyModifiers.Win | HotkeyModifiers.Shift,
        HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win,
    ];

    [Fact]
    public void EveryNamedKeyRoundTripsWithEveryModifierSet()
    {
        int named = 0;
        for (int key = 0; key <= 0xFF; key++)
        {
            if (HotkeyGesture.KeyName(key) is not { } name) continue;
            named++;
            foreach (HotkeyModifiers modifiers in ModifierSets)
            {
                if (!HotkeyGesture.TryCreate(modifiers, key, out HotkeyGesture created))
                {
                    Assert.False(HotkeyGesture.IsAllowed(modifiers, key), $"{modifiers}+{name}");
                    continue;
                }
                string text = created.ToString();
                Assert.EndsWith(name, text);
                Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture parsed), text);
                Assert.Equal(created, parsed);
                Assert.Equal(text, parsed.ToString());
                // Case and spaces around "+" don't matter.
                Assert.True(HotkeyGesture.TryParse(text.ToLowerInvariant().Replace("+", " + ", StringComparison.Ordinal), out parsed));
                Assert.Equal(created, parsed);
            }
        }
        Assert.Equal(105, named); // A–Z, 0–9, F1–F24 and 45 named keys
    }

    [Theory]
    [InlineData("Ctrl+Alt+L", "Ctrl+Alt+L")]
    [InlineData("alt + ctrl + l", "Ctrl+Alt+L")]                 // canonical order
    [InlineData("Control+Alt+Escape", "Ctrl+Alt+Esc")]           // aliases
    [InlineData("Windows+Return", "Win+Enter")]
    [InlineData("Ctrl+PgDn", "Ctrl+PageDown")]
    [InlineData("Ctrl+Del", "Ctrl+Delete")]
    [InlineData("Ctrl+PrtSc", "Ctrl+PrintScreen")]
    [InlineData("Ctrl+Alt+Plus", "Ctrl+Alt+Plus")]
    [InlineData("F9", "F9")]                                     // function keys may go alone
    [InlineData("Shift+F24", "Shift+F24")]
    [InlineData("Ctrl+F12", "Ctrl+F12")]
    public void ShortcutsParseToTheirCanonicalText(string text, string canonical)
    {
        Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture gesture));
        Assert.Equal(canonical, gesture.ToString());
    }

    [Theory]
    [InlineData("")]               // H-009: an empty setting means no shortcut
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("L")]              // a plain key would be taken from typing
    [InlineData("Shift+A")]        // so would a capital letter
    [InlineData("F12")]            // Windows reserves F12 alone for the debugger
    [InlineData("Ctrl+Alt")]       // no key
    [InlineData("Ctrl+L+K")]       // two keys
    [InlineData("Ctrl++L")]
    [InlineData("Ctrl+Alt+")]
    [InlineData("Ctrl+Alt+Hyper")] // unknown key
    [InlineData("Ctrl+Alt+0x41")]
    public void InvalidShortcutsAreRefused(string? text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData("Alt+F4")]           // close the window
    [InlineData("Alt+Tab")]          // switch apps
    [InlineData("Alt+Shift+Tab")]
    [InlineData("Alt+Esc")]          // cycle windows
    [InlineData("Alt+Shift+Esc")]
    [InlineData("Alt+Space")]        // the window menu
    [InlineData("Ctrl+Esc")]         // Start
    [InlineData("Ctrl+Shift+Esc")]   // Task Manager
    public void WindowsOwnKeysAreRefused(string text)
    {
        // RegisterHotKey would take these from every app (K7's recorder refuses them too).
        Assert.False(HotkeyGesture.TryParse(text, out _));
        string[] parts = text.Split('+');
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        foreach (string part in parts[..^1]) modifiers |= Enum.Parse<HotkeyModifiers>(part);
        int key = parts[^1] switch { "F4" => 0x73, "Tab" => 0x09, "Esc" => 0x1B, _ => 0x20 };
        Assert.False(HotkeyGesture.IsAllowed(modifiers, key));
        Assert.False(HotkeyGesture.TryCreate(modifiers, key, out _));
    }

    [Theory]
    [InlineData("Ctrl+Alt+F4")]
    [InlineData("Ctrl+Alt+Tab")]
    [InlineData("Win+Esc")]
    [InlineData("Ctrl+Alt+Esc")]
    [InlineData("Ctrl+Space")]
    public void NeighboursOfWindowsKeysAreAllowed(string text) => Assert.True(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void TryCreateIgnoresUnknownModifierBitsAndKeysWithoutNames()
    {
        Assert.True(HotkeyGesture.TryCreate(HotkeyModifiers.Ctrl | (HotkeyModifiers)64, 'L', out HotkeyGesture gesture));
        Assert.Equal(HotkeyModifiers.Ctrl, gesture.Modifiers);
        Assert.False(HotkeyGesture.TryCreate(HotkeyModifiers.Ctrl, 0xFF, out gesture));
        Assert.Equal(default, gesture);
        Assert.Equal("Ctrl+0xFF", new HotkeyGesture(HotkeyModifiers.Ctrl, 0xFF).ToString());
    }
}
