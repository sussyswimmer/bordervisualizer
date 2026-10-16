using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;
using BenchMotion = Rimlight.Bench.FrameMotion;
using BenchPace = Rimlight.Bench.Pace;
using FramePipeline = Rimlight.Bench.FramePipeline;
using PaceModel = Rimlight.Bench.PaceModel;

namespace Rimlight.Platform.Tests;

// Rimlight.Bench (C12) times the render thread on Linux through copies of two Lane B pieces, because the app is
// Windows-only: FramePipeline copies MusicGlowSource.NextFrame and PaceModel copies FramePacer.Next, FrameDt and
// InputWaits. These tests keep the copies honest, so PERF.md's numbers and the 8-hour soak measure what the app runs
// (HANDOFF, C12's item 2).
public sealed class BenchMirrorTests
{
    [Fact]
    public void PaceModelDecidesLikeTheFramePacerForOneMonitor()
    {
        // One monitor that presents every frame it draws: what PaceModel leaves out (per-monitor fades, unfinished
        // presents, stalled surfaces, the visualizer's Observed) doesn't occur.
        var random = new Random(17);
        var pacer = new FramePacer();
        var model = new PaceModel();
        pacer.Configure(60, 60, reduce: false);
        float[] visibilities = [0, 1e-6f, 1.6e-3f, GlowConstants.MinVisibility, 0.01f, 0.5f, 1, float.NaN];
        FrameMotion[] motions = [FrameMotion.Full, FrameMotion.Slow, FrameMotion.Listening, FrameMotion.Still];
        double now = 0;
        var state = new LightState(default, default, 0.6f, 0.8f, 0.45f, 6, 0, 0, 1);
        for (int i = 0; i < 200_000; i++)
        {
            float dt = random.Next(3) switch { 0 => 1 / 60f, 1 => 0.1f, _ => (float)random.NextDouble() * 0.3f };
            now += dt;
            if (random.Next(50) == 0)
            {
                pacer.NoteInputChanged(now);
                model.NoteInputChanged(now);
            }
            state = state with
            {
                Visibility = random.Next(4) == 0 ? visibilities[random.Next(visibilities.Length)] : state.Visibility,
                Intensity = Math.Clamp(state.Intensity + (float)(random.NextDouble() - 0.5) * 0.1f, 0, 1),
                Phase = state.Phase + (random.Next(3) == 0 ? 0.01f : 0),
            };
            FrameMotion motion = motions[random.Next(motions.Length)];
            bool refilled = random.Next(20) == 0;
            bool anyVisible = state.Visibility >= GlowConstants.MinVisibility;
            Pace app = pacer.Next(now, new FrameReport(motion, state, dt, refilled, anyVisible, Transitioning: false, AllPresented: true,
                Stalled: false));
            BenchPace bench = model.Next(now, Enum.Parse<BenchMotion>(motion.ToString()), in state, dt, refilled);
            Assert.True(app.ToString() == bench.ToString(), $"frame {i}: the app paces {app}, the bench {bench} ({motion}, visibility {state.Visibility})");
        }
        Assert.Equal(FramePacer.SlowInterval, PaceModel.SlowInterval);
        Assert.Equal(FramePacer.InputBusySeconds, PaceModel.InputBusySeconds);
        Assert.Equal(FramePacer.ChangeHoldSeconds, PaceModel.ChangeHoldSeconds);
        Assert.Equal(FramePacer.FastChangePerSecond, PaceModel.FastChangePerSecond);
        Assert.Equal(GlowConstants.MinVisibility, PaceModel.MinVisibility);
    }

    [Fact]
    public void PaceModelPassesTheSameDtAsTheOverlay()
    {
        // The soak's dt comes from PaceModel.FrameDt, the overlay's from FramePacer.FrameDt.
        var pacer = new FramePacer();
        foreach ((int cap, double refresh, bool reduce) in new[] { (60, 60.0, false), (0, 144.0, false), (0, 240.0, true) })
        {
            pacer.Configure(cap, refresh, reduce);
            foreach (double elapsed in new[] { 0, 0.001, 1 / 240.0, 1 / 60.0, 0.05, 0.1, 0.2499, 0.25, 0.2501, 5 })
            {
                Assert.Equal(pacer.FrameDt(elapsed, frameDue: true), PaceModel.FrameDt(elapsed, pacer.FullInterval, due: true));
                Assert.Equal(pacer.FrameDt(elapsed, frameDue: false), PaceModel.FrameDt(elapsed, pacer.FullInterval, due: false));
            }
        }
        Assert.Equal(FramePacer.MaxFrameDtSeconds, PaceModel.MaxFrameDtSeconds);
    }

