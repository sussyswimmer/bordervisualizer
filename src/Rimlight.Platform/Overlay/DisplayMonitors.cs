using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Rimlight.Platform.Overlay;

// A monitor as the overlay sees it: physical-pixel rectangles (the process is Per-Monitor V2 aware) and its DPI.
// DeviceName (\\.\DISPLAY1) keys overlays within a session; StableId is the monitor's device interface path
// (doc 06 §1), which survives reboots and re-plugging and is what Settings.CustomMonitorIds stores.
internal sealed record DisplayMonitor(string DeviceName, string StableId, RECT Bounds, RECT WorkArea, uint Dpi, bool IsPrimary)
{
    public float Scale => Dpi / 96f;
}

internal static class DisplayMonitors
{
    // Enumerates the attached monitors. Allocates; called only when the layout may have changed.
    public static unsafe List<DisplayMonitor> Enumerate()
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
            monitors.Add(new DisplayMonitor(deviceName, StableId(deviceName), info.monitorInfo.rcMonitor, info.monitorInfo.rcWork, dpi, primary));
        }
        return monitors;
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
