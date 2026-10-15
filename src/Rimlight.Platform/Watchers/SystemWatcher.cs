using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rimlight.Platform.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Watchers;

/// <summary>
/// Watches what should pause the glow or change how it runs (doc 02 "Lifetime events", doc 04 §4, PRD §5): sleep and
/// wake, battery power, a locked or disconnected session, the display turning off, and fullscreen apps. Runs its own
/// thread with a hidden top-level window, because power and session notifications go to top-level windows only, and
/// so a slow shell query never delays a frame. The thread sleeps in its message loop between notifications; the only
/// polling is the fullscreen check every 2 s while it is on.
/// </summary>
public sealed class SystemWatcher : IDisposable
{
    private const string ClassName = "Rimlight.SystemWatcher";
    private const uint StopMessage = PInvoke.WM_APP + 1;
    private const uint FullscreenSettingMessage = PInvoke.WM_APP + 2;
    private const nuint FullscreenTimer = 1;
    private const nuint SessionRetryTimer = 2;
    private const uint SessionRetryMs = 5000; // the session service can still be starting at logon
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private static readonly WNDPROC Procedure = WindowProcedure;
    private static SystemWatcher? current; // the running watcher; its thread dispatches its window's messages

    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private nint window; // written by the watcher thread, read by others to post to it
    private int detectFullscreen;
    private int stopping;
    private Exception? startupError;
    private SystemState state;

    // Watcher-thread state.
    private PowerWatcher? power;
    private SessionWatcher? session;
    private FullscreenDetector? fullscreen;

    /// <summary>Creates the watcher. Call <see cref="Start"/> to begin watching.</summary>
    /// <param name="detectFullscreen">Whether to look for fullscreen apps ("Pause in fullscreen apps").</param>
    public SystemWatcher(bool detectFullscreen)
    {
        this.detectFullscreen = detectFullscreen ? 1 : 0;
        state = new SystemState { OnBattery = PowerStatus.IsOnBattery() };
        thread = new Thread(Run) { Name = "Rimlight system watcher", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); // a window and a message loop; the shell query is a shell API
    }

    /// <summary>The current state. Safe to read from any thread.</summary>
    public SystemState State => Volatile.Read(ref state);

    /// <summary>Raised on the watcher thread with each new <see cref="State"/>. Handlers must be quick and must not block.</summary>
    public event Action<SystemState>? StateChanged;

    /// <summary>
    /// Raised on the watcher thread after the machine wakes, the session is unlocked or reconnected, or the display
    /// comes back on: the GPU device and the displays may have changed meanwhile (sleep can lose the device, and a
    /// remote session renders on another adapter). Raised after the <see cref="StateChanged"/> it comes with.
    /// </summary>
    public event Action? Resumed;

