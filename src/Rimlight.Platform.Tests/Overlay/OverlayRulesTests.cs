using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// OverlayRules (K3, K10): what "On battery" does, the per-monitor pause fades and when the glow is paused everywhere.
public sealed class OverlayRulesTests
{
    [Fact]
    public void AMonitorFadesOutAndBackInOver300Ms()
    {
        float fade = 1;
        int frames = 0;
        while (OverlayRules.AdvanceFade(ref fade, paused: true, 1 / 60f)) frames++;
        Assert.Equal(0, fade);
        Assert.InRange(frames, 18, 19); // 300 ms of 1/60 s steps (float rounding can leave a last tiny one)
        Assert.False(OverlayRules.AdvanceFade(ref fade, paused: true, 1 / 60f));

        Assert.True(OverlayRules.AdvanceFade(ref fade, paused: false, 0.15f));
        Assert.Equal(0.5f, fade, 5);
        Assert.True(OverlayRules.AdvanceFade(ref fade, paused: false, 0.25f)); // the step that arrives still counts
        Assert.Equal(1, fade);
        Assert.False(OverlayRules.AdvanceFade(ref fade, paused: false, 0.25f));
    }

    [Fact]
    public void TheGlowIsPausedEverywhereByAPauseTheBatteryEveryMonitorOrNoOverlay()
    {
        var settings = new Settings();
        Settings pauseOnBattery = settings with { OnBattery = BatteryBehavior.Pause };
        Settings reduceOnBattery = settings with { OnBattery = BatteryBehavior.Reduce };
        var shown = new Monitor(false, 1);
        var fading = new Monitor(true, 0.4f);
        var fadedOut = new Monitor(true, 0);

        Assert.False(OverlayRules.PausedEverywhere(false, settings, false, new List<Monitor> { shown, fadedOut }));
        Assert.False(OverlayRules.PausedEverywhere(false, settings, false, new List<Monitor> { fading, fadedOut }));
        Assert.True(OverlayRules.PausedEverywhere(false, settings, false, new List<Monitor> { fadedOut, fadedOut }));
        Assert.True(OverlayRules.PausedEverywhere(true, settings, false, new List<Monitor> { shown }));
        Assert.True(OverlayRules.PausedEverywhere(false, pauseOnBattery, true, new List<Monitor> { shown }));
        Assert.False(OverlayRules.PausedEverywhere(false, pauseOnBattery, false, new List<Monitor> { shown }));
        Assert.False(OverlayRules.PausedEverywhere(false, reduceOnBattery, true, new List<Monitor> { shown }));
        // No display chosen, or none of the chosen ones attached: nothing could show the glow, so the capture closes.
        Assert.True(OverlayRules.PausedEverywhere(false, settings, false, new List<Monitor>()));

        Assert.True(OverlayRules.Reduced(reduceOnBattery, onBattery: true));
        Assert.False(OverlayRules.Reduced(reduceOnBattery, onBattery: false));
        Assert.False(OverlayRules.Reduced(pauseOnBattery, onBattery: true));
        Assert.False(OverlayRules.Reduced(settings with { OnBattery = BatteryBehavior.Normal }, onBattery: true));
    }

    [Fact]
    public void TheRulesAllocateNothing()
    {
        var settings = new Settings();
        var overlays = new List<Monitor> { new(true, 0), new(true, 0) };
        float fade = 1;
        OverlayRules.PausedEverywhere(false, settings, false, overlays);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool paused = false;
        for (int i = 0; i < 10_000; i++)
        {
            paused ^= OverlayRules.PausedEverywhere(false, settings, i % 2 == 0, overlays);
            OverlayRules.AdvanceFade(ref fade, i % 40 < 20, 1 / 60f);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Monitor(bool paused, float fade) : IMonitorFade
    {
        public bool Paused { get; } = paused;
        public float Fade { get; } = fade;
    }
}
