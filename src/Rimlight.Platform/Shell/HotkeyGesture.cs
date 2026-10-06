using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Rimlight.Platform.Shell;

/// <summary>The modifier keys of a global shortcut.</summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary>Either Ctrl key.</summary>
    Ctrl = 1,
    /// <summary>Either Alt key.</summary>
    Alt = 2,
    /// <summary>Either Shift key.</summary>
    Shift = 4,
    /// <summary>Either Windows key.</summary>
    Win = 8,
}

/// <summary>
/// A global shortcut in the settings format (H-009): <c>Modifier+…+Key</c> with the modifiers <c>Ctrl</c>, <c>Alt</c>,
/// <c>Shift</c> and <c>Win</c> and one key name, e.g. <c>Ctrl+Alt+L</c>. Parsing ignores case and spaces around the
/// <c>+</c>; <see cref="ToString"/> writes the canonical form (modifiers in that order, then the key). Pure code, no
/// Windows calls.
/// </summary>
/// <remarks>
/// A shortcut needs Ctrl, Alt or Win, so it never takes a key away from typing (Shift+A would steal capital A).
/// Function keys may go without, except F12, which Windows reserves for the debugger. Windows' own window and task
/// keys (Alt+F4, Alt+Tab, Alt+Esc, Alt+Space, Ctrl+Esc, Ctrl+Shift+Esc) are refused too: RegisterHotKey would take
/// them away from every app. The settings window's shortcut recorder turns a pressed key into a gesture with
/// <see cref="TryCreate"/> (WPF's <c>KeyInterop.VirtualKeyFromKey</c> gives the virtual-key code).
/// </remarks>
/// <param name="Modifiers">The modifier keys.</param>
/// <param name="VirtualKey">The Windows virtual-key code of the key.</param>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private const int F1 = 0x70;
    private const int F4 = 0x73;
    private const int F12 = 0x7B;
    private const int F24 = 0x87;
    private const int Tab = 0x09;
    private const int Escape = 0x1B;
    private const int Space = 0x20;

    // Key names by virtual-key code. The first name of a code is the canonical one; the others are accepted aliases.
    private static readonly (string Name, int Key)[] Names = BuildNames();

    /// <summary>
    /// Parses a shortcut. False for an empty string ("no hotkey"), an unknown key name, a missing or second key, or
    /// a combination <see cref="IsAllowed"/> rejects.
    /// </summary>
    /// <param name="text">The setting's text, e.g. <c>Ctrl+Alt+L</c>.</param>
    /// <param name="gesture">The shortcut when the text is valid.</param>
    /// <returns>True when <paramref name="text"/> is a valid shortcut.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = HotkeyModifiers.None;
        int key = 0;
        foreach (string part in text.Split('+'))
        {
            string token = part.Trim();
            if (token.Length == 0) return false; // "Ctrl++L", a trailing "+"
            HotkeyModifiers modifier = ParseModifier(token);
            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
                continue;
            }
            if (key != 0 || !TryParseKey(token, out key)) return false; // a second key, or an unknown name
        }
        return TryCreate(modifiers, key, out gesture);
    }

    /// <summary>Makes a shortcut from modifiers and a virtual-key code, if the key has a name and the combination is allowed.</summary>
    /// <param name="modifiers">The modifier keys.</param>
    /// <param name="virtualKey">The Windows virtual-key code.</param>
    /// <param name="gesture">The shortcut when it is valid.</param>
    /// <returns>True when the combination can be a global shortcut.</returns>
    public static bool TryCreate(HotkeyModifiers modifiers, int virtualKey, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win), virtualKey);
        if (KeyName(virtualKey) is not null && IsAllowed(gesture.Modifiers, virtualKey)) return true;
        gesture = default;
        return false;
    }

    /// <summary>
    /// Whether a combination may be a global shortcut: Ctrl, Alt or Win must be part of it, except for the function
    /// keys F1–F24 (not F12 alone), and it must not be one of Windows' window and task keys.
    /// </summary>
    /// <param name="modifiers">The modifier keys.</param>
    /// <param name="virtualKey">The Windows virtual-key code.</param>
    /// <returns>True when allowed.</returns>
    public static bool IsAllowed(HotkeyModifiers modifiers, int virtualKey)
    {
        if (IsSystemKey(modifiers, virtualKey)) return false;
        if ((modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0) return true;
        bool functionKey = virtualKey is >= F1 and <= F24;
        return functionKey && !(virtualKey == F12 && modifiers == HotkeyModifiers.None);
    }

    /// <summary>The canonical name of a key (e.g. <c>L</c>, <c>F5</c>, <c>PageUp</c>), or null if it has none.</summary>
    /// <param name="virtualKey">The Windows virtual-key code.</param>
    /// <returns>The name, or null.</returns>
    public static string? KeyName(int virtualKey)
    {
        foreach ((string name, int key) in Names)
        {
            if (key == virtualKey) return name;
        }
        return null;
    }

    /// <summary>The canonical text, e.g. <c>Ctrl+Alt+L</c>: what the settings store and what menus show.</summary>
    /// <returns>The shortcut's text.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) text.Append("Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append("Shift+");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) text.Append("Win+");
        return text.Append(KeyName(VirtualKey) ?? $"0x{VirtualKey:X2}").ToString();
    }

    // Close window, switch apps (also backwards), cycle windows, window menu, Start, Task Manager.
    private static bool IsSystemKey(HotkeyModifiers modifiers, int virtualKey)
    {
        const HotkeyModifiers Alt = HotkeyModifiers.Alt;
        const HotkeyModifiers AltShift = HotkeyModifiers.Alt | HotkeyModifiers.Shift;
        return (virtualKey, modifiers) switch
        {
            (F4, Alt) or (Tab, Alt) or (Tab, AltShift) or (Escape, Alt) or (Escape, AltShift) or (Space, Alt) => true,
            (Escape, HotkeyModifiers.Ctrl) or (Escape, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift) => true,
            _ => false,
        };
    }

    private static HotkeyModifiers ParseModifier(string token)
    {
        if (Is(token, "Ctrl") || Is(token, "Control")) return HotkeyModifiers.Ctrl;
        if (Is(token, "Alt")) return HotkeyModifiers.Alt;
        if (Is(token, "Shift")) return HotkeyModifiers.Shift;
        if (Is(token, "Win") || Is(token, "Windows")) return HotkeyModifiers.Win;
        return HotkeyModifiers.None;
    }

    private static bool TryParseKey(string token, out int key)
    {
        foreach ((string name, int code) in Names)
        {
            if (Is(token, name))
            {
                key = code;
                return true;
            }
        }
        key = 0;
        return false;
    }

    private static bool Is(string token, string name) => string.Equals(token, name, StringComparison.OrdinalIgnoreCase);

    private static (string, int)[] BuildNames()
    {
        var names = new List<(string, int)>();
        for (char c = 'A'; c <= 'Z'; c++) names.Add((c.ToString(), c)); // VK_A..VK_Z are the ASCII codes
        for (char c = '0'; c <= '9'; c++) names.Add((c.ToString(), c)); // so are VK_0..VK_9
        for (int i = 1; i <= 24; i++) names.Add(($"F{i}", F1 + i - 1));
        names.AddRange(
        [
            ("Space", 0x20), ("Enter", 0x0D), ("Return", 0x0D), ("Tab", 0x09), ("Esc", 0x1B), ("Escape", 0x1B),
            ("Backspace", 0x08), ("Insert", 0x2D), ("Ins", 0x2D), ("Delete", 0x2E), ("Del", 0x2E),
            ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PgUp", 0x21), ("PageDown", 0x22), ("PgDn", 0x22),
            ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
            ("Pause", 0x13), ("PrintScreen", 0x2C), ("PrtSc", 0x2C), ("ScrollLock", 0x91),
            ("NumPad0", 0x60), ("NumPad1", 0x61), ("NumPad2", 0x62), ("NumPad3", 0x63), ("NumPad4", 0x64),
            ("NumPad5", 0x65), ("NumPad6", 0x66), ("NumPad7", 0x67), ("NumPad8", 0x68), ("NumPad9", 0x69),
            ("Multiply", 0x6A), ("Add", 0x6B), ("Subtract", 0x6D), ("Decimal", 0x6E), ("Divide", 0x6F),
            // Punctuation keys by their position on a US layout (VK_OEM_*); other layouts print other characters.
            ("Semicolon", 0xBA), ("Plus", 0xBB), ("Comma", 0xBC), ("Minus", 0xBD), ("Period", 0xBE), ("Slash", 0xBF),
            ("Backtick", 0xC0), ("OpenBracket", 0xDB), ("Backslash", 0xDC), ("CloseBracket", 0xDD), ("Quote", 0xDE),
            ("Oem102", 0xE2),
        ]);
        return [.. names];
    }
}
