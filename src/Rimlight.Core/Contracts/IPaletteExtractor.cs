namespace Rimlight.Core;

/// <summary>Extracts a two-color palette from decoded artwork.</summary>
public interface IPaletteExtractor
{
    /// <summary>Extracts colors, or returns null when the image represents missing artwork.</summary>
    /// <param name="bgra">Tightly packed BGRA8 pixels.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="trackId">Source track identifier.</param>
    /// <returns>A linear RGB palette, or null for no art.</returns>
    Palette? Extract(ReadOnlySpan<byte> bgra, int width, int height, string trackId);
}
