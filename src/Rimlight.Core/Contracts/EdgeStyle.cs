namespace Rimlight.Core;

/// <summary>How the light meets the screen edge.</summary>
public enum EdgeStyle
{
    /// <summary>The soft glow, flat at the edge and fading inward (the default).</summary>
    Glow,
    /// <summary>A bright line along the very edge, with a dimmer glow inside it.</summary>
    Line,
}
