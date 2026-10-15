using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RemoteDesktop;

namespace Rimlight.Platform.Watchers;

// This session's lock state and connection (doc 02 "Lifetime events"): WTSRegisterSessionNotification sends
// WM_WTSSESSION_CHANGE to the system watcher's window. Remote Desktop: the glow keeps working in a connected remote
// session, and pauses while the session is disconnected (it has no display then), as it does for a fast user switch.
internal sealed class SessionWatcher : IDisposable
{
    private readonly uint sessionId;
    private HWND registered;

    public SessionWatcher()
    {
        using Process process = Process.GetCurrentProcess();
        sessionId = (uint)process.SessionId;
        OnConsole = PInvoke.WTSGetActiveConsoleSessionId() == sessionId;
    }

    public bool Locked { get; private set; }

    // No terminal shows the session: disconnected from the console (fast user switching) or from Remote Desktop.
    public bool Disconnected { get; private set; }

    // The session is the one on the physical console (not a remote one), so the console display state applies to it.
    public bool OnConsole { get; private set; }

    // Registers for this session's notifications, then reads the current state; registering first means no change
    // between the two is missed. False when the session service isn't ready yet (RPC_S_INVALID_BINDING early at
    // logon): call again later.
    public bool Register(HWND window)
    {
        if (!registered.IsNull) return true;
        if (PInvoke.WTSRegisterSessionNotification(window, PInvoke.NOTIFY_FOR_THIS_SESSION))
            registered = window;
        else
            Trace.WriteLine($"[SessionWatcher] WTSRegisterSessionNotification failed ({Marshal.GetLastPInvokeError()}); retrying.");
        ReadState();
        return !registered.IsNull;
    }

    // Handles WM_WTSSESSION_CHANGE. Returns true when the session became usable again (unlocked or reconnected): the
    // GPU device and the displays may have changed meanwhile (a remote session renders on another adapter).
    public bool Handle(WPARAM wParam, LPARAM lParam)
    {
        if ((uint)lParam.Value != sessionId) return false; // NOTIFY_FOR_THIS_SESSION sends only ours; be sure
        bool usable = false;
        switch ((uint)wParam.Value)
        {
            case PInvoke.WTS_SESSION_LOCK:
                Locked = true;
                break;
            case PInvoke.WTS_SESSION_UNLOCK:
                Locked = false;
                usable = true;
                break;
            case PInvoke.WTS_CONSOLE_CONNECT:
            case PInvoke.WTS_REMOTE_CONNECT:
                Disconnected = false;
                usable = true;
                break;
            case PInvoke.WTS_CONSOLE_DISCONNECT:
            case PInvoke.WTS_REMOTE_DISCONNECT:
                Disconnected = true;
                break;
            default:
                return false; // logon, logoff, remote control: nothing the glow depends on
        }
        OnConsole = PInvoke.WTSGetActiveConsoleSessionId() == sessionId;
        Trace.WriteLine($"[SessionWatcher] Session {(Locked ? "locked" : "unlocked")}, {(Disconnected ? "disconnected" : OnConsole ? "on the console" : "remote")}.");
        return usable;
    }

    public void Dispose()
    {
        if (!registered.IsNull) PInvoke.WTSUnRegisterSessionNotification(registered);
        registered = default;
    }

    // The state now, for a start while locked or disconnected (an update restart). Unknown stays unlocked.
    private unsafe void ReadState()
    {
        OnConsole = PInvoke.WTSGetActiveConsoleSessionId() == sessionId;
        PWSTR buffer;
        uint bytes;
        if (!PInvoke.WTSQuerySessionInformation(HANDLE.WTS_CURRENT_SERVER_HANDLE, PInvoke.WTS_CURRENT_SESSION,
            WTS_INFO_CLASS.WTSSessionInfoEx, &buffer, &bytes))
        {
            return;
        }
        try
        {
            var info = (WTSINFOEXW*)buffer.Value;
            if (info is null || bytes < sizeof(WTSINFOEXW) || info->Level != 1) return;
            WTSINFOEX_LEVEL1_W session = info->Data.WTSInfoExLevel1;
            Disconnected = session.SessionState == WTS_CONNECTSTATE_CLASS.WTSDisconnected;
            // Windows 7 had these two flags reversed; Windows 8 and later report them as documented.
            if (session.SessionFlags == (int)PInvoke.WTS_SESSIONSTATE_LOCK) Locked = true;
            else if (session.SessionFlags == (int)PInvoke.WTS_SESSIONSTATE_UNLOCK) Locked = false;
        }
        finally
        {
            PInvoke.WTSFreeMemory(buffer.Value);
        }
    }
}
