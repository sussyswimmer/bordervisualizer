using Rimlight.Core;
using Rimlight.Core.Audio;
using Rimlight.Core.Lighting;
using Rimlight.Tests.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Lighting;

// C6: the real ILightEngine (doc 07 Phase 3, doc 01 §2, doc 04 §4, doc 02 idle optimization, H-007, H-008 item 2).
// Expected values are the spec's numbers written out, not the engine's constants, so changing a constant fails a test.
public sealed class LightEngineTests(ITestOutputHelper output)
{
    private const float Dt = 1f / 60;
    private static readonly Settings Music = new();                                       // Music Sync; silence → Idle Glow
    private static readonly Settings Hide = new() { WhenSilent = SilentBehavior.Hide };
    private static readonly Settings Idle = new() { Animation = AnimationMode.IdleGlow };
    private static readonly Settings Off = new() { Animation = AnimationMode.Off };
    private static readonly Settings Disabled = new() { Enabled = false };
    private static readonly AudioFeatures Loud = new(0.5f, 0.5f, 0f, false);
    private static readonly AudioFeatures Quiet = new(0f, 0f, 0f, false);             // a quiet passage before IsSilent
    private static readonly AudioFeatures Silent = new(0f, 0f, 0f, true);

    // Default Brightness 0.8: Music Sync at Level 0.5 and 0, and the Idle Glow breathing range 0.9 × 0.8 × (1 ± 0.1).
    private const float LoudIntensity = 0.8f * (0.35f + 0.65f * 0.5f);
    private const float QuietIntensity = 0.8f * 0.35f;
    private const float IdleLow = 0.8f * 0.9f * 0.9f, IdleHigh = 0.8f * 0.9f * 1.1f;

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    private static List<LightState> Frames(LightEngine engine, int count, AudioFeatures audio, Settings settings, bool paused = false, float dt = Dt)
    {
        var states = new List<LightState>(count);
        for (int i = 0; i < count; i++) states.Add(engine.Update(dt, audio, Palette.Default, settings, paused));
        return states;
    }

    private static LightState Run(LightEngine engine, float seconds, AudioFeatures audio, Settings settings, bool paused = false) =>
        Frames(engine, (int)MathF.Round(seconds / Dt), audio, settings, paused)[^1];

    private static LightEngine Settled(Settings settings, AudioFeatures audio, bool paused = false)
    {
        var engine = new LightEngine();
        Run(engine, 3, audio, settings, paused);
        return engine;
    }

    // Phase movement in cycles, unwrapped: a frame never moves it by half a cycle.
    private static double Advance(float from, float to)
    {
        double d = to - from;
        return d < -0.5 ? d + 1 : d;
    }

