using Rimlight.Core;

namespace Rimlight.Bench;

/// <summary>How the overlay thread waits for its next frame (Platform's <c>Pace</c>).</summary>
internal enum Pace
{
    /// <summary>At the full frame rate.</summary>
    Full,
    /// <summary>At 10 frames a second.</summary>
    Slow,
    /// <summary>No frames until an input changes.</summary>
    Block,
}

// The frame schedule of the overlay thread (doc 02 "Frame pacing"): Platform's FramePacer.Next and the dt rule of
// OverlayHost's loop, for one monitor that presents every frame it draws. Rimlight.Platform is Windows-only, so the
// policy is mirrored here: keep the two in step. Per-monitor pause fades and unfinished presents (both keep the full
// rate in the app) don't occur in a simulation without a display.
internal sealed class PaceModel
{
    /// <summary>Seconds between slow frames: 10 fps for breathing and for listening (FramePacer.SlowInterval).</summary>
    public const double SlowInterval = 0.1;
    /// <summary>Slow motion runs at the full rate this long after an input changes (FramePacer.InputBusySeconds).</summary>
    public const double InputBusySeconds = 1.0;
    /// <summary>... and this long after the light last changed quickly (FramePacer.ChangeHoldSeconds).</summary>
    public const double ChangeHoldSeconds = 0.5;
    /// <summary>A change faster than this per second counts as quick (FramePacer.FastChangePerSecond).</summary>
    public const float FastChangePerSecond = 0.25f;
    /// <summary>A longer gap between frames is passed as one ordinary frame (OverlayHost.MaxFrameDtSeconds).</summary>
    public const double MaxFrameDtSeconds = 0.25;
    /// <summary>Below this Visibility nothing is drawn and the overlay is hidden (GlowConstants.MinVisibility).</summary>
    public const float MinVisibility = 0.5f / 255;

    private LightState previous;
    private double busyUntil = double.NegativeInfinity;
    private double changedUntil = double.NegativeInfinity;

    /// <summary>An input changed (settings, pause, displays): transitions it starts get the full rate for a moment.</summary>
    /// <param name="nowSeconds">The time of the change.</param>
    public void NoteInputChanged(double nowSeconds) => busyUntil = nowSeconds + InputBusySeconds;

    /// <summary>Decides how to wait after a frame (FramePacer.Next).</summary>
    /// <param name="nowSeconds">The frame's time.</param>
    /// <param name="motion">The frame source's motion.</param>
    /// <param name="state">The light state the frame drew.</param>
    /// <param name="dtSeconds">The frame's dt.</param>
    /// <param name="gradientChanged">Whether the frame refilled the gradient.</param>
    /// <returns>The pace until the next frame.</returns>
    public Pace Next(double nowSeconds, FrameMotion motion, in LightState state, float dtSeconds, bool gradientChanged)
    {
        if (gradientChanged || ChangeRate(previous, state, dtSeconds) > FastChangePerSecond)
            changedUntil = nowSeconds + ChangeHoldSeconds;
        previous = state;

        bool busy = nowSeconds < busyUntil || nowSeconds < changedUntil;
        Pace pace = motion switch
        {
            FrameMotion.Full => Pace.Full,
            FrameMotion.Slow => busy ? Pace.Full : Pace.Slow,
            FrameMotion.Listening => Pace.Slow,
            _ => Pace.Block,
        };
        // Nothing on screen and the light hidden (Visibility exactly 0, H-011): frames only keep the analysis going. A
        // light fading in keeps the full rate even under MinVisibility, as in FramePacer since C13: above about 130 Hz
        // the first frame of a 300 ms fade-in is still under it.
        bool visible = state.Visibility >= MinVisibility;
        return pace == Pace.Full && !visible && !(state.Visibility > 0) ? Pace.Slow : pace;
    }

    /// <summary>The dt the overlay passes for a frame (OverlayHost's loop): the time since the previous frame, one
    /// ordinary frame after a longer gap, and at most one frame when an input brought the frame forward.</summary>
    /// <param name="elapsedSeconds">Time since the previous frame.</param>
    /// <param name="fullInterval">Seconds between frames at the full rate.</param>
    /// <param name="due">False when an input brought the frame forward.</param>
    /// <returns>The dt in seconds.</returns>
    public static float FrameDt(double elapsedSeconds, double fullInterval, bool due) =>
        (float)(elapsedSeconds > MaxFrameDtSeconds ? fullInterval : due ? elapsedSeconds : Math.Min(elapsedSeconds, fullInterval));

    // FramePacer.ChangeRate: the fastest change of anything the glow shows, per second (colors reach the screen through
    // the gradient, whose refills count on their own).
    private static float ChangeRate(in LightState a, in LightState b, float dtSeconds)
    {
        if (!(dtSeconds > 0)) return 0;
        float phase = MathF.Abs(b.Phase - a.Phase) % 1;
        float change = MathF.Max(
            MathF.Max(MathF.Max(Delta(a.Intensity, b.Intensity), Delta(a.Visibility, b.Visibility)),
                MathF.Max(Delta(a.Pulse, b.Pulse), Delta(a.Spread, b.Spread))),
            MathF.Max(MathF.Max(Delta(a.Ratio, b.Ratio), Delta(a.CoreThicknessDip, b.CoreThicknessDip) / 40),
                float.IsFinite(phase) ? MathF.Min(phase, 1 - phase) : 0));
        return change / dtSeconds;
    }

    private static float Delta(float a, float b)
    {
        float d = MathF.Abs(b - a);
        return float.IsFinite(d) ? d : 0;
    }
}
