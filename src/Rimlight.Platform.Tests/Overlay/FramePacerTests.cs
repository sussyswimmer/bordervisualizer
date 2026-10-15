using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// FramePacer (K3, doc 02 "Frame pacing"): the full rate, and how the overlay thread waits after each frame.
public sealed class FramePacerTests
{
    private static readonly LightState Shown = new(default, default, 0.6f, 0.8f, 0.45f, 6, 0.1f, 0, 1);
    private static readonly LightState Hidden = Shown with { Visibility = 0 };

    [Theory]
    // fpsCap, refresh, reduce → full rate, vsync
    [InlineData(60, 60.0, false, 60.0, true)]
    [InlineData(60, 59.94, false, 59.94, true)]   // a 60 cap on 59.94 Hz is the display's rate
    [InlineData(60, 61.0, false, 60.0, true)]     // within 3 % of the refresh: paced on the display
    [InlineData(60, 62.0, false, 60.0, false)]    // further below it: the timer keeps the cap
    [InlineData(60, 144.0, false, 60.0, false)]
    [InlineData(30, 60.0, false, 30.0, false)]
    [InlineData(120, 60.0, false, 60.0, true)]    // a cap above the refresh runs at the refresh
    [InlineData(120, 144.0, false, 120.0, false)]
    [InlineData(120, 120.0, false, 120.0, true)]
    [InlineData(0, 144.0, false, 144.0, true)]    // native
    [InlineData(0, 240.0, false, 240.0, true)]
    [InlineData(0, 60.0, true, 30.0, false)]      // "Reduce on battery" caps native too
    [InlineData(120, 144.0, true, 30.0, false)]
    [InlineData(30, 30.0, true, 30.0, true)]
    [InlineData(45, 144.0, false, 60.0, false)]   // not a cap the settings offer: 60
    [InlineData(0, 0.0, false, 60.0, true)]       // unknown refresh: native follows the display at 60
    [InlineData(60, 0.0, false, 60.0, false)]     // unknown refresh: a cap uses the timer
    [InlineData(0, 5000.0, false, 60.0, true)]    // nonsense refresh counts as unknown
    public void TheFullRateFollowsTheCapTheDisplayAndTheBattery(int fpsCap, double refreshHz, bool reduce, double rate, bool vsync)
    {
        var pacer = new FramePacer();
        pacer.Configure(fpsCap, refreshHz, reduce);
        Assert.Equal(1 / rate, pacer.FullInterval, 9);
        Assert.Equal(vsync, pacer.UseVsync);
        Assert.Equal(1 / rate, pacer.IntervalFor(Pace.Full), 9);
        Assert.Equal(Math.Max(FramePacer.SlowInterval, 1 / rate), pacer.IntervalFor(Pace.Slow), 9);
    }

    // Pace is internal, so the expected pace is named.
    [Theory]
    [InlineData(FrameMotion.Full, "Full")]
    [InlineData(FrameMotion.Slow, "Slow")]
    [InlineData(FrameMotion.Listening, "Slow")]
    [InlineData(FrameMotion.Still, "Block")]
    [InlineData(FrameMotion.Observed, "Full")]
    public void EachMotionHasItsPaceOnScreen(FrameMotion motion, string expected)
    {
        var pacer = new FramePacer();
        pacer.Next(99, Report(motion, Shown)); // the light was already like this: no fast change
        Assert.Equal(expected, pacer.Next(100, Report(motion, Shown)).ToString());
    }

    [Theory]
    [InlineData(FrameMotion.Full, "Slow")]       // nothing to show: frames only listen
    [InlineData(FrameMotion.Slow, "Slow")]
    [InlineData(FrameMotion.Listening, "Slow")]
    [InlineData(FrameMotion.Still, "Block")]
    [InlineData(FrameMotion.Observed, "Full")]   // the visualizer plots every frame, shown or not (K8)
    public void NothingOnScreenAndTheLightHiddenOnlyListens(FrameMotion motion, string expected)
    {
        var pacer = new FramePacer();
        Assert.Equal(expected, pacer.Next(100, Report(motion, Hidden, anyVisible: false)).ToString());
    }

