using Rimlight.App.SettingsUi;
using Rimlight.Platform.Shell;
using Xunit;

namespace Rimlight.Platform.Tests;

// Which shortcuts may toggle the glow beyond HotkeyGesture's rules: the recorder's refusal of keys apps use (K7's review)
// and the AltGr check (K6's review, HANDOFF H-024).
public sealed class ShortcutChoiceTests
{
    [Theory]
    [InlineData("Ctrl+C", true)]             // copy
    [InlineData("Ctrl+Z", true)]
    [InlineData("Ctrl+Shift+T", true)]       // a browser's "reopen tab"
    [InlineData("Ctrl+Tab", true)]
    [InlineData("Alt+F", true)]              // a menu's access key
    [InlineData("Ctrl+Left", true)]          // moving by words
    [InlineData("Ctrl+Shift+End", true)]
    [InlineData("Ctrl+Backspace", true)]
    [InlineData("Ctrl+Enter", true)]
    [InlineData("Alt+Shift+1", true)]
    [InlineData("Ctrl+Comma", true)]
    [InlineData("Ctrl+Oem102", true)]
    [InlineData("Ctrl+Alt+L", false)]        // the default: Ctrl and Alt together
    [InlineData("Ctrl+Alt+Shift+Left", false)]
    [InlineData("Win+Shift+L", false)]       // Win
    [InlineData("Win+Alt+C", false)]
    [InlineData("Ctrl+F5", false)]           // function keys aren't typing
    [InlineData("Alt+F1", false)]
    [InlineData("F8", false)]
    [InlineData("Ctrl+Escape", false)]       // not allowed at all: HotkeyGesture refuses it first
    [InlineData("Ctrl+NumPad5", false)]
    [InlineData("Ctrl+PrintScreen", false)]
    public void TheRecorderRefusesKeysAppsUseForTypingAndEditing(string text, bool refused)
    {
        if (!HotkeyGesture.TryParse(text, out HotkeyGesture gesture))
        {
            Assert.False(refused, $"{text} isn't a shortcut at all");
            return;
        }
        Assert.Equal(refused, ShortcutChoice.TakenFromApps(gesture));
    }

    [Fact]
    public void EveryLetterAndDigitNeedsWinOrCtrlAndAlt()
    {
        const HotkeyModifiers Ctrl = HotkeyModifiers.Ctrl, Alt = HotkeyModifiers.Alt;
        const HotkeyModifiers Shift = HotkeyModifiers.Shift, Win = HotkeyModifiers.Win;
        HotkeyModifiers[] refused = [Ctrl, Alt, Ctrl | Shift, Alt | Shift];
        HotkeyModifiers[] taken = [Ctrl | Alt, Ctrl | Alt | Shift, Win, Win | Shift, Ctrl | Win];
        for (int key = 0x30; key <= 0x5A; key++)
        {
            if (key is > 0x39 and < 0x41) continue;
            foreach (HotkeyModifiers modifiers in refused)
            {
                Assert.True(HotkeyGesture.TryCreate(modifiers, key, out HotkeyGesture gesture));
                Assert.True(ShortcutChoice.TakenFromApps(gesture), gesture.ToString());
            }
            foreach (HotkeyModifiers modifiers in taken)
            {
                Assert.True(HotkeyGesture.TryCreate(modifiers, key, out HotkeyGesture gesture));
                Assert.False(ShortcutChoice.TakenFromApps(gesture), gesture.ToString());
            }
        }
    }

    [Theory]
    [InlineData("Win+Alt+L")]
    [InlineData("Ctrl+Alt+Win+L")]   // Win in it: never AltGr
    [InlineData("Ctrl+Shift+L")]
    [InlineData("Alt+Shift+L")]
    [InlineData("Alt+F1")]
    [InlineData("Ctrl+F5")]
    [InlineData("F8")]
    public void OnlyCtrlAltWithoutWinCanTypeAnAltGrCharacter(string text)
    {
        // These never reach the keyboard layouts (GetKeyboardLayoutList, ToUnicodeEx), so this runs off Windows too.
        Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture gesture));
        Assert.False(GlobalHotkey.TypesCharacter(gesture));
    }

    [Fact]
    public void TheAltGrCheckAsksTheInstalledLayoutsOnWindows()
    {
        // What the answer is depends on the machine's layouts (US and UK type nothing with Ctrl+Alt); this checks that
        // CsWin32's signatures work and that asking leaves the shortcut parseable. Off Windows there are no layouts.
        if (!OperatingSystem.IsWindows()) return;
        string[] combinations = ["Ctrl+Alt+L", "Ctrl+Alt+Shift+L", "Ctrl+Alt+E", "Ctrl+Alt+Q", "Ctrl+Alt+7", "Ctrl+Alt+Quote"];
        foreach (string text in combinations)
        {
            Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture gesture));
            bool typed = GlobalHotkey.TypesCharacter(gesture);
            Assert.Equal(typed, GlobalHotkey.TypesCharacter(gesture)); // the thread's dead-key state isn't touched
        }
    }
}
