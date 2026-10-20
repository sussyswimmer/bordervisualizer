using System.Runtime.InteropServices;
using Rimlight.Platform.Shell;

namespace Rimlight.Native;

// The global shortcut's text (rimlight_core.h, "Shell"): the Windows HotkeyGesture parser and formatter, so both apps
// read and write the ToggleHotkey setting the same way (H-009). Modifiers are its flags: 1 Ctrl, 2 Alt (Option on the
// Mac), 4 Shift, 8 Win (Command on the Mac); keys are Windows virtual-key codes, which the Mac maps to its own.
internal static unsafe partial class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "rl_hotkey_parse")]
    public static int HotkeyParse(byte* text, int* modifiers, int* virtualKey)
    {
        try
        {
            if (modifiers == null || virtualKey == null) return NativeStatus.InvalidArgument;
            if (!HotkeyGesture.TryParse(Utf8Text.Read(text), out HotkeyGesture gesture)) return 0;
            *modifiers = (int)gesture.Modifiers;
            *virtualKey = gesture.VirtualKey;
            return 1;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_hotkey_format")]
    public static int HotkeyFormat(int modifiers, int virtualKey, byte* buffer, int capacity)
    {
        try
        {
            if (!HotkeyGesture.TryCreate((HotkeyModifiers)modifiers, virtualKey, out HotkeyGesture gesture))
                return NativeStatus.InvalidArgument;
            return Utf8Text.Write(gesture.ToString(), buffer, capacity);
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }
}
