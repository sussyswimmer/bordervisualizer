using Rimlight.Platform.Shell;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// What the Behavior page's shortcut recorder takes (doc 06 §4), on top of what <see cref="HotkeyGesture"/> allows as a
/// global shortcut at all. Pure code, so Rimlight.Platform.Tests checks it off Windows.
/// </summary>
internal static class ShortcutChoice
{
    /// <summary>
    /// Ctrl+C, Ctrl+Z, Ctrl+Tab, Ctrl+Shift+T, Alt+F, Ctrl+Left and the like: a global shortcut takes them from every
    /// app, and the recorder makes them easy to press out of habit (K7's review). Keys apps use for typing, editing and
    /// moving need Win, or Ctrl and Alt together. Only the recorder refuses them; a shortcut already in the settings
    /// still works.
    /// </summary>
    /// <param name="gesture">A combination <see cref="HotkeyGesture.TryCreate"/> accepted.</param>
    /// <returns>True when the recorder should refuse it.</returns>
    public static bool TakenFromApps(HotkeyGesture gesture)
    {
        HotkeyModifiers modifiers = gesture.Modifiers;
        if (modifiers.HasFlag(HotkeyModifiers.Win) || modifiers.HasFlag(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) return false;
        return gesture.VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A // digits, letters
            or 0x08 or 0x09 or 0x0D or 0x20                                     // Backspace, Tab, Enter, Space
            or >= 0x21 and <= 0x28 or 0x2D or 0x2E                              // PageUp to the arrows, Insert, Delete
            or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDE or 0xE2;              // punctuation
    }
}
