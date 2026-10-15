using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rimlight.Platform.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Shell;

/// <summary>What became of the shortcut in the settings.</summary>
public enum HotkeyState
{
    /// <summary>No shortcut is set (an empty setting).</summary>
    None,
    /// <summary>The shortcut works.</summary>
    Registered,
    /// <summary>The setting isn't a valid shortcut (<see cref="HotkeyGesture.TryParse"/>).</summary>
    Invalid,
    /// <summary>Another app (or Windows) already uses the combination.</summary>
    InUse,
    /// <summary>Windows refused it for another reason.</summary>
    Failed,
    /// <summary>
    /// Not registered because a keyboard layout types a character with it (<see cref="GlobalHotkey.TypesCharacter"/>):
    /// it would take that character away from typing.
    /// </summary>
    TypesCharacter,
}

/// <summary>The outcome of registering the shortcut in the settings, for the tray menu and Settings' inline warning.</summary>
/// <param name="Text">The setting as given.</param>
/// <param name="State">Whether it works, and if not, why.</param>
/// <param name="Gesture">The parsed shortcut, unless <see cref="State"/> is None or Invalid.</param>
public sealed record HotkeyStatus(string Text, HotkeyState State, HotkeyGesture? Gesture)
{
    /// <summary>No shortcut.</summary>
    public static HotkeyStatus None { get; } = new("", HotkeyState.None, null);
}

