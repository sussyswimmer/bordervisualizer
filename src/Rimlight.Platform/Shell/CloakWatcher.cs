using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Shell;

/// <summary>
/// Says whether one of the app's windows is cloaked: Windows still counts it as visible, but nothing draws it, mostly
/// because it is on another virtual desktop. WPF's <c>IsVisible</c> stays true then and no window message arrives, so
/// this listens for Windows' cloak events. Create it on the window's thread, whose message loop delivers them; the
/// callback then runs on that thread. Keep it while the window is shown and dispose it on that thread when the window
/// hides or closes: the hook hears every window's cloak changes, so a hidden window shouldn't keep one.
/// </summary>
public sealed class CloakWatcher : IDisposable
{
    private readonly HWND window;
    private readonly Action changed;
    private readonly WINEVENTPROC procedure; // kept alive while hooked
    private UnhookWinEventSafeHandle? hook;

    /// <summary>Starts watching a window.</summary>
    /// <param name="window">The window's handle.</param>
    /// <param name="changed">Called when the window is cloaked or uncloaked; <see cref="IsCloaked"/> says which.</param>
    public CloakWatcher(nint window, Action changed)
    {
        this.window = (HWND)window;
        this.changed = changed;
        procedure = OnCloakEvent;
        // Every process's events, since the shell cloaks other apps' windows for a desktop switch; filtered to this
        // window below. They are rare: a desktop switch, or a shell flyout opening or closing.
        hook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_CLOAKED, PInvoke.EVENT_OBJECT_UNCLOAKED, null, procedure, 0, 0,
            PInvoke.WINEVENT_OUTOFCONTEXT);
        if (hook.IsInvalid)
        {
            Trace.WriteLine("[CloakWatcher] SetWinEventHook failed; a window on another virtual desktop counts as shown.");
            hook.Dispose();
            hook = null;
        }
    }

    /// <summary>
    /// True while the window is cloaked. False when it isn't or Windows won't say, and always when the events can't be
    /// watched, since then nothing would tell when it is shown again.
    /// </summary>
    public bool IsCloaked => hook is not null && IsWindowCloaked(window);

    /// <inheritdoc />
    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
    }

    private static unsafe bool IsWindowCloaked(HWND window)
    {
        uint cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Succeeded
            && cloaked != 0;
    }

    private void OnCloakEvent(HWINEVENTHOOK eventHook, uint winEvent, HWND hwnd, int idObject, int idChild, uint idEventThread, uint eventTimeMs)
    {
        if (hook is null || hwnd != window || idObject != (int)OBJECT_IDENTIFIER.OBJID_WINDOW || idChild != PInvoke.CHILDID_SELF) return;
        try
        {
            changed();
        }
        catch (Exception exception)
        {
            // A callback must never throw into native code.
            Trace.WriteLine($"[CloakWatcher] Handling a cloak change failed: {exception.Message}");
        }
    }
}
