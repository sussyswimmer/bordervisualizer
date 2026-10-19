using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Tests;

// The Mac overlay loop's decisions, run as the Swift render thread runs them: begin, a plan per screen, done, end.
public sealed class MacOverlayLoopTests
{
    private const double Frame = 1 / 60.0;

    [Fact]
    public void TheDemoShowsEveryScreenAndRunsAtTheFullRate()
    {
        var loop = Started(new Settings(), demo: true, screens: 2);
        Run(loop, seconds: 0.5, out PaceInfo pace, out int shows, out int draws);
        Assert.Equal(2, shows);   // each screen's window is shown once
        Assert.True(draws > 40);  // and keeps drawing: the beat moves every frame
        Assert.Equal(0, pace.Pace);
        Assert.Equal(60, pace.FullRateHz, 3);
    }

    [Fact]
    public void GlowOffFadesOutHidesTheWindowsAndStops()
    {
        var loop = Started(new Settings { Animation = AnimationMode.IdleGlow }, demo: false, screens: 1);
        Run(loop, seconds: 1, out _, out int shows, out _);
        Assert.Equal(1, shows);

        loop.ApplySettings(loop.Settings with { Enabled = false });
        int hides = 0;
        PaceInfo pace = default;
        for (int i = 0; i < 120 && pace.Pace != 2; i++)
        {
            Step(loop, out pace, out _, out _, out int hidden);
            hides += hidden;
        }
        Assert.Equal(1, hides);
        Assert.Equal(2, pace.Pace); // nothing left to draw: no frames until an input
    }

    [Fact]
    public void AStaticGlowIsNotPresentedAgain()
    {
        // Idle Glow at a constant brightness would be the same pixels: the plan says None once it is on screen.
        var loop = Started(new Settings { Animation = AnimationMode.IdleGlow }, demo: false, screens: 1);
        Run(loop, seconds: 1, out _, out _, out _);
        loop.BeginFrame((float)Frame, [], 0);
        SurfaceAction action = loop.PlanSurface(0, out _);
        loop.SurfaceDone(0, presented: action != SurfaceAction.None, 0);
        loop.EndFrame(now);
        loop.BeginFrame(0, [], 0); // no time passes: nothing can change
        Assert.Equal(SurfaceAction.None, loop.PlanSurface(0, out _));
    }

    [Fact]
    public void APausedScreenFadesOutOverThreeHundredMilliseconds()
    {
        var loop = Started(new Settings(), demo: true, screens: 2);
        Run(loop, seconds: 0.5, out _, out _, out _);
        loop.SetSurfaces([Info(paused: true), Info()]);
        int frames = 0;
        bool hidden = false;
        while (!hidden && frames < 60)
        {
            loop.BeginFrame((float)Frame, [], 0);
            SurfaceAction a = loop.PlanSurface(0, out _);
            hidden = a != SurfaceAction.None && loop.SurfaceDone(0, true, 0);
            loop.PlanSurface(1, out _);
            loop.SurfaceDone(1, true, 0);
            loop.EndFrame(now += Frame);
            frames++;
        }
        Assert.True(hidden);
        Assert.InRange(frames, 17, 20); // 0.3 s at 60 fps, then the transparent frame
    }

    [Fact]
    public void ASleepingScreenIsStalledAfterASecondAndStopsHoldingTheFullRate()
    {
        var loop = Started(new Settings { Animation = AnimationMode.IdleGlow }, demo: false, screens: 1);
        Run(loop, seconds: 1, out _, out _, out _);
        long ms = 10_000;
        PaceInfo pace = default;
        for (int i = 0; i < 90; i++, ms += 16)
        {
            loop.BeginFrame((float)Frame, [], 0);
            loop.PlanSurface(0, out _);
            loop.SurfaceDone(0, presented: false, ms);
            pace = loop.EndFrame(now += Frame);
        }
        Assert.NotEqual(0, pace.Pace);
    }

    [Fact]
    public void AFrameAllocatesNothing()
    {
        var loop = Started(new Settings(), demo: true, screens: 3);
        float[] samples = new float[800];
        Run(loop, seconds: 7, out _, out _, out _); // past a palette change, so the crossfade path is warm too
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            loop.ApplyInputs(now);
            loop.BeginFrame((float)Frame, samples, 48000);
            for (int s = 0; s < 3; s++)
            {
                if (loop.PlanSurface(s, out _) != SurfaceAction.None) loop.SurfaceDone(s, true, i);
            }
            loop.EndFrame(now += Frame);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ABatteryWithReduceCapsAtThirtyAndHalvesTheScale()
    {
        var loop = Started(new Settings(), demo: true, screens: 1);
        loop.SetDisplay(120, onBattery: true);
        loop.ApplyInputs(now);
        FrameInfo info = loop.BeginFrame((float)Frame, [], 0);
        loop.PlanSurface(0, out _);
        loop.SurfaceDone(0, true, 0);
        Assert.Equal(0.5f, info.RenderScale);
        Assert.Equal(30, loop.EndFrame(now).FullRateHz, 3);
    }

    [Fact]
    public void SettingsArePublishedToTheRenderThreadOnItsNextInputs()
    {
        var loop = Started(new Settings(), demo: false, screens: 1);
        Assert.False(loop.ApplyInputs(now));
        loop.ApplySettings(loop.Settings with { FpsCap = 30 });
        Assert.True(loop.ApplyInputs(now));
        loop.BeginFrame((float)Frame, [], 0);
        Assert.Equal(30, loop.EndFrame(now).FullRateHz, 3);
    }

    private double now = 100;

    private MacOverlayLoop Started(Settings settings, bool demo, int screens)
    {
        var loop = new MacOverlayLoop(settings);
        loop.SetDemo(demo);
        loop.SetDisplay(60, onBattery: false);
        SurfaceInfo[] infos = new SurfaceInfo[screens];
        for (int i = 0; i < screens; i++) infos[i] = Info();
        loop.SetSurfaces(infos);
        loop.ApplyInputs(now);
        return loop;
    }

    private static SurfaceInfo Info(bool paused = false) =>
        new() { WidthPx = 1920, HeightPx = 1080, PixelsPerPoint = 1, Paused = paused ? 1 : 0 };

    private void Run(MacOverlayLoop loop, double seconds, out PaceInfo pace, out int shows, out int draws)
    {
        pace = default;
        shows = draws = 0;
        for (double t = 0; t < seconds; t += Frame)
        {
            Step(loop, out pace, out int s, out int d, out _);
            shows += s;
            draws += d;
        }
    }

    // One frame on every screen, everything presented: how many windows were shown, drawn and hidden.
    private void Step(MacOverlayLoop loop, out PaceInfo pace, out int shows, out int draws, out int hides)
    {
        loop.ApplyInputs(now);
        loop.BeginFrame((float)Frame, [], 0);
        shows = draws = hides = 0;
        for (int i = 0; i < loop.SurfaceCount; i++)
        {
            SurfaceAction action = loop.PlanSurface(i, out _);
            if (action == SurfaceAction.None) continue;
            if (action == SurfaceAction.ShowAndDraw) shows++;
            draws++;
            if (loop.SurfaceDone(i, presented: true, 0)) hides++;
        }
        now += Frame;
        pace = loop.EndFrame(now);
    }
}
