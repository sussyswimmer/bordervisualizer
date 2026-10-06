using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace Rimlight.Platform.Overlay;

// The names Windows shows for monitors (e.g. "DELL U2720Q"), from the display configuration (CCD) API: each active
// path's source gives the GDI device name (\\.\DISPLAY1) and its target the monitor's EDID name and device interface
// path, which is the same string as DisplayMonitor.StableId.
internal static class MonitorNames
{
    private const int MaxAttempts = 3; // the configuration can change between sizing and querying

    // One monitor behind a GDI device: two or more when the desktop is duplicated (clone mode).
    internal readonly record struct Target(string? FriendlyName, string DevicePath, bool IsBuiltIn);

    // Targets by GDI device name (case-insensitive). Empty when Windows doesn't answer.
    public static Dictionary<string, List<Target>> ByDeviceName()
    {
        var targets = new Dictionary<string, List<Target>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            DISPLAYCONFIG_PATH_INFO[]? paths = QueryActivePaths();
            if (paths is null) return targets;
            foreach (DISPLAYCONFIG_PATH_INFO path in paths)
            {
                string? deviceName = SourceName(path.sourceInfo);
                if (deviceName is null || TargetName(path.targetInfo) is not { } target) continue;
                if (!targets.TryGetValue(deviceName, out List<Target>? list)) targets[deviceName] = list = [];
                list.Add(target);
            }
        }
        catch (Exception exception)
        {
            // Only the names are lost; the list falls back to "Display N".
            Trace.WriteLine($"[Displays] Reading monitor names failed: {exception.Message}");
        }
        return targets;
    }

    private static DISPLAYCONFIG_PATH_INFO[]? QueryActivePaths()
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount)
                != WIN32_ERROR.ERROR_SUCCESS)
                return null;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            WIN32_ERROR result = PInvoke.QueryDisplayConfig(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes);
            if (result == WIN32_ERROR.ERROR_SUCCESS) return paths[..(int)Math.Min(pathCount, (uint)paths.Length)];
            if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER) return null;
        }
        return null;
    }

    private static unsafe string? SourceName(in DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        name.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        name.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        name.header.adapterId = source.adapterId;
        name.header.id = source.id;
        if (PInvoke.DisplayConfigGetDeviceInfo(&name.header) != 0) return null;
        string deviceName = ToString(name.viewGdiDeviceName.AsSpan());
        return deviceName.Length > 0 ? deviceName : null;
    }

    private static unsafe Target? TargetName(in DISPLAYCONFIG_PATH_TARGET_INFO target)
    {
        var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        name.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        name.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        name.header.adapterId = target.adapterId;
        name.header.id = target.id;
        if (PInvoke.DisplayConfigGetDeviceInfo(&name.header) != 0) return null;
        string friendly = ToString(name.monitorFriendlyDeviceName.AsSpan()).Trim();
        // Laptop panels usually have no EDID name; they are named "Built-in display" instead.
        bool builtIn = name.outputTechnology is DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;
        return new Target(friendly.Length > 0 ? friendly : null, ToString(name.monitorDevicePath.AsSpan()), builtIn);
    }

    private static string ToString(ReadOnlySpan<char> buffer)
    {
        int end = buffer.IndexOf('\0');
        return new string(end < 0 ? buffer : buffer[..end]);
    }
}
