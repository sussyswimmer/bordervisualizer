using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

// One overlay's pause on its monitor (OverlayHost.SetPausedMonitors, doc 04 §4) and the fade toward it.
internal interface IMonitorFade
{
    // Paused on this monitor: a fullscreen window covers it.
    bool Paused { get; }

    // 1 shown, 0 paused; moved by OverlayRules.AdvanceFade.
    float Fade { get; }
}

// OverlayHost's rules besides the frame pacing (FramePacer): what "On battery" does, the per-monitor pause fades, and
// when the glow counts as paused everywhere. Kept free of Windows calls, like FramePacer, so the overlay loop model in
// Rimlight.Platform.Tests runs these very rules. Owned by the overlay thread.
internal static class OverlayRules
{
    public const float MonitorFadeSeconds = 0.3f; // a monitor's pause fades out and back in over 300 ms (doc 04 §4)

    // "Reduce on battery": at most 30 fps (PRD §5) at half render scale (doc 04 §2).
    public static bool Reduced(Settings settings, bool onBattery) => onBattery && settings.OnBattery == BatteryBehavior.Reduce;

    // Moves a monitor's fade toward its target, 0 while paused and 1 otherwise, over MonitorFadeSeconds. True while it
    // moves, including the step that arrives.
    public static bool AdvanceFade(ref float fade, bool paused, float dt)
    {
        float target = paused ? 0 : 1;
        if (fade == target) return false;
        float step = dt / MonitorFadeSeconds;
        fade = target > fade ? Math.Min(target, fade + step) : Math.Max(target, fade - step);
        return true;
    }

    // The frame source's `paused`: by SetPaused, by "Pause on battery", on every monitor once its fade has finished (so
    // the fade shows whatever the engine does with the pause), or with no overlay at all (no display chosen, or none of
    // the chosen ones attached). The light engine fades the glow out, the loopback capture closes unless something else
    // shows the audio, and frames stop once the glow is static. Allocates nothing.
    public static bool PausedEverywhere<T>(bool paused, Settings settings, bool onBattery, List<T> overlays) where T : IMonitorFade
    {
        if (paused || (onBattery && settings.OnBattery == BatteryBehavior.Pause)) return true;
        foreach (T overlay in overlays)
            if (!overlay.Paused || overlay.Fade > 0) return false;
        return true;
    }
}
