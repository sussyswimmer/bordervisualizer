namespace Rimlight.Core;

/// <summary>How the light moves with the music (CONTRACT CHANGE H-033).</summary>
public enum MovementStyle
{
    /// <summary>
    /// Light travels round the edge (the default): a bright comet chases round it, the colors stream round it, and each
    /// beat sends a flash running along it from one spot.
    /// </summary>
    Flow,
    /// <summary>The whole edge swells and brightens together on each beat, as before H-033.</summary>
    Pulse,
}
