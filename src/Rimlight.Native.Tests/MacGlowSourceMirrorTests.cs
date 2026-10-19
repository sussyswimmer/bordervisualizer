using Rimlight.Core;
using Rimlight.Native.Overlay;
using BenchPipeline = Rimlight.Bench.FramePipeline;

namespace Rimlight.Native.Tests;

// The Mac's frame source runs the same frames as the Windows one. Rimlight.Platform.Tests' BenchMirrorTests holds
// Bench's FramePipeline equal to MusicGlowSource; this holds MacGlowSource equal to FramePipeline, bit for bit, on the
// same audio, settings, album palettes and pauses (docs/10-MACOS.md §1.4). Like FramePipeline it leaves out the demo
// and Settings' meter.
public sealed class MacGlowSourceMirrorTests
{
    private const int SampleRate = 48000;

    private enum Program { Music, Silence, NoPackets }

    [Fact]
    public void TheMacRunsTheSameFramesAsTheWindowsFrameSource()
    {
        var settings = new Settings();
        var mac = new MacGlowSource(settings);
        var bench = new BenchPipeline(settings, MacGlowSource.ManualPalette(settings));
        float[] gradient = new float[256];
        var album = new Palette(new Rgb(0.9f, 0.2f, 0.1f), new Rgb(0.1f, 0.3f, 0.9f), "a");
        var next = new Palette(new Rgb(0.2f, 0.9f, 0.3f), new Rgb(0.9f, 0.8f, 0.1f), "b");
        Palette? published = null;
        Program program = Program.Music;

        var script = new (double At, Action Change)[]
        {
            (3, () => published = album),
            (6, () => settings = settings with { Sensitivity = 1.6f }),
            (9, () => program = Program.NoPackets),
            (14, () => settings = settings with { WhenSilent = SilentBehavior.Hide }),
            (18, () => program = Program.Music),
            (20, () => published = next),
            (21, () => settings = settings with { OverrideAlbumColor = true }),
            (23, () => settings = settings with { Animation = AnimationMode.Off }),
            (25, () => settings = settings with { Animation = AnimationMode.IdleGlow, OverrideAlbumColor = false }),
            (28, () => settings = settings with { Animation = AnimationMode.MusicSync, Brightness = 0.5f }),
            (30, () => program = Program.Silence),
            (33, () => program = Program.Music),
            (34, () => published = null),
            (36, () => settings = settings with { Enabled = false }),
            (38, () => settings = settings with { Enabled = true, PrimaryRatio = 0.4f }),
        };

        var random = new Random(23);
        float[] buffer = new float[SampleRate];
        double now = 0, audioTime = 0;
        int step = 0, frames = 0, generation = 0, pulses = 0;
        bool capturing = false;
        while (now < 45)
        {
            while (step < script.Length && script[step].At <= now) script[step++].Change();
            bool paused = now is >= 40 and < 42;
            float dt = random.Next(10) == 0 ? 0.1f : random.Next(2) == 0 ? 1 / 60f : 1 / 144f;
            now += dt;

            // The capture runs while the glow listens, as on Windows; opening or closing it starts the analysis over.
            bool listen = BenchPipeline.Listens(settings, paused);
            bool changed = listen != capturing;
            if (changed)
            {
                capturing = listen;
                generation++;
            }
            int count = 0;
            if (capturing && program != Program.NoPackets)
            {
                count = (int)Math.Round(dt * SampleRate);
                for (int i = 0; i < count; i++, audioTime += 1.0 / SampleRate)
                    buffer[i] = program == Program.Music ? Music(audioTime, random) : 0;
            }
            int rate = capturing ? SampleRate : BenchPipeline.FallbackSampleRate;
            ReadOnlySpan<float> samples = buffer.AsSpan(0, count);

            mac.PublishAlbum(published);
            var macFrame = mac.NextFrame(dt, settings, paused, gradient, samples, capturing ? SampleRate : 0, generation);
            var benchMotion = bench.NextFrame(samples, rate, dt, settings, published, paused, changed);
            frames++;
            if (macFrame.State.Pulse > 0.3f) pulses++;

            Assert.True(macFrame.State == bench.State, $"{now:0.000} s: the Mac drew {macFrame.State}, Windows {bench.State}");
            Assert.Equal(bench.Refilled, macFrame.GradientChanged);
            Assert.Equal(benchMotion.ToString(), macFrame.Motion.ToString());
            Assert.True(gradient.AsSpan().SequenceEqual(bench.Gradient), $"{now:0.000} s: the gradients differ");
        }
        Assert.True(frames > 2000, $"{frames} frames");
        Assert.True(pulses > 100, $"only {pulses} frames pulsed: the music never reached the light");
    }

    // A 120 BPM kick (a decaying 55 Hz burst) over a quiet chord and noise: enough for beats and a groove.
    private static float Music(double t, Random random)
    {
        double sinceBeat = t % 0.5;
        double kick = Math.Exp(-sinceBeat / 0.08) * Math.Sin(2 * Math.PI * 55 * sinceBeat);
        double chord = 0.08 * (Math.Sin(2 * Math.PI * 220 * t) + Math.Sin(2 * Math.PI * 277 * t));
        double noise = 0.02 * (random.NextDouble() * 2 - 1);
        return (float)(0.6 * kick + chord + noise);
    }
}
