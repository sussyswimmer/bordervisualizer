using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

/// <summary>
/// Supplies the glow for each frame. Called on the overlay thread only, once per frame; implementations must not
/// block or allocate.
/// </summary>
public interface IOverlayFrameSource
{
    /// <summary>Advances to the next frame.</summary>
    /// <param name="dtSeconds">
    /// Time since the previous call, in seconds. After a pause in calls (nothing could change, or nothing was shown)
    /// this is one ordinary frame, not the length of the gap, so fades and smoothing continue where they stopped.
    /// </param>
    /// <param name="settings">The settings snapshot for this frame (the one last passed to <see cref="OverlayHost.ApplySettings"/>).</param>
    /// <param name="paused">
    /// True while the glow should be paused: <see cref="OverlayHost.SetPaused"/>, "Pause on battery", or every overlay
    /// paused on its monitor (<see cref="OverlayHost.SetPausedMonitors"/>). Pass it to <see cref="ILightEngine.Update"/>.
    /// Also true for the one call made when the last overlay goes away (no monitor selected or connected); no calls
    /// follow until an overlay exists again, so release whatever only a shown glow needs, such as audio capture.
    /// </param>
    /// <param name="gradient">
    /// 64 linear RGBA texels (256 floats), as <see cref="IPaletteBlender.FillGradient"/> writes them. The buffer keeps
    /// its contents between calls; refill it only when the colors change.
    /// </param>
    /// <returns>The light state to draw on every overlay, and what to expect next.</returns>
    OverlayFrame NextFrame(float dtSeconds, Settings settings, bool paused, Span<float> gradient);
}

/// <summary>One frame from an <see cref="IOverlayFrameSource"/>.</summary>
/// <param name="State">The light state to draw on every overlay.</param>
/// <param name="GradientChanged">True when the gradient was refilled during this call.</param>
/// <param name="Motion">How the glow will change from here; the overlay picks its frame rate from it.</param>
public readonly record struct OverlayFrame(LightState State, bool GradientChanged, FrameMotion Motion);

/// <summary>How the glow is about to change, as the frame source sees it (doc 02 "Frame pacing").</summary>
public enum FrameMotion
{
    /// <summary>Driven by music, or in a transition: draw at the full frame rate.</summary>
    Full,

    /// <summary>Only slow change, such as Idle Glow breathing: 10 frames a second are enough.</summary>
    Slow,

    /// <summary>
    /// Nothing changes until the audio does (<see cref="ILightEngine.IsStatic"/>, music sync): keep calling the source
    /// about 10 times a second so it can notice music, but present nothing new.
    /// </summary>
    Listening,

    /// <summary>
    /// Nothing changes until an input does (<see cref="ILightEngine.IsStatic"/>, and audio doesn't matter): once the
    /// screen shows this frame, stop calling the source until settings, pause, battery or displays change.
    /// </summary>
    Still,
}
