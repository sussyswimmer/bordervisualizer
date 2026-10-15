using Rimlight.App.Visualizer;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Platform.Tests;

// K10: the integrated render loop on a simulated clock. OverlayLoopModel mirrors OverlayHost's frame loop and drives the
// real FramePacer and the app's MusicGlowSource, which runs the real analyzer, light engine and blender from CoreFactory
// on a synthetic 124 BPM track. These are the K3 steady states and timings, now with the real Core instead of K3's
// stand-in engine.
public sealed class PacingTimelineTests(ITestOutputHelper output)
{
    private const string Primary = @"\\.\DISPLAY1";
    private const string Secondary = @"\\.\DISPLAY2";
    private static readonly (string, int, int, float)[] OneMonitor = [(Primary, 1920, 1080, 1f)];
    private static readonly (string, int, int, float)[] TwoMonitors = [(Primary, 2560, 1440, 1.25f), (Secondary, 1920, 1080, 1f)];

    [Fact]
    public void TheWholeDayReachesEverySteadyStateOnTime()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var model = new OverlayLoopModel(rig, settings, OneMonitor);
        Settings hide = settings with { WhenSilent = SilentBehavior.Hide };
        Settings off = hide with { Animation = AnimationMode.Off };
        Settings disabled = hide with { Enabled = false };
        Settings pauseOnBattery = hide with { OnBattery = BatteryBehavior.Pause };
        Settings idleMode = hide with { Animation = AnimationMode.IdleGlow };

        model.At(8, () => rig.Program = AudioProgram.NoPackets);   // the music stops
        model.At(18, () => model.ApplySettings(hide));              // When it's quiet: Hide the glow
        model.At(26.03, () => rig.Program = AudioProgram.Music);    // the music comes back
        model.At(32.005, () => model.ApplySettings(off));       // between two 60 Hz frames
        model.At(35, () => model.ApplySettings(hide));
        model.At(38.005, () => model.ApplySettings(disabled));
        model.At(41, () => model.ApplySettings(hide));
        model.At(44.005, () => model.SetPaused(true));              // locked, display off, ...
        model.At(47, () => model.SetPaused(false));
        model.At(50, () => model.SetPausedMonitors(Primary));       // a fullscreen app on the only monitor
        model.At(53, () => model.SetPausedMonitors());
        model.At(56, () => model.SetOnBattery(true));               // "Save power": 30 fps at half scale
        model.At(60, () => model.ApplySettings(pauseOnBattery));
        model.At(63, () => model.ApplySettings(idleMode));          // still on battery, Idle Glow mode, Save power
        model.At(66, () => model.SetOnBattery(false));
        model.At(70.05, () => model.ApplySettings(off));            // between two 10 Hz frames
        model.RunUntil(71);

        Steady(model, 4, 8, 60, 60, captureOpen: true, windows: 1, "music");
        Steady(model, 14, 18, 10, 10, captureOpen: true, windows: 1, "silence, Idle Glow");
        Steady(model, 22, 26, 10, 0, captureOpen: true, windows: 0, "silence, hidden, listening");
        Steady(model, 28, 32, 60, 60, captureOpen: true, windows: 1, "music again");
        Steady(model, 33, 35, 0, 0, captureOpen: false, windows: 0, "Off");
        Steady(model, 36, 38, 60, 60, captureOpen: true, windows: 1, "back from Off");
        Steady(model, 39, 41, 0, 0, captureOpen: false, windows: 0, "glow turned off");
        Steady(model, 42, 44, 60, 60, captureOpen: true, windows: 1, "glow on");
        Steady(model, 45, 47, 0, 0, captureOpen: false, windows: 0, "paused everywhere");
        Steady(model, 48, 50, 60, 60, captureOpen: true, windows: 1, "resumed");
        Steady(model, 51, 53, 0, 0, captureOpen: false, windows: 0, "its only monitor paused");
        Steady(model, 54, 56, 60, 60, captureOpen: true, windows: 1, "monitor resumed");
        Steady(model, 57, 60, 30, 30, captureOpen: true, windows: 1, "on battery, Save power");
        Assert.True(model.Frames.Where(f => f.Time is >= 57 and < 60).All(f => f.HalfScale), "half scale on battery");
        Steady(model, 61, 63, 0, 0, captureOpen: false, windows: 0, "on battery, Pause the glow");
        Steady(model, 64, 66, 10, 10, captureOpen: false, windows: 1, "Idle Glow mode on battery");
        Steady(model, 67, 70, 10, 10, captureOpen: false, windows: 1, "Idle Glow mode");
        Steady(model, 70.5, 71, 0, 0, captureOpen: false, windows: 0, "Off from Idle Glow");

