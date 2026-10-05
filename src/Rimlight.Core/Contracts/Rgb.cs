namespace Rimlight.Core;

/// <summary>A color in linear RGB.</summary>
/// <param name="R">Red, 0..1.</param>
/// <param name="G">Green, 0..1.</param>
/// <param name="B">Blue, 0..1.</param>
public readonly record struct Rgb(float R, float G, float B);
