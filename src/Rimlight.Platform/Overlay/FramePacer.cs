using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

// How the overlay thread waits for the next frame.
internal enum Pace
{
    // At the full rate: on the display's refresh (FramePacer.UseVsync) or on the frame timer at FullInterval.
    Full,
    // On the frame timer at SlowInterval: Idle Glow breathing, or listening for music with nothing changing.
    Slow,
    // No frames: wait for an input change (settings, pause, battery, displays) or a window message.
    Block,
}

// What one frame showed, for FramePacer.Next.
internal readonly record struct FrameReport(
    FrameMotion Motion,      // what the frame source expects next
    LightState State,        // the light state the frame drew
    float DtSeconds,         // time since the previous frame
    bool GradientChanged,    // the palette gradient was refilled this frame
    bool AnyVisible,         // some overlay shows a glow (the others are hidden)
    bool Transitioning,      // a per-monitor fade is in progress
    bool AllPresented);      // every overlay already shows this frame (or is hidden); nothing is left to present

// The frame-pacing policy (doc 02 "Frame pacing"), kept free of Windows calls so it can be checked off Windows.
// Owned by the overlay thread.
//
// Full rate is the FpsCap (30/60/120, or 0 = the display's refresh), never above the primary monitor's refresh, and
// capped at 30 fps when "Reduce on battery" applies. When that rate is the display's refresh the overlay thread waits
// on a swap chain's frame-latency object (vsync) instead of the timer, so frames line up with the display.
// The source's FrameMotion picks the pace; a few host-side facts override it:
//   - an unfinished present or a per-monitor fade needs the full rate until it is done;
//   - for InputBusySeconds after an input changes, and ChangeHoldSeconds after the state last changed quickly,
//     Slow motion runs at the full rate (an engine's own transitions are unknown here, and 10 fps would step them);
//   - with nothing on screen the full rate is pointless: frames only listen for music, at the slow rate.
internal sealed class FramePacer
{
    public const double SlowInterval = 0.1;            // 10 fps: Idle Glow breathing is slow (doc 02)
    public const int ReducedFps = 30;                  // "Reduce on battery" (PRD §5)
    public const double InputBusySeconds = 1.0;        // after a settings, pause or battery change
    public const double ChangeHoldSeconds = 0.5;       // after the last fast change of the light state
    // Idle breathing (±10% over ~6 s, PRD §2) changes brightness by at most ~0.1 per second; anything faster would
    // visibly step at 10 fps (more than 2.5% per frame).
    public const float FastChangePerSecond = 0.25f;
    public const double DefaultRefreshHz = 60;
    private const double VsyncTolerance = 0.97;        // a 59 Hz report of a 59.94 Hz display still counts as 60

    private LightState previous;
    private double busyUntil = double.NegativeInfinity;
    private double changedUntil = double.NegativeInfinity;

    public FramePacer() => Configure(60, DefaultRefreshHz, reduce: false);

    // Seconds between frames at the full rate.
    public double FullInterval { get; private set; }

    // True when the full rate is the display's refresh: pace on the swap chain's frame-latency object.
    public bool UseVsync { get; private set; }

    // fpsCap: Settings.FpsCap (30 | 60 | 120 | 0 = native; anything else counts as 60). refreshHz: the primary
    // monitor's refresh rate, or 0 if unknown. reduce: "Reduce on battery" applies. Vsync pacing must then wait on a
    // surface of a monitor with this refresh rate.
    public void Configure(int fpsCap, double refreshHz, bool reduce)
    {
        bool known = refreshHz is >= 20 and <= 1000;
        double refresh = known ? refreshHz : DefaultRefreshHz;
        double rate = fpsCap switch
        {
            0 => refresh,
            30 or 60 or 120 => Math.Min(fpsCap, refresh),
            _ => Math.Min(60, refresh),
        };
        if (reduce) rate = Math.Min(rate, ReducedFps);
        // With an unknown refresh rate only "native" may follow the display: a cap could be far below it.
        UseVsync = known ? rate >= refresh * VsyncTolerance : fpsCap == 0 && !reduce;
        FullInterval = 1 / rate;
    }

    // An input changed (settings, pause, battery, displays): transitions it starts get the full rate for a moment.
    public void NoteInputChanged(double nowSeconds) => busyUntil = nowSeconds + InputBusySeconds;

    // Seconds between frames for a pace (Block has no interval).
    public double IntervalFor(Pace pace) => pace == Pace.Slow ? Math.Max(SlowInterval, FullInterval) : FullInterval;

    // Decides how to wait after a frame.
    public Pace Next(double nowSeconds, in FrameReport frame)
    {
        if (frame.GradientChanged || ChangeRate(previous, frame.State, frame.DtSeconds) > FastChangePerSecond)
            changedUntil = nowSeconds + ChangeHoldSeconds;
        previous = frame.State;

        if (frame.Transitioning || !frame.AllPresented) return Pace.Full;
        bool busy = nowSeconds < busyUntil || nowSeconds < changedUntil;
        Pace pace = frame.Motion switch
        {
            FrameMotion.Full => Pace.Full,
            FrameMotion.Slow => busy ? Pace.Full : Pace.Slow,
            FrameMotion.Listening => Pace.Slow,
            _ => Pace.Block,
        };
        // Nothing on screen: frames only keep the analysis going, so the next visible change starts within 100 ms.
        return pace == Pace.Full && !frame.AnyVisible ? Pace.Slow : pace;
    }

    // The fastest change of anything the glow shows, per second. Colors are left out: they reach the screen through
    // the gradient, whose refills count on their own.
    internal static float ChangeRate(in LightState a, in LightState b, float dtSeconds)
    {
        if (!(dtSeconds > 0)) return 0;
        float phase = MathF.Abs(b.Phase - a.Phase) % 1;
        float change = Max(
            Max(Delta(a.Intensity, b.Intensity), Delta(a.Visibility, b.Visibility)),
            Max(Delta(a.Pulse, b.Pulse), Delta(a.Spread, b.Spread)),
            Max(Delta(a.Ratio, b.Ratio), Delta(a.CoreThicknessDip, b.CoreThicknessDip) / 40),
            float.IsFinite(phase) ? MathF.Min(phase, 1 - phase) : 0);
        return change / dtSeconds;
    }

    private static float Delta(float a, float b)
    {
        float d = MathF.Abs(b - a);
        return float.IsFinite(d) ? d : 0;
    }

    private static float Max(float a, float b) => MathF.Max(a, b);

    private static float Max(float a, float b, float c, float d) => MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
}