        // Music back from Hide: the light is on screen within about one 10 Hz listening poll of the first packet.
        ModelFrame back = model.Frames.First(f => f.Time > 26.03 && f.AnyVisible);
        output.WriteLine($"Visible again {1000 * (back.Time - 26.03):0} ms after the music came back");
        Assert.InRange(back.Time - 26.03, 0, 0.115);

        // An input wakes the loop at once, and the fade it starts begins on that frame: with the time since the last frame,
        // or one full-rate frame's worth when it comes between 10 Hz frames, never the whole gap.
        foreach (double input in new[] { 32.005, 38.005, 44.005, 70.05 })
        {
            ModelFrame before = model.Frames.Last(f => f.Time < input);
            ModelFrame woken = model.Frames.First(f => f.Time >= input);
            Assert.Equal(input, woken.Time, 9);
            Assert.Equal((float)Math.Min(input - before.Time, 1 / 60.0), woken.Dt, 5);
            Assert.True(woken.State.Visibility < before.State.Visibility, $"fade starts on the first frame after {input} s");
            // Off and pause end in Block. The fade reaches 0 after 300 ms; with nothing on screen the next frame is a
            // 10 Hz one, where the engine reports static (from the frame after Visibility 0, H-011), and the loop stops.
            ModelFrame last = model.Frames.Last(f => f.Time < input + 0.9);
            output.WriteLine($"Input at {input} s: dt {1000 * woken.Dt:0.0} ms, frames stop {1000 * (last.Time - input):0} ms later");
            Assert.InRange(last.Time - input, 0.3, 0.42);
            Assert.Equal(Pace.Block, last.Pace);
        }

        // A monitor pause fades its overlay out over 300 ms before the engine pauses and the window hides.
        ModelFrame hidden = model.Frames.First(f => f.Time >= 50 && f.WindowsShown == 0);
        Assert.InRange(hidden.Time - 50, 0.28, 0.45);

