using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Which monitors show the glow, and how the Displays page changes that (doc 06 §1, H-009). Custom selections store
/// monitor IDs (<see cref="MonitorInfo.StableId"/>, compared ordinally) and keep the IDs of monitors that are unplugged
/// right now, so a selection survives re-plugging. Pure code.
/// </summary>
internal static class MonitorChoice
{
    /// <summary>Whether a monitor shows the glow under these settings (the overlay's own rule).</summary>
    public static bool IsLit(Settings settings, MonitorInfo monitor) => settings.Monitors switch
    {
        MonitorSelection.PrimaryOnly => monitor.IsPrimary,
        MonitorSelection.Custom => (settings.CustomMonitorIds ?? []).Contains(monitor.StableId, StringComparer.Ordinal),
        _ => true,
    };

    /// <summary>
    /// "Choose displays": restores the previous custom choice if it lights any attached monitor; otherwise starts from
    /// the monitors lit now, so nothing changes on screen. Remembered unplugged monitors stay in the list either way.
    /// </summary>
    public static Settings ChooseDisplays(Settings settings, IReadOnlyList<MonitorInfo> attached)
    {
        if (settings.Monitors == MonitorSelection.Custom) return settings;
        bool previousLightsSome = attached.Any(m => Valid(settings.CustomMonitorIds).Contains(m.StableId, StringComparer.Ordinal));
        return previousLightsSome
            ? settings with { Monitors = MonitorSelection.Custom }
            : settings with { Monitors = MonitorSelection.Custom, CustomMonitorIds = LitNow(settings, attached) };
    }

    /// <summary>
    /// Turns one monitor on or off. From "All" or "Primary only" this switches to a custom choice that starts from the
    /// monitors lit now, so only the toggled one changes.
    /// </summary>
    public static Settings Toggle(Settings settings, IReadOnlyList<MonitorInfo> attached, MonitorInfo monitor, bool lit)
    {
        List<string> ids = settings.Monitors == MonitorSelection.Custom
            ? [.. Valid(settings.CustomMonitorIds)]
            : [.. LitNow(settings, attached)];
        ids.RemoveAll(id => string.Equals(id, monitor.StableId, StringComparison.Ordinal));
        if (lit) ids.Add(monitor.StableId);
        return settings with { Monitors = MonitorSelection.Custom, CustomMonitorIds = ids };
    }

    // The remembered IDs of monitors that aren't attached, then every attached monitor lit under the current choice.
    private static List<string> LitNow(Settings settings, IReadOnlyList<MonitorInfo> attached)
    {
        var ids = new List<string>();
        foreach (string id in Valid(settings.CustomMonitorIds))
            if (!attached.Any(m => string.Equals(m.StableId, id, StringComparison.Ordinal)) && !ids.Contains(id, StringComparer.Ordinal))
                ids.Add(id);
        foreach (MonitorInfo monitor in attached)
            if (IsLit(settings, monitor) && !ids.Contains(monitor.StableId, StringComparer.Ordinal))
                ids.Add(monitor.StableId);
        return ids;
    }

    // H-009: only null or empty entries are dropped.
    private static IEnumerable<string> Valid(IReadOnlyList<string>? ids) =>
        (ids ?? []).Where(id => !string.IsNullOrEmpty(id));
}
