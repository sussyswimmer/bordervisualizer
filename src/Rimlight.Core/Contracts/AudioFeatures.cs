namespace Rimlight.Core;

/// <summary>Audio features for one render frame.</summary>
/// <param name="Level">Smoothed loudness, 0..1.</param>
/// <param name="Bass">Smoothed bass, 0..1.</param>
/// <param name="Beat">Decaying beat pulse, 0..1.</param>
/// <param name="IsSilent">Whether sustained silence was detected.</param>
public readonly record struct AudioFeatures(float Level, float Bass, float Beat, bool IsSilent);