    [Theory]
    [InlineData(1.6e-3f)]   // the first frame of a 300 ms fade-in at 144 Hz
    [InlineData(5.7e-4f)]   // ... at 240 Hz
    [InlineData(1e-6f)]
    public void AFadeUnderMinVisibilityKeepsTheFullRate(float visibility)
    {
        // H-011 as C13 fixed it: with no window shown yet, only the engine's own "hidden" (exactly 0) slows to 10 Hz.
        Assert.True(visibility < GlowConstants.MinVisibility);
        var pacer = new FramePacer();
        Assert.Equal(Pace.Full, pacer.Next(100, Report(FrameMotion.Full, Shown with { Visibility = visibility }, anyVisible: false)));
        Assert.Equal(Pace.Slow, pacer.Next(100.1, Report(FrameMotion.Full, Hidden, anyVisible: false)));
        Assert.Equal(Pace.Slow, pacer.Next(100.2, Report(FrameMotion.Full, Shown with { Visibility = float.NaN }, anyVisible: false)));
    }

    [Theory]
    [InlineData(FrameMotion.Slow)]
    [InlineData(FrameMotion.Listening)]
    [InlineData(FrameMotion.Still)]
    public void AnUnfinishedPresentOrAMonitorFadeRunsAtTheFullRate(FrameMotion motion)
    {
        var pacer = new FramePacer();
        Assert.Equal(Pace.Full, pacer.Next(100, Report(motion, Hidden, anyVisible: false, transitioning: true)));
        Assert.Equal(Pace.Full, pacer.Next(100.1, Report(motion, Hidden, anyVisible: false, allPresented: false)));
    }

    [Fact]
    public void SlowMotionRunsAtTheFullRateForAMomentAfterAnInputOrAFastChange()
    {
        var pacer = new FramePacer();
        pacer.Next(9, Report(FrameMotion.Slow, Shown)); // the light appears: a fast change, held until 9.5 s
        Assert.Equal(Pace.Slow, pacer.Next(10, Report(FrameMotion.Slow, Shown)));

        pacer.NoteInputChanged(20);
        Assert.Equal(Pace.Full, pacer.Next(20.99, Report(FrameMotion.Slow, Shown)));
        Assert.Equal(Pace.Slow, pacer.Next(21.01, Report(FrameMotion.Slow, Shown)));
        // Busy doesn't keep Listening or Still frames going: nothing they show changes.
        pacer.NoteInputChanged(30);
        Assert.Equal(Pace.Slow, pacer.Next(30.1, Report(FrameMotion.Listening, Hidden, anyVisible: false)));
        Assert.Equal(Pace.Block, pacer.Next(30.2, Report(FrameMotion.Still, Hidden, anyVisible: false)));

        // A gradient refill holds the full rate for 0.5 s.
        Assert.Equal(Pace.Full, pacer.Next(40, Report(FrameMotion.Slow, Shown, gradientChanged: true)));
        Assert.Equal(Pace.Full, pacer.Next(40.49, Report(FrameMotion.Slow, Shown)));
        Assert.Equal(Pace.Slow, pacer.Next(40.51, Report(FrameMotion.Slow, Shown)));

        // A change faster than 0.25/s holds it too: 0.03 in 0.1 s is 0.3/s; 0.02 is 0.2/s.
        Assert.Equal(Pace.Full, pacer.Next(50, Report(FrameMotion.Slow, Shown with { Intensity = 0.83f }, dt: 0.1f)));
        Assert.Equal(Pace.Full, pacer.Next(50.49, Report(FrameMotion.Slow, Shown with { Intensity = 0.83f })));
        Assert.Equal(Pace.Slow, pacer.Next(50.6, Report(FrameMotion.Slow, Shown with { Intensity = 0.85f }, dt: 0.1f)));
    }

