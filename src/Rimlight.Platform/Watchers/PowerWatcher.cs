using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Watchers;

// Sleep and wake, the power source and the display state (doc 02 "Lifetime events", PRD §5), from WM_POWERBROADCAST.
// Those go to top-level windows only, so this listens on the system watcher's hidden top-level window and thread.
internal sealed class PowerWatcher : IDisposable
{
    private const uint DisplayStateOff = 0; // GUID_CONSOLE_DISPLAY_STATE data: 0 off, 1 on, 2 dimmed (still shown)

    private static readonly Guid DisplayState = PInvoke.GUID_CONSOLE_DISPLAY_STATE;

    private HPOWERNOTIFY displayNotification;

    // Between PBT_APMSUSPEND and the resume. Modern Standby machines don't send it; their display turns off instead.
    public bool Suspended { get; private set; }

    // The physical display is off. It belongs to the console session, so a remote session ignores it.
    public bool ConsoleDisplayOff { get; private set; }

    public bool OnBattery { get; private set; } = PowerStatus.IsOnBattery();

    // Registering sends the current display state at once, as a WM_POWERBROADCAST through the message loop.
    public unsafe void Register(HWND window)
    {
        Guid setting = DisplayState;
        displayNotification = PInvoke.RegisterPowerSettingNotification(window, &setting, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (displayNotification.IsNull)
            Trace.WriteLine($"[PowerWatcher] RegisterPowerSettingNotification failed ({Marshal.GetLastPInvokeError()}); the glow won't pause while the display is off.");
    }

    // Handles WM_POWERBROADCAST. Returns true when the machine woke or the display came back on: the GPU device and
    // the displays may have changed meanwhile.
    public unsafe bool Handle(WPARAM wParam, LPARAM lParam)
    {
        switch ((uint)wParam.Value)
        {
            case PInvoke.PBT_APMSUSPEND:
                Suspended = true;
                Trace.WriteLine("[PowerWatcher] Suspending.");
                return false;

            case PInvoke.PBT_APMRESUMEAUTOMATIC: // every resume
            case PInvoke.PBT_APMRESUMESUSPEND:   // a resume the user started; follows the automatic one
                Suspended = false;
                OnBattery = PowerStatus.IsOnBattery(); // the power source may have changed during sleep
                Trace.WriteLine("[PowerWatcher] Resumed.");
                return true;

            case PInvoke.PBT_APMPOWERSTATUSCHANGE: // AC/DC switches, and battery level changes
                OnBattery = PowerStatus.IsOnBattery();
                return false;

            case PInvoke.PBT_POWERSETTINGCHANGE:
                var setting = (POWERBROADCAST_SETTING*)lParam.Value;
                if (setting is null || setting->PowerSetting != DisplayState || setting->DataLength < sizeof(uint)) return false;
                bool off = *(uint*)&setting->Data == DisplayStateOff;
                bool cameBack = ConsoleDisplayOff && !off;
                ConsoleDisplayOff = off;
                // A resume always sends PBT_APMRESUMEAUTOMATIC; the display coming back also ends a suspend in case
                // that was missed, so the glow can never stay paused after a wake.
                if (cameBack) Suspended = false;
                return cameBack;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (!displayNotification.IsNull) PInvoke.UnregisterPowerSettingNotification(displayNotification);
        displayNotification = default;
    }
}
