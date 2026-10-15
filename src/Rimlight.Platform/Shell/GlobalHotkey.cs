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