/// <summary>
/// The system-wide shortcut that turns the glow on and off (doc 06 §4): <c>RegisterHotKey</c> on a message-only
/// window. Create, use and dispose it on one thread that pumps messages (the UI thread); <see cref="Pressed"/> is
/// raised there. One instance per process.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const string ClassName = "Rimlight.Hotkey";
    private const int HotkeyId = 1; // applications use 0x0000-0xBFFF
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const uint KeepKeyboardState = 0x4; // ToUnicodeEx: don't touch the thread's dead-key state (Windows 10 1607+)
    private const byte KeyDown = 0x80;

    private static readonly WNDPROC Procedure = WindowProcedure;
    private static GlobalHotkey? current; // the instance whose window the procedure serves

    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private HWND window;
    private bool registered;

    /// <summary>Creates the message-only window on the calling thread. Throws if Windows refuses.</summary>
    public unsafe GlobalHotkey()
    {
        if (Interlocked.CompareExchange(ref current, this, null) is not null)
            throw new InvalidOperationException("Only one global hotkey can exist at a time.");
        try
        {
            NativeWindowClass.Register(ClassName, Procedure);
            fixed (char* className = ClassName)
            fixed (char* title = "Rimlight hotkey")
            {
                window = PInvoke.CreateWindowEx(0, className, title, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, default,
                    NativeWindowClass.Instance, null);
            }
            if (window.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx failed for the hotkey window.");
        }
        catch
        {
            Interlocked.CompareExchange(ref current, null, this);
            throw;
        }
    }

    /// <summary>Raised on the owning thread each time the shortcut is pressed (held keys don't repeat it).</summary>
    public event Action? Pressed;

    /// <summary>The outcome of the last <see cref="Register"/>.</summary>
    public HotkeyStatus Status { get; private set; } = HotkeyStatus.None;

    /// <summary>
    /// Replaces the shortcut with <paramref name="text"/> (the <c>ToggleHotkey</c> setting; empty for none) and reports
    /// whether it works. The previous shortcut is released first, even if the new one fails. Call on the owning thread.
    /// </summary>
    /// <param name="text">The shortcut, e.g. <c>Ctrl+Alt+L</c>.</param>
    /// <returns>The new <see cref="Status"/>.</returns>
    public HotkeyStatus Register(string? text)
    {
        CheckThread();
        text ??= "";
        if (registered && text == Status.Text) return Status; // unchanged

        Unregister();
        if (string.IsNullOrWhiteSpace(text)) return Status = HotkeyStatus.None with { Text = text };
        if (!HotkeyGesture.TryParse(text, out HotkeyGesture gesture))
        {
            Trace.WriteLine($"[Hotkey] \"{text}\" is not a valid shortcut.");
            return Status = new HotkeyStatus(text, HotkeyState.Invalid, null);
        }
        if (TypedCharacter(gesture) is { } typed)
        {
            Trace.WriteLine($"[Hotkey] {gesture} is not registered: keyboard layout {typed.Layout} types {typed.Text} with it (AltGr).");
            return Status = new HotkeyStatus(text, HotkeyState.TypesCharacter, gesture);
        }

        // MOD_NOREPEAT: holding the keys toggles the glow once, not on every key repeat.
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win)) modifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
        if (PInvoke.RegisterHotKey(window, HotkeyId, modifiers, (uint)gesture.VirtualKey))
        {
            registered = true;
            Trace.WriteLine($"[Hotkey] {gesture} registered.");
            return Status = new HotkeyStatus(text, HotkeyState.Registered, gesture);
        }

        int error = Marshal.GetLastPInvokeError();
        HotkeyState state = error == ErrorHotkeyAlreadyRegistered ? HotkeyState.InUse : HotkeyState.Failed;
        Trace.WriteLine($"[Hotkey] {gesture} could not be registered: {(state == HotkeyState.InUse ? "another app uses it" : $"error {error}")}.");
        return Status = new HotkeyStatus(text, state, gesture);
    }

    /// <summary>
    /// Whether one of the user's keyboard layouts types a character with <paramref name="gesture"/>. Windows treats
    /// Ctrl+Alt as AltGr, so a Ctrl+Alt shortcut also fires on AltGr and takes its character away from every app
    /// (AltGr+L types ł on Polish keyboards). <see cref="Register"/> refuses such a shortcut; the settings window's
    /// recorder can check a combination before storing it. Layouts added later are checked at the next registration.
    /// </summary>
    /// <param name="gesture">The shortcut.</param>
    /// <returns>True when a layout types a character (or starts an accent) with it.</returns>
    public static bool TypesCharacter(HotkeyGesture gesture) => TypedCharacter(gesture) is not null;

    /// <summary>Releases the shortcut and destroys the window. Call on the owning thread.</summary>
    public void Dispose()
    {
        if (window.IsNull) return;
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            // A window can only be destroyed by its own thread; Windows frees both when the process ends.
            Trace.WriteLine("[Hotkey] Disposed on the wrong thread; the shortcut stays until the app exits.");
            return;
        }
        Unregister();
        PInvoke.DestroyWindow(window);
        window = default;
        Pressed = null;
        Interlocked.CompareExchange(ref current, null, this);
    }

    private void Unregister()
    {
        if (!registered) return;
        registered = false;
        PInvoke.UnregisterHotKey(window, HotkeyId);
    }

    // The first installed layout that types something with the gesture, and what it types. Only Ctrl+Alt without Win
    // can be AltGr; Ctrl+Alt+Shift is AltGr+Shift.
    private static unsafe (string Text, string Layout)? TypedCharacter(HotkeyGesture gesture)
    {
        HotkeyModifiers modifiers = gesture.Modifiers;
        if (!modifiers.HasFlag(HotkeyModifiers.Ctrl) || !modifiers.HasFlag(HotkeyModifiers.Alt) || modifiers.HasFlag(HotkeyModifiers.Win))
            return null;
        int count = PInvoke.GetKeyboardLayoutList(0, null);
        if (count <= 0) return null; // unknown: register, as before the check
        var layouts = new HKL[count];
        fixed (HKL* list = layouts) count = PInvoke.GetKeyboardLayoutList(count, list);

        // AltGr as Windows sends it (left Ctrl + right Alt); stackalloc starts zeroed, so every other key is up.
        byte* state = stackalloc byte[256];
        state[(int)VIRTUAL_KEY.VK_CONTROL] = state[(int)VIRTUAL_KEY.VK_LCONTROL] = KeyDown;
        state[(int)VIRTUAL_KEY.VK_MENU] = state[(int)VIRTUAL_KEY.VK_RMENU] = KeyDown;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) state[(int)VIRTUAL_KEY.VK_SHIFT] = state[(int)VIRTUAL_KEY.VK_LSHIFT] = KeyDown;
        const int BufferLength = 8;
        char* buffer = stackalloc char[BufferLength];
        uint key = (uint)gesture.VirtualKey;
        for (int i = 0; i < count; i++)
        {
            HKL layout = layouts[i];
            uint scanCode = PInvoke.MapVirtualKeyEx(key, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC, layout);
            int length = PInvoke.ToUnicodeEx(key, scanCode, state, buffer, BufferLength, KeepKeyboardState, layout);
            // -1 is a dead key (an accent typed with AltGr); control characters aren't typing.
            if (length < 0) return ("an accent (dead key)", layout.ToString());
            if (length > 0 && !char.IsControl(buffer[0])) return ($"\"{new string(buffer, 0, Math.Min(length, BufferLength))}\"", layout.ToString());
        }
        return null;
    }

    private void CheckThread()
    {
        ObjectDisposedException.ThrowIf(window.IsNull, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("RegisterHotKey binds a shortcut to the window's own thread.");
    }

    private static LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (message == PInvoke.WM_HOTKEY && wParam.Value == HotkeyId && current is { } hotkey && hotkey.window == hwnd)
            {
                hotkey.Pressed?.Invoke();
                return default;
            }
        }
        catch (Exception exception)
        {
            // An exception must never cross into native code.
            Trace.WriteLine($"[Hotkey] Handling the shortcut failed: {exception}");
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
