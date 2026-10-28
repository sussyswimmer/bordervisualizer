using Rimlight.Core;
using Rimlight.Core.Lighting;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Lighting;

// H-033: the Pulse movement is the light as it was before Flow existed, bit for bit. PulseReferenceEngine is the engine
// at the base commit, copied; both run the same inputs and every field of every frame must have the same bits.
public sealed class PulseMovementTests(ITestOutputHelper output)
{
    private const float Dt = 1f / 60;

    private static readonly Settings Pulse = new() { Movement = MovementStyle.Pulse };

    // Every mode and fade the engine has, and settings it must repair.
    private static readonly Settings[] Modes =
    [
        Pulse,
        Pulse with { WhenSilent = SilentBehavior.Hide },
        Pulse with { Animation = AnimationMode.IdleGlow },
        Pulse with { Animation = AnimationMode.IdleGlow, WhenSilent = SilentBehavior.Hide },
        Pulse with { Animation = AnimationMode.Off },
        Pulse with { Enabled = false },
        Pulse with { Brightness = 0.4f, Glow = 0.9f, PrimaryRatio = 0.3f, CoreThicknessDip = 20, EdgeStyle = EdgeStyle.Line },
        Pulse with { Brightness = float.NaN, Glow = 7, PrimaryRatio = -1, CoreThicknessDip = float.PositiveInfinity },
        Pulse with { Animation = (AnimationMode)42, WhenSilent = (SilentBehavior)42 },
    ];

    private static readonly Palette[] Palettes =
    [
        Palette.Default,
        new(new Rgb(0.9f, 0.2f, 0.1f), new Rgb(0.1f, 0.3f, 0.8f), "track"),
        new(new Rgb(float.NaN, float.PositiveInfinity, -2), new Rgb(5, float.NegativeInfinity, 0.5f), null),
    ];

    private static readonly float[] OddSteps = [0, float.NaN, -1, float.PositiveInfinity, float.Epsilon, 1f / 240, 1f / 30, 0.1f, 0.37f, 2, 3600];
    private static readonly float[] OddValues = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f, 1.7f, -0f];

    private static int[] Bits(in LightState s) =>
    [
        BitConverter.SingleToInt32Bits(s.ColorA.R), BitConverter.SingleToInt32Bits(s.ColorA.G), BitConverter.SingleToInt32Bits(s.ColorA.B),
        BitConverter.SingleToInt32Bits(s.ColorB.R), BitConverter.SingleToInt32Bits(s.ColorB.G), BitConverter.SingleToInt32Bits(s.ColorB.B),
        BitConverter.SingleToInt32Bits(s.Ratio), BitConverter.SingleToInt32Bits(s.Intensity), BitConverter.SingleToInt32Bits(s.Spread),
        BitConverter.SingleToInt32Bits(s.CoreThicknessDip), BitConverter.SingleToInt32Bits(s.Phase), BitConverter.SingleToInt32Bits(s.Pulse),
        BitConverter.SingleToInt32Bits(s.Visibility),
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PulseIsTheEngineBeforeFlowBitForBit(int seed)
    {
        // Ten minutes of music at uneven frame rates: graded beats, beats that rise over several frames or repeat on
        // dt = 0 frames, silences, pauses, every mode and fade, stalls, invalid steps, levels and settings.
        var engine = new LightEngine();
        var reference = new PulseReferenceEngine();
        var random = new Random(seed);
        Settings settings = Pulse;
        Palette palette = Palette.Default;
        bool paused = false, silent = false;
        float beat = 0, ramp = 0;
        int frames = 36_000, statics = 0, beats = 0;
        for (int i = 0; i < frames; i++)
        {
            if (random.Next(150) == 0) settings = Modes[random.Next(Modes.Length)];
            if (random.Next(400) == 0) palette = Palettes[random.Next(Palettes.Length)];
            if (random.Next(paused ? 60 : 400) == 0) paused = !paused;
            if (random.Next(silent ? 150 : 300) == 0) silent = !silent;
            if (ramp > 0)
            {
                beat = MathF.Min(1, beat + ramp);         // a beat rising over several frames
                if (beat == 1) ramp = 0;
            }
            else if (random.Next(25) == 0)
            {
                beats++;
                if (random.Next(4) == 0) ramp = 0.15f + 0.2f * (float)random.NextDouble();
                else beat = MathF.Max(beat, 0.05f + 0.95f * (float)random.NextDouble());
            }
            else
            {
                beat *= MathF.Exp(-Dt / 0.12f);
            }
            float level = random.Next(300) == 0 ? OddValues[random.Next(OddValues.Length)] : (float)random.NextDouble();
            float shownBeat = random.Next(500) == 0 ? OddValues[random.Next(OddValues.Length)] : beat;
            float dt = random.Next(40) == 0 ? OddSteps[random.Next(OddSteps.Length)] : Dt * (0.7f + 0.6f * (float)random.NextDouble());
            var audio = new AudioFeatures(level, 0.5f, shownBeat, silent);

            LightState expected = reference.Update(dt, audio, palette, settings, paused);
            LightState actual = engine.Update(dt, audio, palette, settings, paused);
            Assert.True(Bits(expected).SequenceEqual(Bits(actual)), $"frame {i}: {expected} but {actual}");
            Assert.Equal(default, actual.Flow);
            Assert.Equal(reference.IsStatic, engine.IsStatic);
            if (engine.IsStatic) statics++;
        }
        output.WriteLine($"seed {seed}: {frames} frames, {beats} beats, {statics} static");
        Assert.InRange(statics, frames / 20, frames / 2);  // the hidden, static paths ran too, but mostly the light shows
    }
}