    /// <summary>
    /// Starts the watcher thread and waits (up to 2 s) until it has registered for notifications and read the state
    /// once, so <see cref="State"/> is current when this returns. Throws if its window can't be created.
    /// </summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref current, this, null) is not null)
            throw new InvalidOperationException("Only one system watcher can run at a time.");
        thread.Start();
        if (!ready.Wait(StartTimeout))
        {
            Trace.WriteLine("[SystemWatcher] Still starting after 2 s; continuing without waiting.");
            return;
        }
        if (startupError is not null)
        {
            thread.Join();
            throw new InvalidOperationException("The system watcher could not start.", startupError);
        }
    }

    /// <summary>
    /// Turns fullscreen detection on or off ("Pause in fullscreen apps"). Off reports no fullscreen app and stops the
    /// 2 s poll. Safe to call from any thread.
    /// </summary>
    /// <param name="enabled">True to look for fullscreen apps.</param>
    public void SetFullscreenDetection(bool enabled)
    {
        if (Interlocked.Exchange(ref detectFullscreen, enabled ? 1 : 0) != (enabled ? 1 : 0)) Post(FullscreenSettingMessage);
    }

    /// <summary>Stops the watcher thread and unregisters every notification.</summary>
    public void Dispose()
    {
        if (thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted))
        {
            ready.Dispose();
            return;
        }
        Interlocked.Exchange(ref stopping, 1);
        Post(StopMessage);
        if (thread.Join(StopTimeout))
            ready.Dispose();
        else
            Trace.WriteLine("[SystemWatcher] The watcher thread did not stop within 2 s.");
    }

    // Before the window exists the thread hasn't started its loop; it reads the flags when it does.
    private void Post(uint message)
    {
        nint handle = Interlocked.CompareExchange(ref window, 0, 0);
        if (handle != 0) PInvoke.PostMessage((HWND)handle, message, default, default);
    }

    private void Run()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SystemWatcher] Starting failed: {exception.Message}");
            startupError = exception;
            Teardown();
            Interlocked.CompareExchange(ref current, null, this);
            ready.Set();
            return;
        }

        ready.Set();
        try
        {
            // A Dispose before the window existed couldn't post the stop message (see Post).
            if (Volatile.Read(ref stopping) == 0) Loop();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SystemWatcher] The watcher thread failed: {exception}");
        }
        finally
        {
            Teardown();
            Interlocked.CompareExchange(ref current, null, this);
        }
    }

    private unsafe void Initialize()
    {
        NativeWindowClass.Register(ClassName, Procedure);
        HWND handle;
        fixed (char* className = ClassName)
        fixed (char* title = "Rimlight system watcher")
        {
            // Top-level (not message-only), so it receives WM_POWERBROADCAST and WM_DISPLAYCHANGE. Never shown.
            handle = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE, className, title,
                WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, default, default, NativeWindowClass.Instance, null);
        }
        if (handle.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx failed for the system watcher window.");
        Interlocked.Exchange(ref window, (nint)handle.Value);

        power = new PowerWatcher();
        power.Register(handle);
        session = new SessionWatcher();
        if (!session.Register(handle)) PInvoke.SetTimer(handle, SessionRetryTimer, SessionRetryMs, null);
        fullscreen = new FullscreenDetector(handle, FullscreenTimer, () => Publish(false));
        ApplyFullscreenSetting(); // reads the flag after the window is published, so no change is missed
        Publish(false);
    }

    private static unsafe void Loop()
    {
        MSG message;
        // GetMessage returns 0 for WM_QUIT and -1 on an error; both end the loop.
        while (PInvoke.GetMessage(&message, default, 0, 0).Value > 0)
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
    }

    private void ApplyFullscreenSetting()
    {
        if (Volatile.Read(ref detectFullscreen) != 0) fullscreen!.Enable();
        else fullscreen!.Disable();
    }

    // After a power or session notification. A wake or an unlock looks at the foreground window again at once: while
    // locked, the lock screen was the fullscreen foreground window.
    private void OnChange(bool resumed)
    {
        if (resumed) fullscreen?.Evaluate();
        Publish(resumed);
    }

    // Publishes a new snapshot when anything changed, then reports a wake.
    private void Publish(bool resumed)
    {
        if (session is not { } sessionState || power is not { } powerState) return; // still starting
        var next = new SystemState
        {
            Locked = sessionState.Locked,
            Disconnected = sessionState.Disconnected,
            Suspended = powerState.Suspended,
            // The console's display state means nothing to a remote session (doc 02 asks for GUID_CONSOLE_DISPLAY_STATE).
            DisplayOff = powerState.ConsoleDisplayOff && sessionState.OnConsole,
            OnBattery = powerState.OnBattery,
            RemoteSession = !sessionState.OnConsole && !sessionState.Disconnected,
            FullscreenEverywhere = fullscreen?.Everywhere ?? false,
            FullscreenMonitors = fullscreen?.Monitors ?? [],
        };
        if (!next.Equals(Volatile.Read(ref state)))
        {
            Volatile.Write(ref state, next);
            Trace.WriteLine($"[SystemWatcher] {next}");
            Raise(StateChanged, next);
        }
        if (resumed) Raise(Resumed);
    }

    private static void Raise(Action<SystemState>? handlers, SystemState snapshot)
    {
        try
        {
            handlers?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SystemWatcher] A state handler failed: {exception}");
        }
    }

    private static void Raise(Action? handlers)
    {
        try
        {
            handlers?.Invoke();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SystemWatcher] A resume handler failed: {exception}");
        }
    }

    private LRESULT? OnMessage(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_POWERBROADCAST:
                OnChange(power?.Handle(wParam, lParam) ?? false);
                return (LRESULT)1; // TRUE: nothing to object to
            case PInvoke.WM_WTSSESSION_CHANGE:
                OnChange(session?.Handle(wParam, lParam) ?? false);
                return default;
            case PInvoke.WM_DISPLAYCHANGE:
                fullscreen?.Evaluate(); // monitor rectangles changed (a game switching display modes)
                return null;
            case PInvoke.WM_TIMER:
                if (wParam.Value == FullscreenTimer)
                {
                    fullscreen?.Evaluate();
                }
                else if (wParam.Value == SessionRetryTimer && session is not null)
                {
                    if (session.Register(hwnd)) PInvoke.KillTimer(hwnd, SessionRetryTimer);
                    Publish(false);
                }
                return default;
            case FullscreenSettingMessage:
                if (fullscreen is not null) ApplyFullscreenSetting();
                return default;
            case StopMessage:
                // WM_APP messages can also come from outside (a top-level window gets every HWND_BROADCAST), so only
                // a Dispose stops the thread.
                if (Volatile.Read(ref stopping) != 0) PInvoke.PostQuitMessage(0);
                return default;
            case PInvoke.WM_CLOSE:
                return default; // closes only with the app
            default:
                return null;
        }
    }

    // On the watcher thread, once the loop has ended (or startup failed): unregisters everything, then the window.
    private void Teardown()
    {
        fullscreen?.Dispose();
        fullscreen = null;
        power?.Dispose();
        power = null;
        session?.Dispose(); // unregistered while the window still exists
        session = null;
        nint handle = Interlocked.Exchange(ref window, 0);
        if (handle != 0) PInvoke.DestroyWindow((HWND)handle);
    }

    private static LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (current is { } watcher && watcher.OnMessage(hwnd, message, wParam, lParam) is { } result) return result;
        }
        catch (Exception exception)
        {
            // An exception must never cross into native code.
            Trace.WriteLine($"[SystemWatcher] Window message 0x{message:X4} failed: {exception.Message}");
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
