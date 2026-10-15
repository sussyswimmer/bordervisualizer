using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Watchers;

// Finds fullscreen apps (doc 04 §4): re-evaluated when the foreground window changes and every 2 s, only while
// "Pause in fullscreen apps" is on. Gathers the facts from Windows; FullscreenRules decides. Lives on the system
// watcher's thread, whose message loop delivers the foreground events and the poll timer.
internal sealed class FullscreenDetector : IDisposable
{
    private const uint PollMs = 2000; // doc 04 §4

    // What the foreground window can do in the rules.
    private enum Role
    {
        // Nothing: the desktop, the taskbar, Alt+Tab, Task View, our tray menu, or a window that is gone.
        Ignored,
        // Ends a sticky pause on its monitor but never pauses: File Explorer, the shell's dialogs, our settings window.
        Activates,
        // Any other app's window.
        MayBeFullscreen,
    }

    private readonly HWND window; // the system watcher's window: owns the poll timer
    private readonly nuint timerId;
    private readonly Action changed;
    private readonly WINEVENTPROC foregroundProcedure; // kept alive while hooked
    private readonly uint processId = (uint)Environment.ProcessId;
    private UnhookWinEventSafeHandle? hook;
    private FullscreenEntry[] entries = [];

    // changed: called on this thread when Everywhere or Monitors changed.
    public FullscreenDetector(HWND window, nuint timerId, Action changed)
    {
        this.window = window;
        this.timerId = timerId;
        this.changed = changed;
        foregroundProcedure = OnForegroundChanged;
    }

    public bool Enabled { get; private set; }

    // Pause everywhere: a screen saver, presentation settings, or an exclusive-mode app while there is no foreground window.
    public bool Everywhere { get; private set; }

    // GDI device names of the monitors to pause, sorted; the same instance while the set doesn't change.
    public string[] Monitors { get; private set; } = [];

    public void Enable()
    {
        if (Enabled) return;
        Enabled = true;
        // Out-of-context events arrive through this thread's message loop, so the callback runs here.
        hook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, null,
            foregroundProcedure, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        if (hook.IsInvalid) Trace.WriteLine("[FullscreenDetector] SetWinEventHook failed; fullscreen apps are found by the 2 s poll only.");
        if (PInvoke.SetTimer(window, timerId, PollMs, null) == 0) Trace.WriteLine("[FullscreenDetector] SetTimer failed; no 2 s poll.");
        Evaluate();
    }

    public void Disable()
    {
        if (!Enabled) return;
        Enabled = false;
        Unhook();
        entries = [];
        Update(false, []);
    }

    // Looks at the foreground window and Windows' notification state now. Allocates a little; runs at most on each
    // foreground change and every 2 s.
    public void Evaluate()
    {
        if (!Enabled) return;
        NotificationState state = QueryState();
        HWND active = PInvoke.GetForegroundWindow();
        Role role = active.IsNull ? Role.Ignored : Classify(active);
        WindowFacts? foreground = role == Role.Ignored ? null : Probe(active);

        var previous = new (FullscreenEntry, WindowFacts?)[entries.Length];
        for (int i = 0; i < entries.Length; i++) previous[i] = (entries[i], Probe((HWND)entries[i].Window));

        FullscreenVerdict verdict = FullscreenRules.Decide(state, active.IsNull, foreground, role == Role.MayBeFullscreen, previous);
        entries = verdict.Entries;
        Update(verdict.Everywhere, [.. entries.Select(entry => entry.Monitor).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    public void Dispose()
    {
        Enabled = false;
        Unhook();
    }

    private void Unhook()
    {
        hook?.Dispose();
        hook = null;
        PInvoke.KillTimer(window, timerId);
    }

    private void Update(bool everywhere, string[] monitors)
    {
        bool sameMonitors = monitors.AsSpan().SequenceEqual(Monitors);
        if (everywhere == Everywhere && sameMonitors) return;
        Everywhere = everywhere;
        if (!sameMonitors) Monitors = monitors;
        changed();
    }

    private void OnForegroundChanged(HWINEVENTHOOK hook, uint winEvent, HWND hwnd, int idObject, int idChild, uint idEventThread, uint eventTimeMs)
    {
        try
        {
            Evaluate();
        }
        catch (Exception exception)
        {
            // A callback must never throw into native code.
            Trace.WriteLine($"[FullscreenDetector] Evaluating the foreground window failed: {exception.Message}");
        }
    }

    private static NotificationState QueryState()
    {
        if (PInvoke.SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state).Failed) return NotificationState.Normal;
        return state switch
        {
            QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT => NotificationState.NotPresent,
            QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY => NotificationState.Busy,
            QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN => NotificationState.D3DFullscreen,
            QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE => NotificationState.PresentationMode,
            _ => NotificationState.Normal,
        };
    }

    // The shell and the desktop never count as a fullscreen app (doc 04 §4), and neither do our own windows: the
    // overlays never activate, but are excluded explicitly anyway. Every window of the shell's process is excluded,
    // so Alt+Tab and Task View (full-monitor Explorer windows) don't flash a pause. Windows the user works in still end
    // a sticky pause like any other: in the shell's process File Explorer and Control Panel (CabinetWClass) and dialogs
    // such as Run or Properties (#32770), allow-listed so an unknown shell surface keeps the pause; and our own windows
    // except the tray menu, a WPF popup (tool window, no-activate) that the tray icon makes the foreground window.
    private Role Classify(HWND hwnd)
    {
        HWND shell = PInvoke.GetShellWindow();
        if (hwnd == shell || hwnd == PInvoke.GetDesktopWindow()) return Role.Ignored;
        PInvoke.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner == 0) return Role.Ignored;
        if (owner == processId)
        {
            var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            return (style & (WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE)) != 0 ? Role.Ignored : Role.Activates;
        }
        Span<char> name = stackalloc char[32];
        ReadOnlySpan<char> className = name[..Math.Clamp(PInvoke.GetClassName(hwnd, name), 0, name.Length)];
        if (className is "Progman" or "WorkerW") return Role.Ignored;
        if (!shell.IsNull)
        {
            PInvoke.GetWindowThreadProcessId(shell, out uint shellProcess);
            if (owner == shellProcess) return className is "CabinetWClass" or "#32770" ? Role.Activates : Role.Ignored;
        }
        return Role.MayBeFullscreen;
    }

    // A window's rectangle and monitor, or null when it isn't shown: closed, hidden, minimized, cloaked (another
    // virtual desktop) or on no monitor.
    private static unsafe WindowFacts? Probe(HWND hwnd)
    {
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd) || !PInvoke.IsWindowVisible(hwnd) || PInvoke.IsIconic(hwnd)) return null;
        uint cloaked = 0;
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Succeeded && cloaked != 0) return null;
        if (!PInvoke.GetWindowRect(hwnd, out RECT bounds)) return null;

        HMONITOR monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (monitor.IsNull) return null;
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)) return null;

        ReadOnlySpan<char> device = info.szDevice.AsSpan();
        int end = device.IndexOf('\0');
        string name = new(end < 0 ? device : device[..end]);
        return new WindowFacts((nint)hwnd.Value, ToPixels(bounds), PInvoke.IsZoomed(hwnd), name, ToPixels(info.monitorInfo.rcMonitor));
    }

    private static PixelRect ToPixels(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);
}
