using System.Text;

namespace Rimlight.Platform.Watchers;

/// <summary>
/// What the system says about showing the glow (doc 02 "Lifetime events", doc 04 §4, PRD §5): published by
/// <see cref="SystemWatcher"/> as a new immutable snapshot on every change.
/// </summary>
public sealed record SystemState
{
    /// <summary>The session is locked.</summary>
    public bool Locked { get; init; }

    /// <summary>No terminal shows the session: it was switched away from (fast user switching) or Remote Desktop disconnected.</summary>
    public bool Disconnected { get; init; }

    /// <summary>The machine is going to sleep or hibernate.</summary>
    public bool Suspended { get; init; }

    /// <summary>The display this session is shown on is off (a remote session never reports this).</summary>
    public bool DisplayOff { get; init; }

    /// <summary>The machine runs on battery (<see cref="PowerStatus.IsOnBattery"/>).</summary>
    public bool OnBattery { get; init; }

    /// <summary>The session is shown through Remote Desktop rather than on this machine's console.</summary>
    public bool RemoteSession { get; init; }

    /// <summary>
    /// A fullscreen state not tied to one monitor: a screen saver, presentation settings, or an exclusive-mode
    /// Direct3D app without a foreground window. Only reported while fullscreen detection is on.
    /// </summary>
    public bool FullscreenEverywhere { get; init; }

    /// <summary>
    /// GDI device names (<c>\\.\DISPLAY1</c>) of the monitors a fullscreen app covers, sorted; empty while fullscreen
    /// detection is off. The same list instance stays in every snapshot until the set changes, so record equality
    /// tells a real change.
    /// </summary>
    public IReadOnlyList<string> FullscreenMonitors { get; init; } = [];

    /// <summary>True when the glow should pause on every monitor: locked, disconnected, asleep, display off, or fullscreen everywhere.</summary>
    public bool PausesEverywhere => Locked || Disconnected || Suspended || DisplayOff || FullscreenEverywhere;

    /// <summary>A short description for logs and the Debug menu, e.g. "unlocked, display on, AC, no fullscreen".</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append(Locked ? "locked" : "unlocked");
        if (Disconnected) text.Append(", disconnected");
        else if (RemoteSession) text.Append(", remote session");
        if (Suspended) text.Append(", suspended");
        text.Append(DisplayOff ? ", display off" : ", display on");
        text.Append(OnBattery ? ", battery" : ", AC");
        if (FullscreenEverywhere) text.Append(", fullscreen everywhere");
        else if (FullscreenMonitors.Count > 0) text.Append(", fullscreen on ").AppendJoin(" ", FullscreenMonitors);
        else text.Append(", no fullscreen");
        return text.ToString();
    }
}