        // dt never jumps: one ordinary frame after every gap (10 Hz frames are 0.1 s).
        Assert.All(model.Frames.Skip(1), f => Assert.InRange(f.Dt, 0, 0.1001f));
    }

    [Fact]
    public void APausedMonitorFadesAloneWhileTheOtherKeepsTheMusic()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var model = new OverlayLoopModel(rig, settings, TwoMonitors);
        model.At(3, () => model.SetPausedMonitors(Secondary));
        model.At(6, () => model.SetPausedMonitors());
        model.RunUntil(9);

        Steady(model, 1, 3, 60, 120, captureOpen: true, windows: 2, "two monitors");
        Steady(model, 4, 6, 60, 60, captureOpen: true, windows: 1, "one paused");
        Steady(model, 7, 9, 60, 120, captureOpen: true, windows: 2, "both again");
        ModelFrame hidden = model.Frames.First(f => f.Time >= 3 && f.WindowsShown == 1);
        Assert.InRange(hidden.Time - 3, 0.28, 0.35);
    }

    [Fact]
    public void SettingsAudioAndTheVisualizerKeepTheAnalyzerRunningWithoutDrawing()
    {
        Settings off = new() { Animation = AnimationMode.Off };
        using var rig = new GlowRig(off);
        var model = new OverlayLoopModel(rig, off, OneMonitor);
        var feed = new VisualizerFeed();
        model.At(2, () =>
        {
            rig.Source.SetAudioWanted(true); // Settings shows the level meter
            model.RequestFrame();
        });
        model.At(5, () =>
        {
            rig.Source.SetVisualizer(feed); // the debug visualizer opens
            model.RequestFrame();
        });
        model.At(8, () =>
        {
            rig.Source.SetVisualizer(null);
            rig.Source.SetAudioWanted(false);
            model.RequestFrame();
        });
        model.RunUntil(11);

        Steady(model, 0.5, 2, 0, 0, captureOpen: false, windows: 0, "Off");
        Steady(model, 3, 5, 10, 0, captureOpen: true, windows: 0, "Off, Settings shows the audio");
        Steady(model, 6, 8, 60, 0, captureOpen: true, windows: 0, "Off, visualizer open");
        Steady(model, 9, 11, 0, 0, captureOpen: false, windows: 0, "Off again");
        Assert.True(feed.TryTake(out AnalyzerSnapshot snapshot));
        Assert.True(snapshot.Sequence > 100);
        Assert.False(snapshot.Features.IsSilent); // the analyzer heard the music while the glow stayed off
    }

    [Fact]
    public void DemoModeDrawsAtTheFullRateWithTheCaptureClosed()
    {
        Settings off = new() { Enabled = false };
        using var rig = new GlowRig(off);
        var model = new OverlayLoopModel(rig, off, OneMonitor);
        model.At(1, () =>
        {
            rig.Source.SetDemo(true);
            model.RequestFrame();
        });
        model.At(14, () =>
        {
            rig.Source.SetDemo(false);
            model.RequestFrame();
        });
        model.RunUntil(16);

        Steady(model, 2, 14, 60, 60, captureOpen: false, windows: 1, "demo with the glow off");
        Steady(model, 15, 16, 0, 0, captureOpen: false, windows: 0, "demo over, glow off");
    }

    [Theory]
    [InlineData(60, 60.0)]
    [InlineData(0, 144.0)]
    [InlineData(0, 240.0)]
    public void ResumingFadesInFromTheFirstFrameAtAnyRate(int fpsCap, double refreshHz)
    {
        // C13's review case: above about 130 Hz the first frame of the 300 ms fade-in is under MinVisibility, and the
        // pacer must keep the full rate for it (H-011).
        var settings = new Settings { FpsCap = fpsCap };
        using var rig = new GlowRig(settings);
        var model = new OverlayLoopModel(rig, settings, OneMonitor, refreshHz);
        model.At(2, () => model.SetPaused(true));
        model.At(4, () => model.SetPaused(false));
        model.RunUntil(5);

        ModelFrame[] fadeIn = model.Frames.Where(f => f.Time >= 4 && f.Time < 4.4).ToArray();
        double interval = 1 / refreshHz;
        Assert.True(fadeIn.Length >= (int)(0.35 / interval), $"{fadeIn.Length} frames in the fade-in");
        for (int i = 1; i < fadeIn.Length; i++)
        {
            Assert.InRange(fadeIn[i].Time - fadeIn[i - 1].Time, 0, interval * 1.01);
            Assert.InRange(fadeIn[i].State.Visibility - fadeIn[i - 1].State.Visibility, -0.01f, 0.1f);
        }
        Assert.True(fadeIn.First(f => f.AnyVisible).Time - 4 < 3 * interval);
        Assert.Equal(1f, fadeIn[^1].State.Visibility, 3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.15)]
    [InlineData(0.25)]
    public void TheGlowFadesInAtStartEvenAfterASlowFirstFrame(double gpuSetupSeconds)
    {
        // C13's review case: the GPU is set up during the first frame, so the second one comes late. The glow must still
        // fade in over 300 ms instead of popping in at a quarter brightness.
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var model = new OverlayLoopModel(rig, settings, OneMonitor, firstFrameSeconds: gpuSetupSeconds);
        model.RunUntil(1);

        float previous = 0;
        foreach (ModelFrame frame in model.Frames.Where(f => f.Time < 0.6))
        {
            Assert.InRange(frame.State.Visibility - previous, -0.01f, 0.1f);
            previous = frame.State.Visibility;
        }
        Assert.Equal(1f, model.Frames.Last().State.Visibility, 3);
    }

    // Frames and presents per second over [from, to), and the capture and the shown windows throughout (as the last frame
    // before the span left them, when no frame comes in it). Idle Glow's breathing slows near its peaks, where a 10 fps
    // frame can find the screen already showing it (within half a code value), so it presents a little less often.
    private void Steady(OverlayLoopModel model, double from, double to, double fps, double presents, bool captureOpen, int windows,
        string what)
    {
        double measuredFps = model.FramesPerSecond(from, to);
        double measuredPresents = model.PresentsPerSecond(from, to);
        output.WriteLine($"{what,-36} {measuredFps,5:0.0} frames/s {measuredPresents,6:0.0} presents/s");
        double oneFrame = 1.01 / (to - from); // a frame on the span's edge may fall either side
        bool breathing = fps == 10 && presents > 0;
        Assert.InRange(measuredFps, fps - oneFrame, fps + oneFrame);
        Assert.InRange(measuredPresents, (breathing ? 0.7 : 1) * presents - windows * oneFrame, presents + windows * oneFrame);
        ModelFrame[] frames = [.. model.Frames.Where(f => f.Time >= from && f.Time < to)];
        if (frames.Length == 0) frames = [model.Frames.Last(f => f.Time < to)];
        Assert.All(frames, f => Assert.Equal(captureOpen, f.CaptureOpen));
        Assert.All(frames, f => Assert.Equal(windows, f.WindowsShown));
    }
}
