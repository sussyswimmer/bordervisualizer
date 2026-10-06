using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace Rimlight.Platform;

/// <summary>
/// DPI awareness checks. The app manifest requests Per-Monitor V2, which the overlay relies on to place
/// windows in physical pixels on mixed-DPI setups (doc 04 §1).
/// </summary>
public static class DpiAwareness
{
    /// <summary>True when the calling thread is Per-Monitor V2 DPI aware, i.e. the manifest took effect.</summary>
    public static bool IsPerMonitorV2() =>
        PInvoke.AreDpiAwarenessContextsEqual(
            PInvoke.GetThreadDpiAwarenessContext(),
            DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
}
