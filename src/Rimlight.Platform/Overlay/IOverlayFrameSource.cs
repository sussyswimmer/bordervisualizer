using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

/// <summary>
/// Supplies the glow for each frame. Called on the overlay thread only, once per rendered frame; implementations
/// must not block or allocate.
/// </summary>
public interface IOverlayFrameSource
{
    /// <summary>Advances to the next frame.</summary>
    /// <param name="dtSeconds">Time since the previous call, in seconds.</param>
    /// <param name="gradient">
    /// 64 linear RGBA texels (256 floats), as <see cref="IPaletteBlender.FillGradient"/> writes them. The buffer keeps
    /// its contents between calls; refill it only when the colors change.
    /// </param>
    /// <param name="gradientChanged">True when <paramref name="gradient"/> was refilled during this call.</param>
    /// <returns>The light state to draw on every overlay.</returns>
    LightState NextFrame(float dtSeconds, Span<float> gradient, out bool gradientChanged);
}