    // The ways to hide the light. Each is undone by Music Sync (or Hide) with sound, unpaused.
    private static (Settings Settings, AudioFeatures Audio, bool Paused) Hiding(string reason) => reason switch
    {
        "paused" => (Music, Loud, true),
        "off" => (Off, Loud, false),
        "disabled" => (Disabled, Loud, false),
        "silent-hide" => (Hide, Silent, false),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static LightEngine StaticEngine(string reason)
    {
        var (settings, audio, paused) = Hiding(reason);
        var engine = Settled(reason == "silent-hide" ? Hide : Music, Loud);
        Run(engine, 2, audio, settings, paused);
        Assert.True(engine.IsStatic);
        return engine;
    }

    // ---- doc 07 Phase 3: intensity, pulse, phase ----

    [Theory]
    [InlineData(0f, 0.8f)]
    [InlineData(0.5f, 0.8f)]
    [InlineData(1f, 1f)]
    [InlineData(0.25f, 0.1f)]
    [InlineData(0.8f, 0.55f)]
    public void MusicSyncIntensityIsBrightnessTimesTheLevelFormula(float level, float brightness)
    {
        // Intensity = Brightness × (0.35 + 0.65 × Level); Visibility is separate (H-008 item 2).
        var settings = Music with { Brightness = brightness };
        var audio = new AudioFeatures(level, 0, 0, false);
        LightState state = Run(Settled(settings, audio), 0.5f, audio, settings);
        Assert.Equal(brightness * (0.35f + 0.65f * level), state.Intensity, 1e-6f);
        Assert.Equal(1f, state.Visibility);
    }

    [Fact]
    public void SensitivityIsNotAppliedAgain()
    {
        // H-007: the analyzer has already scaled Level and the beat threshold by Sensitivity.
        var audio = new AudioFeatures(0.4f, 0.4f, 0.6f, false);
        var low = Music with { Sensitivity = 0.25f };
        var high = Music with { Sensitivity = 2f };
        Assert.Equal(Run(Settled(low, audio), 0.5f, audio, low), Run(Settled(high, audio), 0.5f, audio, high));
    }

    [Fact]
    public void PulseIsTheBeatOnTheSameFrame()
    {
        // Snappy: no smoothing between the analyzer's Beat (1 on a beat, then τ = 120 ms decay) and Pulse.
        var engine = Settled(Music, Loud);
        foreach (float beat in new[] { 1f, 0.87f, 0.37f, 0.05f, 0f, 1f })
            Assert.Equal(beat, engine.Update(Dt, Loud with { Beat = beat }, Palette.Default, Music, false).Pulse);
    }

    [Theory]
    [InlineData(60f, 0f)]
    [InlineData(30f, 0f)]
    [InlineData(144f, 0f)]
    [InlineData(60f, 0.3f)]
    [InlineData(10f, 0f)]
    public void PhaseDriftsFifteenThousandthsOfACycleASecondAtAnyFrameRate(float fps, float jitter)
    {
        var engine = Settled(Music, Loud);
        var random = new Random(5);
        float phase = engine.Update(0, Loud, Palette.Default, Music, false).Phase;
        double advance = 0, time = 0;
        while (time < 30)
        {
            float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
            float next = engine.Update(dt, Loud, Palette.Default, Music, false).Phase;
            advance += Advance(phase, next);
            phase = next;
            time += dt;
        }
        Assert.Equal(0.015 * time, advance, 1e-5);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(30)]
    public void EachBeatAddsOneHundredthOfACycle(int fps)
    {
        // 120 BPM for 10 s like the analyzer reports it (1 on the beat, τ = 120 ms decay), then 2 s for the last push.
        var engine = Settled(Music, Loud);
        float dt = 1f / fps, beat = 0, phase = engine.Update(0, Loud, Palette.Default, Music, false).Phase;
        double advance = 0;
        int frames = 12 * fps, beats = 0;
        for (int i = 1; i <= frames; i++)
        {
            bool onBeat = i <= 10 * fps && i % (fps / 2) == 0;
            if (onBeat) beats++;
            beat = onBeat ? 1 : beat * MathF.Exp(-dt / 0.12f);
            float next = engine.Update(dt, Loud with { Beat = beat }, Palette.Default, Music, false).Phase;
            advance += Advance(phase, next);
            phase = next;
        }
        Assert.Equal(20, beats);
        Assert.Equal(0.015 * frames * dt + 0.01 * beats, advance, 1e-5);
    }

    [Fact]
    public void AKickIsAQuickPushNotAJump()
    {
        // The 0.01 cycles arrive as a push decaying with τ = 120 ms: 13 % on the beat frame at 60 fps, 92 % by 0.3 s.
        var engine = Settled(Music, Loud);
        float phase = engine.Update(0, Loud, Palette.Default, Music, false).Phase, beat = 1;
        double kicked = 0;
        for (int i = 1; i <= 18; i++)
        {
            float next = engine.Update(Dt, Loud with { Beat = beat }, Palette.Default, Music, false).Phase;
            kicked += Advance(phase, next) - 0.015 * Dt;
            phase = next;
            beat *= MathF.Exp(-Dt / 0.12f);
            if (i == 1) Assert.Equal(0.01 * (1 - Math.Exp(-Dt / 0.12)), kicked, 1e-6);
        }
        Assert.Equal(0.01 * (1 - Math.Exp(-0.3 / 0.12)), kicked, 1e-6);
    }

    [Fact]
    public void ABeatCountsOnceHoweverItRises()
    {
        // A Beat that ramps up over several frames, or repeats on dt = 0 frames, is one beat. A later beat counts again.
        var engine = Settled(Music, Loud);
        float phase = engine.Update(0, Loud, Palette.Default, Music, false).Phase;
        double advance = 0, time = 0;
        void Step(float dt, float beat)
        {
            float next = engine.Update(dt, Loud with { Beat = beat }, Palette.Default, Music, false).Phase;
            advance += Advance(phase, next);
            phase = next;
            time += dt;
        }
        Step(Dt, 0.3f);
        Step(Dt, 0.7f);
        Step(0, 1f);
        Step(0, 1f);
        Step(Dt, 1f);
        float decaying = 1;
        for (int i = 0; i < 120; i++) Step(Dt, decaying *= MathF.Exp(-Dt / 0.12f));
        Assert.Equal(0.015 * time + 0.01, advance, 1e-5);
        Step(Dt, 1f);
        for (int i = 0; i < 120; i++) Step(Dt, decaying *= MathF.Exp(-Dt / 0.12f));
        Assert.Equal(0.015 * time + 0.02, advance, 1e-5);
    }

    // ---- doc 01 §2: Idle Glow and silence ----

    [Fact]
    public void IdleGlowBreathesTenPercentAroundNinetyPercentOfBrightnessEverySixSeconds()
    {
        // About a 6 s sine, ±10 %. Centered on 0.9 × Brightness, so the peak never exceeds Brightness.
        var engine = new LightEngine();
        float min = 1, max = 0, previous = 0, beforePrevious = 0;
        var peaks = new List<double>();
        for (int i = 1; i <= 13 * 60; i++)
        {
            float x = engine.Update(Dt, Loud, Palette.Default, Idle, false).Intensity;
            min = MathF.Min(min, x);
            max = MathF.Max(max, x);
            if (i > 2 && previous > beforePrevious && previous >= x) peaks.Add((i - 1) * (double)Dt);
            beforePrevious = previous;
            previous = x;
        }
        output.WriteLine($"min {min:F5}, max {max:F5}, peaks at {string.Join(", ", peaks.Select(t => t.ToString("F3")))} s");
        Assert.InRange(max, IdleHigh - 1e-4f, IdleHigh + 1e-6f);
        Assert.InRange(min, IdleLow - 1e-6f, IdleLow + 1e-4f);
        Assert.Equal(2, peaks.Count);
        Assert.Equal(6, peaks[1] - peaks[0], Dt);
    }

    [Fact]
    public void IdleGlowIgnoresAudio()
    {
        // No pulse and no drift; Level, Beat and IsSilent change nothing.
        var a = new LightEngine();
        var b = new LightEngine();
        for (int i = 0; i < 600; i++)
        {
            var music = new AudioFeatures(i % 7 / 7f, 0.5f, i % 30 == 0 ? 1 : 0.2f, i % 200 > 100);
            LightState state = a.Update(Dt, music, Palette.Default, Idle, false);
            Assert.Equal(b.Update(Dt, Silent, Palette.Default, Idle, false), state);
            Assert.Equal(0f, state.Pulse);
            Assert.Equal(0f, state.Phase);
        }
    }

    [Fact]
    public void SilenceHandsMusicSyncOverToIdleGlowSmoothlyAndBack()
    {
        // WhenSilent = IdleGlow: over 1.5 s after IsSilent, then back within 150 ms when sound returns.
        // Visibility stays 1 throughout; drift and pulse fade with the music.
        var engine = Settled(Music, Quiet);
        float last = engine.Update(0, Quiet, Palette.Default, Music, false).Intensity;
        Assert.Equal(QuietIntensity, last, 1e-6f);
        var handover = Frames(engine, 90, Silent, Music);
        foreach (LightState state in handover)
        {
            Assert.Equal(1f, state.Visibility);
            Assert.True(MathF.Abs(state.Intensity - last) < 0.015f, $"Intensity jumped {last} → {state.Intensity}");
            last = state.Intensity;
        }
        Assert.InRange(handover[44].Intensity, QuietIntensity + 0.1f, IdleLow - 0.05f);

        // Idle Glow now: breathing range, no drift, no pulse even if the analyzer reported a beat.
        var idle = Frames(engine, 360, Silent with { Beat = 1 }, Music);
        Assert.All(idle, s => Assert.InRange(s.Intensity, IdleLow - 1e-6f, IdleHigh + 1e-6f));
        Assert.All(idle, s => Assert.Equal(handover[^1].Phase, s.Phase));
        Assert.All(idle, s => Assert.Equal(0f, s.Pulse));

        last = idle[^1].Intensity;
        var back = Frames(engine, 9, Loud with { Beat = 0.5f }, Music);
        foreach (LightState state in back)
        {
            Assert.True(MathF.Abs(state.Intensity - last) < 0.1f, $"Intensity jumped {last} → {state.Intensity}");
            last = state.Intensity;
        }
        Assert.True(back[^2].Intensity < LoudIntensity - 1e-3f || back[^2].Intensity > LoudIntensity + 1e-3f);
        Assert.Equal(LoudIntensity, back[^1].Intensity, 1e-6f);
        Assert.Equal(0.5f, back[^1].Pulse);
    }

    [Fact]
    public void HideFadesOutOverOneAndAHalfSecondsAndComesBackWithin150Milliseconds()
    {
        var engine = Settled(Hide, Quiet);
        var fade = Frames(engine, 100, Silent, Hide);
        Assert.Equal(0.5f, fade[44].Visibility, 1e-3f);       // 0.75 s
        Assert.True(fade[88].Visibility > 0);                   // 1.483 s
        Assert.Equal(0f, fade[89].Visibility);                  // 1.5 s
        for (int i = 1; i < fade.Count; i++) Assert.True(fade[i].Visibility <= fade[i - 1].Visibility);
        Assert.All(fade, s => Assert.Equal(QuietIntensity, s.Intensity, 1e-6f)); // Hide fades the Music Sync look

        var back = Frames(engine, 9, Loud, Hide);
        Assert.InRange(back[0].Visibility, 0.01f, 0.5f);         // a fade, not a pop
        Assert.True(back[7].Visibility < 1);
        Assert.Equal(1f, back[8].Visibility);                   // 150 ms
        Assert.All(back, s => Assert.Equal(LoudIntensity, s.Intensity, 1e-6f));
    }

    [Fact]
    public void AHideFadeInterruptedBySoundTurnsStraightBack()
    {
        var engine = Settled(Hide, Quiet);
        float half = Frames(engine, 45, Silent, Hide)[^1].Visibility;
        Assert.Equal(0.5f, half, 1e-3f);
        var back = Frames(engine, 5, Loud, Hide);
        Assert.True(back[0].Visibility > half);
        Assert.Equal(1f, back[4].Visibility);                   // 0.5 of the way at 1/0.15 s: 75 ms
    }

    [Theory]
    [InlineData("when-silent")]
    [InlineData("animation")]
    public void AHideFadeKeepsTheLookItStartsWith(string change)
    {
        // The light shows Idle Glow while silent, then WhenSilent is set to Hide, or Animation switches from Idle Glow
        // to Music Sync with WhenSilent = Hide. It fades out over 1.5 s still breathing as Idle Glow, instead of
        // dropping to the quiet Music Sync level within 150 ms first.
        var engine = Settled(change == "when-silent" ? Music : Idle with { WhenSilent = SilentBehavior.Hide }, Silent);
        var fade = Frames(engine, 100, Silent, Hide);
        Assert.All(fade, s => Assert.InRange(s.Intensity, IdleLow - 1e-6f, IdleHigh + 1e-6f));
        Assert.Equal(0.5f, fade[44].Visibility, 1e-3f);       // 0.75 s
        Assert.True(fade[88].Visibility > 0);
        Assert.Equal(0f, fade[89].Visibility);                  // 1.5 s
        for (int i = 1; i < fade.Count; i++) Assert.True(fade[i].Visibility <= fade[i - 1].Visibility);

        // Once it is gone, sound brings it back with the Music Sync look straight away, within 150 ms.
        var back = Frames(engine, 9, Loud, Hide);
        Assert.Equal(1f, back[8].Visibility);
        Assert.All(back, s => Assert.Equal(LoudIntensity, s.Intensity, 1e-6f));
    }

    // ---- doc 04 §4 and H-008 item 2: pause, Enabled = false, Animation = Off ----

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void TheGateFadesOutAndBackInOver300Milliseconds(string reason)
    {
        var (settings, audio, paused) = Hiding(reason);
        var engine = Settled(Music, Loud);
        var fadeOut = Frames(engine, 18, audio, settings, paused);
        Assert.Equal(0.5f, fadeOut[8].Visibility, 1e-3f);      // 150 ms
        Assert.True(fadeOut[16].Visibility > 0);
        Assert.Equal(0f, fadeOut[17].Visibility);               // 300 ms
        Assert.All(fadeOut, s => Assert.Equal(LoudIntensity, s.Intensity, 1e-6f)); // Intensity excludes Visibility

        var fadeIn = Frames(engine, 18, Loud, Music);
        Assert.Equal(0.5f, fadeIn[8].Visibility, 1e-3f);
        Assert.True(fadeIn[16].Visibility < 1);
        Assert.Equal(1f, fadeIn[17].Visibility);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void ClosingTheGateNeverFlashesALightHiddenBySilence(string reason)
    {
        // It also stays static throughout: hiding a hidden light changes nothing visible.
        var (settings, _, paused) = Hiding(reason);
        var engine = Settled(Hide, Silent);
        Assert.True(engine.IsStatic);
        for (int i = 0; i < 240; i++)
        {
            LightState state = i < 120
                ? engine.Update(Dt, Silent, Palette.Default, settings, paused)
                : engine.Update(Dt, Silent, Palette.Default, Hide, false);
            Assert.Equal(0f, state.Visibility);
            Assert.True(engine.IsStatic);
        }
        Assert.Equal(1f, Run(engine, 0.15f, Loud, Hide).Visibility);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void ClosingTheGateDuringAHideFadeNeverBrightensTheLight(string reason)
    {
        // Silence has faded the light halfway out when the gate closes just as sound returns (a fullscreen app starts
        // its video). The 300 ms gate fade keeps the half-faded light instead of letting it come back as it goes.
        var (settings, audio, paused) = Hiding(reason);
        var engine = Settled(Hide, Quiet);
        float half = Frames(engine, 45, Silent, Hide)[^1].Visibility;
        var fadeOut = Frames(engine, 18, audio, settings, paused);
        Assert.True(fadeOut[0].Visibility < half);
        for (int i = 1; i < fadeOut.Count; i++) Assert.True(fadeOut[i].Visibility < fadeOut[i - 1].Visibility);
        Assert.Equal(half * 0.5f, fadeOut[8].Visibility, 1e-3f); // 150 ms
        Assert.Equal(0f, fadeOut[17].Visibility);               // 300 ms

        var fadeIn = Frames(engine, 18, Loud, Hide);             // sound is back, so the gate reopens onto a full light
        Assert.Equal(0.5f, fadeIn[8].Visibility, 1e-3f);
        Assert.Equal(1f, fadeIn[17].Visibility);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("disabled")]
    public void ClosingTheGateDuringTheIdleGlowCrossfadeKeepsTheLook(string reason)
    {
        // Silence is halfway through handing Music Sync over to Idle Glow when the gate closes: the 300 ms fade-out
        // keeps that blend (only the breathing moves it, by under 0.01) instead of finishing the 1.5 s crossfade.
        var (settings, _, paused) = Hiding(reason);
        var engine = Settled(Music, Quiet);
        float blend = Frames(engine, 45, Silent, Music)[^1].Intensity;
        Assert.InRange(blend, QuietIntensity + 0.1f, IdleLow - 0.05f);
        Assert.All(Frames(engine, 18, Silent, settings, paused), s => Assert.Equal(blend, s.Intensity, 0.01f));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void HowOftenAStaticEngineIsUpdatedChangesNothingLater(string reason)
    {
        // Silence hides the light (WhenSilent = Hide), then the gate closes and reopens, or sound returns while it is
        // closed. The engine stays static throughout, so the renderer may make one Update in each state or many: the
        // light comes back the same way. Sound into a reopened gate shows it within 150 ms; reopening a gate after
        // sound has returned uses the 300 ms gate fade.
        var (closed, _, paused) = Hiding(reason);
        closed = closed with { WhenSilent = SilentBehavior.Hide };
        foreach (bool soundFirst in new[] { false, true })
        {
            List<LightState>? reference = null;
            foreach (int closedUpdates in new[] { 1, 30 })
                foreach (int reopenedUpdates in new[] { 1, 30 })
                {
                    var engine = Settled(Hide, Silent);
                    for (int i = 0; i < closedUpdates; i++)
                    {
                        Assert.Equal(0f, engine.Update(Dt, soundFirst ? Loud : Silent, Palette.Default, closed, paused).Visibility);
                        Assert.True(engine.IsStatic);
                    }
                    for (int i = 0; i < reopenedUpdates && !soundFirst; i++)
                    {
                        Assert.Equal(0f, engine.Update(Dt, Silent, Palette.Default, Hide, false).Visibility);
                        Assert.True(engine.IsStatic);
                    }
                    var back = Frames(engine, 18, Loud, Hide);
                    reference ??= back;
                    Assert.Equal(reference, back);
                }
            Assert.All(reference!, s => Assert.Equal(LoudIntensity, s.Intensity, 1e-6f));
            if (soundFirst)
            {
                Assert.Equal(0.5f, reference![8].Visibility, 1e-3f);
                Assert.True(reference[16].Visibility < 1);
                Assert.Equal(1f, reference[17].Visibility);     // 300 ms
            }
            else
            {
                Assert.InRange(reference![0].Visibility, 0.01f, 0.5f);
                Assert.True(reference[7].Visibility < 1);
                Assert.Equal(1f, reference[8].Visibility);      // 150 ms
            }
        }
    }

    [Fact]
    public void StartsHiddenAndFadesInOver300Milliseconds()
    {
        var engine = new LightEngine();
        Assert.False(engine.IsStatic);
        var frames = Frames(engine, 18, Loud, Music);
        Assert.InRange(frames[0].Visibility, 0.001f, 0.05f);
        Assert.True(frames[16].Visibility < 1);
        Assert.Equal(1f, frames[17].Visibility);
    }

    [Fact]
    public void StartsInTheStateItIsGivenWithoutACrossfade()
    {
        LightState idle = new LightEngine().Update(Dt, Loud, Palette.Default, Idle, false);
        Assert.InRange(idle.Intensity, IdleLow, IdleHigh);
        LightState silent = new LightEngine().Update(Dt, Silent with { Beat = 1 }, Palette.Default, Music, false);
        Assert.InRange(silent.Intensity, IdleLow, IdleHigh);
        Assert.Equal(0f, silent.Pulse);
        var hidden = new LightEngine();
        Assert.Equal(0f, hidden.Update(Dt, Silent, Palette.Default, Hide, false).Visibility);
        Assert.False(hidden.IsStatic);
        Assert.Equal(0f, hidden.Update(Dt, Silent, Palette.Default, Hide, false).Visibility);
        Assert.True(hidden.IsStatic);
    }

    // ---- IsStatic (doc 02 idle optimization) ----

    [Theory]
    [InlineData("music")]
    [InlineData("music-quiet")]
    [InlineData("silent-idle")]
    [InlineData("idle")]
    [InlineData("hide-loud")]
    public void NeverStaticWhileVisible(string scenario)
    {
        // Music Sync always drifts and Idle Glow always breathes, so every visible state moves.
        var (settings, audio) = scenario switch
        {
            "music" => (Music, Loud),
            "music-quiet" => (Music, Quiet),
            "silent-idle" => (Music, Silent),
            "idle" => (Idle, Silent),
            _ => (Hide, Loud),
        };
        var engine = new LightEngine();
        Assert.All(Frames(engine, 600, audio, settings), s => Assert.True(s.Visibility > 0));
        Assert.False(engine.IsStatic);
        for (int i = 0; i < 600; i++)
        {
            engine.Update(Dt, audio, Palette.Default, settings, false);
            Assert.False(engine.IsStatic);
        }
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    [InlineData("silent-hide")]
    public void StaticFromTheFrameAfterTheLightIsGone(string reason)
    {
        // The frame that reaches Visibility 0 is not static, so a renderer that skips static frames still presents it.
        var (settings, audio, paused) = Hiding(reason);
        var engine = Settled(reason == "silent-hide" ? Hide : Music, Loud);
        int gone = -1;
        for (int i = 0; i < 180; i++)
        {
            float visibility = engine.Update(Dt, audio, Palette.Default, settings, paused).Visibility;
            if (gone < 0 && visibility == 0) gone = i;
            Assert.Equal(gone >= 0 && i > gone, engine.IsStatic);
        }
        Assert.Equal(reason == "silent-hide" ? 89 : 17, gone);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    [InlineData("silent-hide")]
    public void WhileStaticTheOutputDependsOnlyOnTheInputs(string reason)
    {
        // Nothing moves with time while static, so a renderer may stop calling Update or call it at any rate.
        var (settings, audio, paused) = Hiding(reason);
        var engine = StaticEngine(reason);
        LightState reference = engine.Update(Dt, audio, Palette.Default, settings, paused);
        foreach (float dt in new[] { 0f, Dt, 0.5f, 10f, 3600f, float.NaN, -1f, float.PositiveInfinity })
        {
            Assert.Equal(reference, engine.Update(dt, audio, Palette.Default, settings, paused));
            Assert.True(engine.IsStatic);
        }
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    [InlineData("silent-hide")]
    public void AppearanceAndAudioChangesThatCannotShowTheLightKeepItStatic(string reason)
    {
        var (settings, audio, paused) = Hiding(reason);
        var engine = StaticEngine(reason);
        var palette = new Palette(new Rgb(1, 0, 0), new Rgb(0, 0, 1), "track");
        var louder = reason == "silent-hide" ? audio with { Level = 1, Beat = 1 } : new AudioFeatures(1, 1, 1, false);
        foreach (Settings changed in new[]
        {
            settings with { Glow = 0.9f }, settings with { Brightness = 0.3f }, settings with { PrimaryRatio = 0.2f },
            settings with { CoreThicknessDip = 20 }, settings with { Sensitivity = 2 }, settings with { CornerRadiusDip = 12 },
        })
        {
            engine.Update(Dt, audio, palette, changed, paused);
            Assert.True(engine.IsStatic);
        }
        engine.Update(Dt, louder, palette, settings, paused);
        Assert.True(engine.IsStatic);
        if (reason != "silent-hide")
        {
            engine.Update(Dt, Silent, palette, settings with { WhenSilent = SilentBehavior.Hide }, paused);
            Assert.True(engine.IsStatic);
        }
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    [InlineData("silent-hide")]
    public void WakesOnTheUpdateWhoseInputsCanShowTheLight(string reason)
    {
        var engine = StaticEngine(reason);
        Settings reopened = reason == "silent-hide" ? Hide : Music;
        engine.Update(0, Loud, Palette.Default, reopened, false);
        Assert.False(engine.IsStatic);                          // even before any time has passed
        Assert.True(engine.Update(Dt, Loud, Palette.Default, reopened, false).Visibility > 0);
    }

    [Theory]
    [InlineData("when-silent-idle")]
    [InlineData("idle-mode")]
    public void ChangingWhatSilenceDoesWakesAHiddenLightStraightIntoIdleGlow(string change)
    {
        var engine = StaticEngine("silent-hide");
        Settings settings = change == "idle-mode" ? Idle with { WhenSilent = SilentBehavior.Hide } : Music;
        engine.Update(0, Silent, Palette.Default, settings, false);
        Assert.False(engine.IsStatic);
        var frames = Frames(engine, 9, Silent, settings);
        Assert.Equal(1f, frames[^1].Visibility);                 // 150 ms
        Assert.All(frames, s => Assert.InRange(s.Intensity, IdleLow - 1e-6f, IdleHigh + 1e-6f)); // no Music Sync look first
    }

    // ---- robustness ----

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.PositiveInfinity)]
    public void AZeroOrInvalidStepAdvancesNothing(float dt)
    {
        var engine = Settled(Music, Loud);
        LightState state = engine.Update(Dt, Loud, Palette.Default, Music, false);
        Assert.Equal(state, engine.Update(dt, Loud, Palette.Default, Music, false));
        LightState fading = engine.Update(Dt, Loud, Palette.Default, Music, true);
        Assert.Equal(fading, engine.Update(dt, Loud, Palette.Default, Music, true));
    }

    [Fact]
    public void AStallCountsAsATenthOfASecond()
    {
        // A huge step (render stall, sleep) moves fades and drift by at most 0.1 s, so nothing jumps.
        var engine = Settled(Music, Loud);
        float phase = engine.Update(0, Loud, Palette.Default, Music, false).Phase;
        LightState state = engine.Update(1000, Loud, Palette.Default, Music, true);
        Assert.Equal(Smooth(1 - 0.1f / 0.3f), state.Visibility, 1e-5f);
        Assert.Equal(0.015 * 0.1, Advance(phase, state.Phase), 1e-6);

        var hide = Settled(Hide, Quiet);
        Assert.Equal(Smooth(1 - 0.1f / 1.5f), hide.Update(3600, Silent, Palette.Default, Hide, false).Visibility, 1e-5f);
    }

    [Fact]
    public void WakingFromStaticStepsAtMostOneFrame()
    {
        // A renderer that idled for an hour passes dt = 3600 on wake; the fade-in still starts from its first frame.
        var engine = StaticEngine("paused");
        LightState state = engine.Update(3600, Loud, Palette.Default, Music, false);
        Assert.Equal(Smooth(1f / 60 / 0.3f), state.Visibility, 1e-6f);
        Assert.False(engine.IsStatic);
        Assert.Equal(Smooth(1f / 60 / 0.3f + 0.1f / 0.3f), engine.Update(3600, Loud, Palette.Default, Music, false).Visibility, 1e-5f);

        // The cap holds for the first step that moves time: a wake with dt = 0 first does not use it up.
        var probed = StaticEngine("paused");
        probed.Update(0, Loud, Palette.Default, Music, false);
        Assert.False(probed.IsStatic);
        Assert.Equal(Smooth(1f / 60 / 0.3f), probed.Update(3600, Loud, Palette.Default, Music, false).Visibility, 1e-6f);
    }

    [Fact]
    public void TheFirstStepOfANewEngineCountsAtMostOneFrame()
    {
        // A renderer may build its swap chains first and pass the half second that took as the first dt (K3, a Settings
        // preview, a hotplugged monitor): the 300 ms fade-in still starts from its first frame, with or without an
        // Update with dt = 0 before it.
        float first = Smooth(1f / 60 / 0.3f);
        var engine = new LightEngine();
        Assert.Equal(first, engine.Update(0.5f, Loud, Palette.Default, Music, false).Visibility, 1e-6f);
        Assert.Equal(Smooth(1f / 60 / 0.3f + 0.1f / 0.3f), engine.Update(0.5f, Loud, Palette.Default, Music, false).Visibility, 1e-5f);

        var probed = new LightEngine();
        Assert.Equal(0f, probed.Update(0, Loud, Palette.Default, Music, false).Visibility);
        Assert.Equal(first, probed.Update(0.5f, Loud, Palette.Default, Music, false).Visibility, 1e-6f);
    }

    [Fact]
    public void NonFiniteAndOutOfRangeInputsGiveFiniteInRangeOutputs()
    {
        var engine = new LightEngine();
        var bad = new Palette(new Rgb(float.NaN, float.PositiveInfinity, -2), new Rgb(5, float.NegativeInfinity, 0.5f), null);
        float[] values = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, -3, 7, 0.5f];
        var settingsList = new List<Settings>();
        foreach (float v in values)
        {
            settingsList.Add(Music with { Brightness = v, Glow = v, PrimaryRatio = v, CoreThicknessDip = v * 100 });
            settingsList.Add(Hide with { Brightness = v, Glow = v, PrimaryRatio = v, CoreThicknessDip = v * 100 });
            settingsList.Add(Idle with { Brightness = v, Glow = v, PrimaryRatio = v, CoreThicknessDip = v * 100 });
            settingsList.Add(Music with { Animation = (AnimationMode)42, WhenSilent = (SilentBehavior)42, Brightness = v });
        }
        int frame = 0;
        foreach (Settings settings in settingsList)
            foreach (float level in values)
                foreach (float beat in values)
                {
                    float dt = values[frame++ % values.Length];
                    LightState s = engine.Update(dt, new AudioFeatures(level, level, beat, frame % 3 == 0), frame % 2 == 0 ? bad : Palette.Default, settings, frame % 5 == 0);
                    foreach (float x in new[] { s.ColorA.R, s.ColorA.G, s.ColorA.B, s.ColorB.R, s.ColorB.G, s.ColorB.B, s.Intensity, s.Spread, s.Pulse, s.Visibility })
                        Assert.InRange(x, 0f, 1f);
                    Assert.InRange(s.Ratio, 0.1f, 0.9f);
                    Assert.InRange(s.CoreThicknessDip, 0f, 40f);
                    Assert.InRange(s.Phase, 0f, 0.99999994f);
                }
        // Non-finite settings fall back to the defaults.
        LightState fallback = Run(new LightEngine(), 1, Loud, Music with { Brightness = float.NaN, Glow = float.NaN, PrimaryRatio = float.NaN, CoreThicknessDip = float.NaN });
        LightState defaults = Run(new LightEngine(), 1, Loud, Music);
        Assert.Equal(defaults, fallback);
    }

    [Fact]
    public void NullPaletteOrSettingsThrow()
    {
        var engine = new LightEngine();
        Assert.Throws<ArgumentNullException>(() => engine.Update(Dt, Loud, null!, Music, false));
        Assert.Throws<ArgumentNullException>(() => engine.Update(Dt, Loud, Palette.Default, null!, false));
    }

    [Fact]
    public void AppearanceFieldsComeFromThePaletteAndSettings()
    {
        var engine = Settled(Music, Loud);
        var palette = new Palette(new Rgb(0.9f, 0.2f, 0.1f), new Rgb(0.1f, 0.3f, 0.8f), "t");
        LightState s = engine.Update(Dt, Loud, palette, Music with { PrimaryRatio = 0.7f, Glow = 0.25f, CoreThicknessDip = 12 }, false);
        Assert.Equal(palette.Primary, s.ColorA);
        Assert.Equal(palette.Secondary, s.ColorB);
        Assert.Equal(0.7f, s.Ratio);
        Assert.Equal(0.25f, s.Spread);
        Assert.Equal(12f, s.CoreThicknessDip);
        Assert.Equal(0.1f, engine.Update(Dt, Loud, palette, Music with { PrimaryRatio = 0.02f }, false).Ratio);
        Assert.Equal(0.9f, engine.Update(Dt, Loud, palette, Music with { PrimaryRatio = 0.97f }, false).Ratio);
        Assert.Equal(1f, engine.Update(Dt, Loud, palette, Music with { Glow = 1.5f }, false).Spread);
        Assert.Equal(40f, engine.Update(Dt, Loud, palette, Music with { CoreThicknessDip = 55 }, false).CoreThicknessDip);
        Assert.Equal(0f, engine.Update(Dt, Loud, palette, Music with { CoreThicknessDip = -1 }, false).CoreThicknessDip);
    }

    [Fact]
    public void PhaseStaysInsideTheUnitInterval()
    {
        var engine = new LightEngine();
        var random = new Random(11);
        float beat = 0;
        for (int i = 0; i < 200_000; i++)
        {
            beat = random.Next(20) == 0 ? 1 : beat * 0.85f;
            float phase = engine.Update((float)random.NextDouble() * 0.2f, Loud with { Beat = beat }, Palette.Default, Music, false).Phase;
            Assert.True(phase >= 0 && phase < 1, $"phase {phase}");
        }
    }

    [Fact]
    public void APhaseJustBelowOneIsReportedAsZero()
    {
        // (float)(1 − 1e-9) is 1, which is outside [0, 1).
        var engine = new LightEngine { PhaseCycles = 1 - 1e-9 };
        Assert.Equal(0f, engine.Update(0, Loud, Palette.Default, Music, false).Phase);
        engine.PhaseCycles = 0.25;
        Assert.Equal(0.25f, engine.Update(0, Loud, Palette.Default, Music, false).Phase);
    }

    [Fact]
    public void TheFactoryReturnsTheRealEngine() => Assert.IsType<LightEngine>(CoreFactory.CreateLightEngine());

    // ---- determinism and allocations ----

    private static readonly Settings[] Modes = [Music, Hide, Idle, Off, Disabled, Music with { Brightness = 0.4f, Glow = 0.9f }];

    private static void Scenario(LightEngine engine, int frames, int seed, List<(LightState State, bool IsStatic)>? record)
    {
        var random = new Random(seed);
        Settings settings = Music;
        bool paused = false, silent = false;
        float beat = 0;
        for (int i = 0; i < frames; i++)
        {
            if (random.Next(120) == 0) settings = Modes[random.Next(Modes.Length)];
            if (random.Next(200) == 0) paused = !paused;
            if (random.Next(150) == 0) silent = !silent;
            beat = !silent && random.Next(30) == 0 ? 1 : beat * 0.87f;
            float dt = random.Next(500) == 0 ? 2f : Dt * (0.8f + 0.4f * (float)random.NextDouble());
            var audio = new AudioFeatures(silent ? 0 : (float)random.NextDouble(), 0.5f, beat, silent);
            LightState state = engine.Update(dt, audio, Palette.Default, settings, paused);
            record?.Add((state, engine.IsStatic));
        }
    }

    [Fact]
    public void IsDeterministic()
    {
        var a = new List<(LightState, bool)>();
        var b = new List<(LightState, bool)>();
        Scenario(new LightEngine(), 30_000, 21, a);
        Scenario(new LightEngine(), 30_000, 21, b);
        Assert.Equal(a, b);
        Assert.Contains(a, x => x.Item2);
        Assert.Contains(a, x => !x.Item2);
    }

    [Fact]
    public void UpdateDoesNotAllocate()
    {
        var engine = new LightEngine();
        Scenario(engine, 2_000, 3, null);
        // Modes cycle every 250 frames (Off and Enabled = false go static), with pauses, silences and 3 s stalls.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50_000; i++)
            engine.Update(i % 400 == 0 ? 3f : Dt, new AudioFeatures(i % 13 / 13f, 0, i % 31 == 0 ? 1 : 0, i % 900 > 600), Palette.Default, Modes[i / 250 % Modes.Length], i % 700 > 650);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- with the real analyzer ----

    [Fact]
    public void FollowsTheRealAnalyzerThroughMusicSilenceAndMusicAgain()
    {
        // 10 s of 120 BPM kicks, 8 s without packets (WASAPI sends none while nothing plays), then 4 s of kicks.
        // Hide: visible while the music plays, gone 1.5 s after IsSilent, back within 150 ms of IsSilent clearing.
        // Each analyzer beat pushes the phase by exactly 0.01 cycles on top of the drift.
        const int rate = 48000, step = rate / 60;
        float[] kicks = SyntheticAudio.KickTrack(rate, 10, 120, 0.8f, 0.2f);
        var analyzer = new AudioAnalyzer();
        var engine = new LightEngine();
        float phase = 0;
        double advance = 0;
        int frames = 22 * 60, silentFrom = -1, silentUntil = -1, beats = 0;
        var visibility = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            ReadOnlySpan<float> samples = i < 600 ? kicks.AsSpan(i * step, step)
                : i < 1080 ? ReadOnlySpan<float>.Empty
                : kicks.AsSpan((i - 1080) * step, step);
            AudioFeatures audio = analyzer.Process(samples, rate, Dt);
            LightState state = engine.Update(Dt, audio, Palette.Default, Hide, false);
            visibility[i] = state.Visibility;
            if (i < 700) advance += Advance(phase, state.Phase);  // to 11.7 s: the last push has finished, IsSilent not yet
            phase = state.Phase;
            if (i == 699) beats = analyzer.Diagnostics.BeatCount;
            if (audio.IsSilent && silentFrom < 0) silentFrom = i;
            if (!audio.IsSilent && silentFrom >= 0 && silentUntil < 0) silentUntil = i;
        }
        output.WriteLine($"{beats} beats, phase +{advance:F5}; IsSilent from frame {silentFrom} until {silentUntil}");
        Assert.InRange(beats, 17, 20);
        Assert.Equal(0.015 * 700 * Dt + 0.01 * beats, advance, 1e-5);
        Assert.All(visibility[18..silentFrom], v => Assert.Equal(1f, v));
        Assert.InRange(silentFrom, 600 + 115, 600 + 125);       // the analyzer's 2 s silence hold
        Assert.True(visibility[silentFrom + 88] > 0);
        Assert.All(visibility[(silentFrom + 89)..silentUntil], v => Assert.Equal(0f, v));
        Assert.InRange(silentUntil, 1080, 1083);
        Assert.Equal(1f, visibility[silentUntil + 8]);
    }
}
