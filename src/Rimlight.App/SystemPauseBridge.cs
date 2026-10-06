using System.Diagnostics;
using Rimlight.Core;
using Rimlight.Platform;
using Rimlight.Platform.Overlay;
using Rimlight.Platform.Watchers;

namespace Rimlight.App;

/// <summary>
/// Feeds the overlay what the system watcher reports (doc 02 "Lifetime events", doc 04 §4, PRD §5): a locked or
/// disconnected session, sleep, the display turning off and fullscreen apps pause the glow; battery power applies
/// "On battery"; a wake re-checks the GPU and the monitors. The Debug menu can add simulated states on top.
/// </summary>
internal sealed class SystemPauseBridge : IDisposable
{
    private readonly object gate = new(); // orders applying from the watcher and UI threads; never taken per frame
    private readonly SystemWatcher? watcher;
    private readonly SystemState fallback; // without a watcher: the power source at start, nothing paused
    private OverlayHost? overlays;
    private string[] appliedMonitors = [];
    private SimulatedSystem simulated;

    /// <summary>Starts watching (on its own thread) and waits briefly for the first state.</summary>
    /// <param name="settings">The settings at start ("Pause in fullscreen apps").</param>
    public SystemPauseBridge(Settings settings)
    {
        fallback = new SystemState { OnBattery = PowerStatus.IsOnBattery() };
        var created = new SystemWatcher(settings.PauseInFullscreen);
        created.StateChanged += _ => Apply();
        created.Resumed += OnResumed;
        try
        {
            created.Start();
            watcher = created;
        }
        catch (Exception exception)
        {
            // The glow keeps running, just without pausing for the system (and with the power source at start).
            Trace.WriteLine($"[App] The system watcher could not start: {exception}");
            created.Dispose();
        }
    }

    /// <summary>The system as last reported. Safe to read from any thread.</summary>
    public SystemState State => watcher?.State ?? fallback;

    /// <summary>Debug stand-ins combined with the real state: battery, a pause everywhere, a paused monitor.</summary>
    public SimulatedSystem Simulated
    {
        get
        {
            lock (gate) return simulated;
        }
        set
        {
            lock (gate) simulated = value;
            Apply();
        }
    }

    /// <summary>Starts feeding this overlay (before its <see cref="OverlayHost.Start"/>, so its first frame knows); null stops.</summary>
    public void Attach(OverlayHost? host)
    {
        lock (gate)
        {
            overlays = host;
            appliedMonitors = []; // a new host starts with no paused monitor
        }
        Apply();
    }

    /// <summary>Applies a settings change ("Pause in fullscreen apps"). Call from the UI thread.</summary>
    public void ApplySettings(Settings settings) => watcher?.SetFullscreenDetection(settings.PauseInFullscreen);

    /// <summary>Stops the watcher; no state reaches the overlay after this.</summary>
    public void Dispose()
    {
        watcher?.Dispose();
        Attach(null);
    }

    // Watcher or UI thread. Reads the latest state under the lock, so the last call always applies the newest values.
    private void Apply()
    {
        lock (gate)
        {
            if (overlays is not { } host) return;
            SystemState state = State;
            host.SetOnBattery(state.OnBattery || simulated.OnBattery);
            host.SetPaused(state.PausesEverywhere || simulated.PausedEverywhere);

            string[] monitors = [.. state.FullscreenMonitors];
            if (simulated.PausedMonitor is { } extra && !monitors.Contains(extra, StringComparer.OrdinalIgnoreCase)) monitors = [.. monitors, extra];
            // Only a real change reaches the overlay: each new set wakes it and starts per-monitor fades.
            if (!monitors.AsSpan().SequenceEqual(appliedMonitors))
            {
                appliedMonitors = monitors;
                host.SetPausedMonitors(monitors);
            }
        }
    }

    // Watcher thread: woke from sleep, unlocked, reconnected or the display came back on.
    private void OnResumed()
    {
        lock (gate) overlays?.CheckDevices();
    }
}

/// <summary>Simulated system states for the Debug menu's render tests.</summary>
/// <param name="OnBattery">Act as if on battery power.</param>
/// <param name="PausedEverywhere">Pause everywhere, like a locked session.</param>
/// <param name="PausedMonitor">Pause this monitor (GDI device name), like a fullscreen video; null for none.</param>
internal readonly record struct SimulatedSystem(bool OnBattery, bool PausedEverywhere, string? PausedMonitor);
