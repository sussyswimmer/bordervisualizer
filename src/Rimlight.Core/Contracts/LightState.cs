namespace Rimlight.Core;

/// <summary>The complete render state for a frame.</summary>
/// <param name="ColorA">Primary linear color.</param>
/// <param name="ColorB">Secondary linear color.</param>
/// <param name="Ratio">Primary gradient share, 0..1.</param>
/// <param name="Intensity">Brightness, 0..1.</param>
/// <param name="Spread">Glow reach, 0..1.</param>
/// <param name="CoreThicknessDip">Core thickness in DIPs.</param>
/// <param name="Phase">Gradient rotation, 0..1.</param>
/// <param name="Pulse">Beat pulse, 0..1.</param>
/// <param name="Visibility">Overlay visibility, 0..1.</param>
public readonly record struct LightState(Rgb ColorA, Rgb ColorB, float Ratio, float Intensity, float Spread, float CoreThicknessDip, float Phase, float Pulse, float Visibility)
{
    /// <summary>
    /// The Flow movement's light travelling round the edge (CONTRACT CHANGE H-033). default is none: the whole edge
    /// lit evenly, as before H-033.
    /// </summary>
    public FlowMotion Flow { get; init; }
}

/// <summary>
/// The Flow movement (<see cref="MovementStyle.Flow"/>) for one frame: how much of it shows, where its comets are and
/// the beats' ripples. Positions and distances are fractions of the screen's perimeter, which runs clockwise from the
/// top-left corner (the gradient's coordinate before <see cref="LightState.Phase"/>).
/// </summary>
/// <param name="Amount">How much the light varies round the edge, 0..1: 0 lights the whole edge evenly.</param>
/// <param name="CometPhase">Where the comets are, 0..1; it grows as they move, counterclockwise like the colors.</param>
/// <param name="Ripple0">A beat's ripple; a strength of 0 is none.</param>
/// <param name="Ripple1">A beat's ripple.</param>
/// <param name="Ripple2">A beat's ripple.</param>
/// <param name="Ripple3">A beat's ripple.</param>
public readonly record struct FlowMotion(float Amount, float CometPhase, Ripple Ripple0, Ripple Ripple1, Ripple Ripple2, Ripple Ripple3);

/// <summary>A flash a beat sends along the edge: two fronts running both ways from its origin as it fades.</summary>
/// <param name="Origin">Where it started on the perimeter, 0..1.</param>
/// <param name="Radius">How far each front has run from the origin, in perimeters, 0 or more.</param>
/// <param name="Strength">Its brightness now, 0..1; 0 is no ripple.</param>
public readonly record struct Ripple(float Origin, float Radius, float Strength);
