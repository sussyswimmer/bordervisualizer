using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Rimlight.Platform.Overlay;

// A monitor as the overlay sees it: physical-pixel rectangles (the process is Per-Monitor V2 aware) and its DPI.
// DeviceName (\\.\DISPLAY1) keys overlays within a session; StableId is the monitor's device interface path
// (doc 06 §1), which survives reboots and re-plugging and is what Settings.CustomMonitorIds stores. RefreshHz is 0
// when Windows doesn't report it.
internal sealed record DisplayMonitor(string DeviceName, string StableId, RECT Bounds, RECT WorkArea, uint Dpi, bool IsPrimary, uint RefreshHz)
{
    public float Scale => Dpi / 96f;
}

/// <summary>A monitor as Settings lists it (the Displays page, doc 06 §3).</summary>
/// <param name="StableId">
/// The monitor's device interface path (<c>DISPLAY_DEVICE.DeviceID</c>), the ID <see cref="Settings.CustomMonitorIds"/>
/// stores (H-009). It survives reboots and re-plugging.
/// </param>
/// <param name="DeviceName">The GDI device name, e.g. <c>\\.\DISPLAY1</c>; stable within a session only.</param>
/// <param name="FriendlyName">The name Windows shows, e.g. <c>DELL U2720Q</c>; null when the monitor reports none.</param>
/// <param name="IsBuiltIn">True for a laptop's own panel.</param>
/// <param name="IsPrimary">True for the primary monitor.</param>
/// <param name="Left">Left edge of the monitor on the desktop, in physical pixels.</param>
/// <param name="Top">Top edge of the monitor on the desktop, in physical pixels.</param>
/// <param name="Width">Width in physical pixels.</param>
/// <param name="Height">Height in physical pixels.</param>
/// <param name="RefreshHz">The refresh rate in Hz, or 0 when Windows doesn't report it.</param>
public sealed record MonitorInfo(string StableId, string DeviceName, string? FriendlyName, bool IsBuiltIn, bool IsPrimary,
    int Left, int Top, int Width, int Height, uint RefreshHz);

/// <summary>The attached monitors, as the overlay sees them.</summary>
public static class DisplayMonitors
{
    /// <summary>
    /// The GDI device name of the primary monitor (for example <c>\\.\DISPLAY1</c>), the name
    /// <see cref="OverlayHost.SetPausedMonitors"/> takes; null if there is none.
    /// </summary>
    public static string? PrimaryDeviceName() => Enumerate().Find(monitor => monitor.IsPrimary)?.DeviceName;

    /// <summary>
    /// The attached monitors with the names Windows shows for them, left to right (then top to bottom). Never throws;
    /// a monitor whose name can't be read has a null <see cref="MonitorInfo.FriendlyName"/>. Any thread.
    /// </summary>
    /// <returns>The monitors; empty if none can be enumerated.</returns>
    public static IReadOnlyList<MonitorInfo> Describe()
    {
        try
        {
            Dictionary<string, List<MonitorNames.Target>> names = MonitorNames.ByDeviceName();
            var list = new List<MonitorInfo>();
            foreach (DisplayMonitor monitor in Enumerate())
            {
                MonitorNames.Target? target = null;
                if (names.TryGetValue(monitor.DeviceName, out List<MonitorNames.Target>? targets) && targets.Count > 0)
                {
                    // A duplicated desktop shows several monitors on one device; the overlay's ID is the first one's.
                    int match = targets.FindIndex(t => string.Equals(t.DevicePath, monitor.StableId, StringComparison.OrdinalIgnoreCase));
                    target = targets[Math.Max(match, 0)];
                }
                RECT bounds = monitor.Bounds;
                list.Add(new MonitorInfo(monitor.StableId, monitor.DeviceName, target?.FriendlyName, target?.IsBuiltIn ?? false,
                    monitor.IsPrimary, bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top, monitor.RefreshHz));
            }
            list.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Top.CompareTo(b.Top));
            return list;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine($"[Displays] Listing the monitors failed: {exception.Message}");
            return [];
        }
    }

    // Enumerates the attached monitors. Allocates; called only when the layout may have changed.
    internal static unsafe List<DisplayMonitor> Enumerate()
    {
        var handles = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(default, (RECT?)null, (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        }, default);

        var monitors = new List<DisplayMonitor>(handles.Count);
        foreach (HMONITOR handle in handles)
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(handle, (MONITORINFO*)&info)) continue;

            string deviceName = ToString(info.szDevice.AsSpan());
            uint dpi = PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded && dpiX > 0 ? dpiX : 96;
            bool primary = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;
            monitors.Add(new DisplayMonitor(deviceName, StableId(deviceName), info.monitorInfo.rcMonitor, info.monitorInfo.rcWork, dpi, primary,
                RefreshRate(deviceName)));
        }
        return monitors;
    }

    // The display mode's refresh rate in Hz (59 for 59.94), or 0 when the driver reports its default (0 or 1).
    private static unsafe uint RefreshRate(string deviceName)
    {
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        if (!PInvoke.EnumDisplaySettings(deviceName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode)) return 0;
        return mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 0;
    }

    // The first monitor device on this display adapter output, as a device interface path. Falls back to the
    // GDI device name, which is stable enough within a session.
    private static unsafe string StableId(string deviceName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (PInvoke.EnumDisplayDevices(deviceName, 0, ref device, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            string id = ToString(device.DeviceID.AsSpan());
            if (id.Length > 0) return id;
        }
        return deviceName;
    }

    private static string ToString(ReadOnlySpan<char> buffer)
    {
        int end = buffer.IndexOf('\0');
        return new string(end < 0 ? buffer : buffer[..end]);
    }
}
