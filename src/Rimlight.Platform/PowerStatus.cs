using Windows.Win32;
using Windows.Win32.System.Power;

namespace Rimlight.Platform;

/// <summary>The machine's power source (PRD §5 "On battery").</summary>
public static class PowerStatus
{
    private const byte AcOffline = 0; // SYSTEM_POWER_STATUS.ACLineStatus: 0 offline, 1 online, 255 unknown

    /// <summary>
    /// True when the machine runs on battery right now. False on AC power, on desktops without a battery, and when
    /// Windows can't tell.
    /// </summary>
    public static bool IsOnBattery() =>
        PInvoke.GetSystemPowerStatus(out SYSTEM_POWER_STATUS status) && status.ACLineStatus == AcOffline;
}