    [Fact]
    public void AGapCountsAsOneFrameAndTheCadenceStaysSteady()
    {
        // OverlayHost's dt rule (doc 02 idle pacing, C13) and its frame deadlines.
        var pacer = new FramePacer(); // 60 fps
        Assert.Equal(0.1f, pacer.FrameDt(0.1, frameDue: true));         // a 10 Hz frame
        Assert.Equal(1 / 60f, pacer.FrameDt(5, frameDue: true));        // after a long gap: one ordinary frame
        Assert.Equal(0.25f, pacer.FrameDt(FramePacer.MaxFrameDtSeconds, frameDue: true));
        Assert.Equal(1 / 60f, pacer.FrameDt(0.05, frameDue: false));    // brought forward by an input: at most one frame
        Assert.Equal(0.004f, pacer.FrameDt(0.004, frameDue: false));
        pacer.Configure(0, 144, reduce: false);
        Assert.Equal(1 / 144f, pacer.FrameDt(0.05, frameDue: false));

        Assert.Equal(20L, FramePacer.NextDeadline(11L, 10L, 10L));      // a little late: one period after the last deadline
        Assert.Equal(30L, FramePacer.NextDeadline(20L, 10L, 10L));      // a whole period late (a stall): from now
        Assert.Equal(15L, FramePacer.NextDeadline(5L, 10L, 10L));       // early (an input brought it forward): from now
        Assert.Equal(0.2, FramePacer.NextDeadline(0.1, 0.1, 0.1), 9);
    }

    [Fact]
    public void ChangeRateTakesTheFastestChangeOfAnythingThatIsDrawn()
    {
        Assert.Equal(0, FramePacer.ChangeRate(Shown, Shown, 0.1f));
        Assert.Equal(0, FramePacer.ChangeRate(Shown, Shown with { Intensity = 0 }, 0)); // no time passed
        Assert.Equal(1f, FramePacer.ChangeRate(Shown, Shown with { Intensity = 0.7f }, 0.1f), 4);
        Assert.Equal(2f, FramePacer.ChangeRate(Shown, Shown with { Visibility = 0.8f }, 0.1f), 4);
        Assert.Equal(1f, FramePacer.ChangeRate(Shown, Shown with { Pulse = 0.1f }, 0.1f), 4);
        Assert.Equal(1f, FramePacer.ChangeRate(Shown, Shown with { Spread = 0.55f }, 0.1f), 4);
        Assert.Equal(1f, FramePacer.ChangeRate(Shown, Shown with { Ratio = 0.7f }, 0.1f), 4);
        Assert.Equal(1f, FramePacer.ChangeRate(Shown, Shown with { CoreThicknessDip = 10 }, 0.1f), 4); // 4 DIP of 40
        // Phase is a loop: 0.99 → 0.01 is 0.02, not 0.98.
        Assert.Equal(0.2f, FramePacer.ChangeRate(Shown with { Phase = 0.99f }, Shown with { Phase = 1.01f }, 0.1f), 4);
        Assert.Equal(0.2f, FramePacer.ChangeRate(Shown with { Phase = 0.99f }, Shown with { Phase = 0.01f }, 0.1f), 4);
        // Colors reach the screen through the gradient, whose refills count on their own.
        Assert.Equal(0, FramePacer.ChangeRate(Shown, Shown with { ColorA = new Rgb(1, 0, 0) }, 0.1f));
        // Non-finite values never count as fast.
        Assert.Equal(0, FramePacer.ChangeRate(Shown, Shown with { Intensity = float.NaN }, 0.1f));
    }

    [Fact]
    public void PacingAllocatesNothing()
    {
        var pacer = new FramePacer();
        FrameReport report = Report(FrameMotion.Full, Shown);
        pacer.Next(0, report);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            pacer.Configure(i % 3 == 0 ? 0 : 60, 144, i % 7 == 0);
            pacer.Next(i * 0.01, report with { State = Shown with { Phase = i * 0.001f }, GradientChanged = i % 50 == 0 });
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    internal static FrameReport Report(FrameMotion motion, LightState state, bool anyVisible = true, bool transitioning = false,
        bool allPresented = true, bool gradientChanged = false, float dt = 1 / 60f, bool stalled = false) =>
        new(motion, state, dt, gradientChanged, anyVisible, transitioning, allPresented, stalled);
}
