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
public readonly record struct LightState(Rgb ColorA, Rgb ColorB, float Ratio, float Intensity, float Spread, float CoreThicknessDip, float Phase, float Pulse, float Visibility);