    [Fact]
    public void PaceModelHoldsInputsLikeTheOverlay()
    {
        // The soak's NextFrameTime asks PaceModel.InputWaits whether an input brings a frame forward, the overlay loop
        // asks FramePacer.InputWaits (K3's input cap).
        var pacer = new FramePacer();
        foreach ((int cap, double refresh, bool reduce) in new[] { (60, 60.0, false), (0, 144.0, false), (0, 240.0, true) })
        {
            pacer.Configure(cap, refresh, reduce);
            double full = pacer.FullInterval;
            foreach (Pace pace in Enum.GetValues<Pace>())
            {
                foreach (double elapsed in new[] { 0, 0.001, full * 0.5, Math.BitDecrement(full), full, Math.BitIncrement(full), 0.05, 0.1, 5 })
                {
                    Assert.True(pacer.InputWaits(pace, elapsed) == PaceModel.InputWaits(Enum.Parse<BenchPace>(pace.ToString()), elapsed, full),
                        $"{pace} at {elapsed} s, full interval {full} s");
                }
            }
        }
    }

    [Fact]
    public void FramePipelineRunsTheSameFramesAsMusicGlowSource()
    {
        // The same audio, settings snapshots, album palettes and pauses through both: every light state, refill,
        // gradient and motion must match bit for bit. (FramePipeline leaves out what the soak doesn't use: Settings'
        // audio meter, the visualizer, the demo, and manual color edits.)
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var pipeline = new FramePipeline(settings, App.Overlay.MusicGlowSource.ManualPalette(settings));
        var album = new Palette(new Rgb(0.9f, 0.2f, 0.1f), new Rgb(0.1f, 0.3f, 0.9f), "a");
        var next = new Palette(new Rgb(0.2f, 0.9f, 0.3f), new Rgb(0.9f, 0.8f, 0.1f), "b");

        // (start, what changes)
        var script = new (double At, Action Change)[]
        {
            (3, () => rig.PublishAlbum(album)),
            (6, () => settings = settings with { Sensitivity = 1.6f }),
            (9, () => rig.Program = AudioProgram.NoPackets),
            (14, () => settings = settings with { WhenSilent = SilentBehavior.Hide }),
            (18, () => rig.Program = AudioProgram.Music),
            (20, () => rig.PublishAlbum(next)),
            (21, () => settings = settings with { OverrideAlbumColor = true }),
            (23, () => settings = settings with { Animation = AnimationMode.Off }),
            (25, () => settings = settings with { Animation = AnimationMode.IdleGlow, OverrideAlbumColor = false }),
            (28, () => settings = settings with { Animation = AnimationMode.MusicSync, Brightness = 0.5f }),
            (30, () => rig.Program = AudioProgram.Silence),
            (33, () => rig.Program = AudioProgram.Music),
            (34, () => rig.PublishAlbum(null)),
            (36, () => settings = settings with { Enabled = false }),
            (38, () => settings = settings with { Enabled = true, PrimaryRatio = 0.4f }),
        };
        var random = new Random(23);
        double now = 0;
        int step = 0, frames = 0;
        bool paused = false;
        while (now < 45)
        {
            while (step < script.Length && script[step].At <= now) script[step++].Change();
            paused = now is >= 40 and < 42;
            float dt = random.Next(10) == 0 ? 0.1f : random.Next(2) == 0 ? 1 / 60f : 1 / 144f;
            now += dt;

            OverlayFrame app = rig.Frame(now, dt, settings, paused);
            BenchMotion motion = pipeline.NextFrame(rig.Delivered, GlowRig.SampleRate, dt, settings, rig.Media.AlbumPalette, paused,
                rig.StreamChanged);
            frames++;

            Assert.True(app.State == pipeline.State, $"{now:0.000} s: the app drew {app.State}, the bench {pipeline.State}");
            Assert.Equal(app.GradientChanged, pipeline.Refilled);
            Assert.Equal(app.Motion.ToString(), motion.ToString());
            Assert.True(rig.Gradient.AsSpan().SequenceEqual(pipeline.Gradient), $"{now:0.000} s: the gradients differ");
        }
        Assert.True(frames > 2000, $"{frames} frames");
    }
}
