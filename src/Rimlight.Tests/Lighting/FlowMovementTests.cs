using Rimlight.Core;
using Rimlight.Core.Lighting;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Lighting;

// H-033: the Flow movement, the default. Maxwell: "I want the rimlight to be like the video I sent where it moves
// around and doesn't just bounce". A comet chases round the edge, faster when the music is louder and pushed along by
// each beat; the colors stream round it faster; each beat sends a ripple running both ways along it from one spot,
// golden-ratio steps apart, while the whole-edge pulse keeps 40 % of the beat. Expected values are the spec's numbers
// written out, not the engine's constants, so changing a constant fails a test. (The Pulse movement is pinned to the
// engine before Flow by PulseMovementTests; the fades, IsStatic and robustness are in LightEngineTests, whose default
// settings are Flow.)
public sealed class FlowMovementTests(ITestOutputHelper output)
{
    private const float Dt = 1f / 60;
    private const double Golden = 0.618034;                // each ripple starts this far round from the last
    private static readonly Settings Flow = new();
    private static readonly Settings Pulse = new() { Movement = MovementStyle.Pulse };
    private static readonly AudioFeatures Loud = new(0.5f, 0.5f, 0f, false);
    private static readonly AudioFeatures Silent = new(0f, 0f, 0f, true);

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    private static LightEngine Settled(Settings settings, AudioFeatures audio)
    {
        var engine = new LightEngine();
        for (int i = 0; i < 180; i++) engine.Update(Dt, audio, Palette.Default, settings, false);
        return engine;
    }

    private static List<LightState> Frames(LightEngine engine, int count, AudioFeatures audio, Settings settings, bool paused = false)
    {
        var states = new List<LightState>(count);
        for (int i = 0; i < count; i++) states.Add(engine.Update(Dt, audio, Palette.Default, settings, paused));
        return states;
    }

    // Movement in cycles, unwrapped: a frame never moves it by half a cycle.
    private static double Advance(float from, float to)
    {
        double d = to - from;
        return d < -0.5 ? d + 1 : d;
    }

    private static Ripple[] Ripples(in LightState s) => [s.Flow.Ripple0, s.Flow.Ripple1, s.Flow.Ripple2, s.Flow.Ripple3];

    private static Ripple[] Live(in LightState s) => Ripples(s).Where(r => r.Strength > 0).ToArray();

    // The ripple a beat started on this frame: its fronts haven't moved yet.
    private static Ripple Newborn(in LightState s) => Assert.Single(Ripples(s), r => r.Strength > 0 && r.Radius == 0);

    private static double Fraction(double x) => x - Math.Floor(x);

    // ---- how much of it shows ----

    [Fact]
    public void FlowIsTheDefaultAndShowsInFullWithTheMusic()
    {
        Assert.Equal(MovementStyle.Flow, new Settings().Movement);
        LightState state = Settled(Flow, Loud).Update(Dt, Loud, Palette.Default, Flow, false);
        Assert.Equal(1f, state.Flow.Amount);
        Assert.Equal(1f, state.Visibility);
        // An undefined Movement behaves like the default.
        var odd = Flow with { Movement = (MovementStyle)7 };
        Assert.Equal(1f, Settled(odd, Loud).Update(Dt, Loud, Palette.Default, odd, false).Flow.Amount);
    }

    [Fact]
    public void PulseAndIdleGlowShowNoFlow()
    {
        // Nothing of the Flow movement in Pulse or in Idle Glow, beats or not: the output is none, so the renderers
        // compute nothing for it.
        foreach (Settings settings in new[] { Pulse, Flow with { Animation = AnimationMode.IdleGlow }, Pulse with { Animation = AnimationMode.IdleGlow } })
        {
            var engine = new LightEngine();
            float beat = 0;
            for (int i = 0; i < 600; i++)
            {
                beat = i % 30 == 0 ? 1 : beat * 0.87f;
                LightState state = engine.Update(Dt, new AudioFeatures(i % 7 / 7f, 0.5f, beat, false), Palette.Default, settings, false);
                Assert.Equal(default, state.Flow);
            }
        }
    }

    [Fact]
    public void SilenceFadesTheFlowOutWithTheMusic()
    {
        // WhenSilent = IdleGlow: the Flow amount is the music weight, so it fades over the same 1.5 s as Intensity and
        // Pulse, and comes back within 150 ms with the sound.
        var engine = Settled(Flow, Loud);
        List<LightState> fade = Frames(engine, 100, Silent with { Beat = 1 }, Flow);
        Assert.Equal(Smooth(0.5f), fade[44].Flow.Amount, 1e-3f);       // 0.75 s
        Assert.True(fade[88].Flow.Amount > 0);
        Assert.Equal(default, fade[89].Flow);                           // 1.5 s: Idle Glow
        for (int i = 1; i < fade.Count; i++) Assert.True(fade[i].Flow.Amount <= fade[i - 1].Flow.Amount);
        List<LightState> back = Frames(engine, 9, Loud, Flow);
        Assert.True(back[7].Flow.Amount < 1);
        Assert.Equal(1f, back[8].Flow.Amount);                          // 150 ms
    }

    [Fact]
    public void ThePulseKeepsFortyPercentOfTheBeat()
    {
        // The ripples carry the rest along the edge; the whole edge still swells a little on each beat.
        var engine = Settled(Flow, Loud);
        foreach (float beat in new[] { 1f, 0.87f, 0.37f, 0.05f, 0f, 1f })
            Assert.Equal(0.4f * beat, engine.Update(Dt, Loud with { Beat = beat }, Palette.Default, Flow, false).Pulse, 1e-6f);
    }

    // ---- the comets ----

    [Theory]
    [InlineData(0f, 60f, 0f)]
    [InlineData(0.5f, 60f, 0f)]
    [InlineData(1f, 60f, 0f)]
    [InlineData(1f, 144f, 0.3f)]
    [InlineData(0.25f, 30f, 0.3f)]
    public void TheCometsRunFasterWhenTheMusicIsLouder(float level, float fps, float jitter)
    {
        // 0.15 laps a second at Level 0 (6.7 s a lap), 0.45 at Level 1 (2.2 s), at any frame rate.
        var audio = new AudioFeatures(level, 0.5f, 0, false);
        var engine = Settled(Flow, audio);
        var random = new Random(3);
        float comets = engine.Update(0, audio, Palette.Default, Flow, false).Flow.CometPhase;
        double advance = 0, time = 0;
        while (time < 20)
        {
            float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
            float next = engine.Update(dt, audio, Palette.Default, Flow, false).Flow.CometPhase;
            advance += Advance(comets, next);
            comets = next;
            time += dt;
        }
        output.WriteLine($"Level {level}: {advance / time:F5} laps a second");
        Assert.Equal((0.15 + 0.3 * level) * time, advance, 1e-5);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(30)]
    public void EachBeatPushesTheCometEightHundredthsOfALap(int fps)
    {
        // 120 BPM for 10 s like the analyzer reports it (1 on the beat, τ = 120 ms decay), then 2 s for the last push.
        // The colors' kick stays 0.01 cycles a beat, on top of Flow's faster drift (0.04 + 0.06 × Level).
        var engine = Settled(Flow, Loud);
        float dt = 1f / fps, beat = 0;
        LightState state = engine.Update(0, Loud, Palette.Default, Flow, false);
        float comets = state.Flow.CometPhase, phase = state.Phase;
        double cometAdvance = 0, phaseAdvance = 0;
        int frames = 12 * fps, beats = 0;
        for (int i = 1; i <= frames; i++)
        {
            bool onBeat = i <= 10 * fps && i % (fps / 2) == 0;
            if (onBeat) beats++;
            beat = onBeat ? 1 : beat * MathF.Exp(-dt / 0.12f);
            state = engine.Update(dt, Loud with { Beat = beat }, Palette.Default, Flow, false);
            cometAdvance += Advance(comets, state.Flow.CometPhase);
            phaseAdvance += Advance(phase, state.Phase);
            comets = state.Flow.CometPhase;
            phase = state.Phase;
        }
        Assert.Equal(20, beats);
        Assert.Equal(0.3 * frames * dt + 0.08 * beats, cometAdvance, 1e-5); // 0.46 laps a second: the clip's 2.2 s
        Assert.Equal(0.07 * frames * dt + 0.01 * beats, phaseAdvance, 1e-5);
    }

    [Fact]
    public void ABeatPushesTheCometsAsAQuickPushInProportionToItsHeight()
    {
        // A beat of 0.5 pushes 0.04 laps, delivered like the colors' kick: 13 % on the beat frame at 60 fps, 92 % by
        // 0.3 s. A rise of 0.1 or less is no beat at all.
        var engine = Settled(Flow, Loud);
        float comets = engine.Update(0, Loud, Palette.Default, Flow, false).Flow.CometPhase, beat = 0.5f;
        double pushed = 0;
        for (int i = 1; i <= 18; i++)
        {
            float next = engine.Update(Dt, Loud with { Beat = beat }, Palette.Default, Flow, false).Flow.CometPhase;
            pushed += Advance(comets, next) - 0.3 * Dt;
            comets = next;
            beat *= MathF.Exp(-Dt / 0.12f);
            if (i == 1) Assert.Equal(0.04 * (1 - Math.Exp(-Dt / 0.12)), pushed, 1e-6);
        }
        Assert.Equal(0.04 * (1 - Math.Exp(-0.3 / 0.12)), pushed, 1e-6);
        for (int i = 0; i < 120; i++)
        {
            float next = engine.Update(Dt, Loud with { Beat = beat *= MathF.Exp(-Dt / 0.12f) }, Palette.Default, Flow, false).Flow.CometPhase;
            pushed += Advance(comets, next) - 0.3 * Dt;
            comets = next;
        }
        Assert.Equal(0.04, pushed, 1e-6);
        foreach (float wobble in new[] { 0.09f, 0.05f, 0.14f })
        {
            float next = engine.Update(Dt, Loud with { Beat = wobble }, Palette.Default, Flow, false).Flow.CometPhase;
            pushed += Advance(comets, next) - 0.3 * Dt;
            comets = next;
        }
        Assert.Equal(0.04, pushed, 1e-6);
    }

    [Fact]
    public void ACometPhaseJustBelowOneIsReportedAsZero()
    {
        var engine = Settled(Flow, Loud);
        engine.CometPhaseCycles = 1 - 1e-9;
        Assert.Equal(0f, engine.Update(0, Loud, Palette.Default, Flow, false).Flow.CometPhase);
        engine.CometPhaseCycles = 0.25;
        Assert.Equal(0.25f, engine.Update(0, Loud, Palette.Default, Flow, false).Flow.CometPhase);
    }

    // ---- the colors ----

    [Theory]
    [InlineData(0f, 60f, 0f)]
    [InlineData(0.5f, 60f, 0f)]
    [InlineData(1f, 144f, 0.3f)]
    [InlineData(0.8f, 10f, 0f)]
    public void TheColorsStreamFasterInFlowAndFasterStillWhenLouder(float level, float fps, float jitter)
    {
        // 0.04 cycles a second at Level 0 and 0.1 at Level 1, against Pulse's 0.015 at any level.
        foreach (Settings settings in new[] { Flow, Pulse })
        {
            var audio = new AudioFeatures(level, 0.5f, 0, false);
            var engine = Settled(settings, audio);
            var random = new Random(5);
            float phase = engine.Update(0, audio, Palette.Default, settings, false).Phase;
            double advance = 0, time = 0;
            while (time < 20)
            {
                float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
                float next = engine.Update(dt, audio, Palette.Default, settings, false).Phase;
                advance += Advance(phase, next);
                phase = next;
                time += dt;
            }
            double expected = settings.Movement == MovementStyle.Flow ? 0.04 + 0.06 * level : 0.015;
            Assert.Equal(expected * time, advance, 1e-5);
        }
    }

    // ---- the ripples ----

    [Fact]
    public void RipplesStartOnlyOnBeatsEachAGoldenRatioStepFromTheLast()
    {
        // Beat wobbling by 0.1 or less starts nothing. Then eight beats of graded heights: each starts one ripple at
        // the height its pulse reaches, k × 0.618034 round the perimeter from the first (never the same spot twice in a
        // row: consecutive origins are at least 0.38 apart round the loop).
        var engine = Settled(Flow, Loud);
        foreach (float wobble in new[] { 0.05f, 0.12f, 0.08f, 0.15f, 0.1f, 0.19f, 0f })
            Assert.Empty(Live(engine.Update(Dt, Loud with { Beat = wobble }, Palette.Default, Flow, false)));

        float[] heights = [1f, 0.6f, 0.35f, 1f, 0.8f, 0.5f, 1f, 0.9f];
        float previous = float.NaN;
        for (int k = 0; k < heights.Length; k++)
        {
            Ripple born = Newborn(engine.Update(Dt, Loud with { Beat = heights[k] }, Palette.Default, Flow, false));
            Assert.Equal(Fraction(k * Golden), born.Origin, 1e-5);
            Assert.Equal(heights[k], born.Strength);
            if (k > 0)
            {
                double apart = Math.Abs(born.Origin - previous);
                Assert.InRange(Math.Min(apart, 1 - apart), 0.38, 0.5);
            }
            previous = born.Origin;
            float beat = heights[k];
            for (int i = 0; i < 20; i++)
            {
                LightState state = engine.Update(Dt, Loud with { Beat = beat *= 0.87f }, Palette.Default, Flow, false);
                Assert.DoesNotContain(Ripples(state), r => r.Strength > 0 && r.Radius == 0); // no ripple without a beat
            }
        }
    }

    [Theory]
    [InlineData(60f)]
    [InlineData(144f)]
    [InlineData(30f)]
    public void ARippleRunsAtNearlyHalfAPerimeterASecondAndFades(float fps)
    {
        // Each front runs 0.45 perimeters a second; the strength is the beat's height × exp(-age / 0.4 s), and the
        // ripple is dropped once under 1/512: 2.41 s after a beat of 0.8.
        var engine = Settled(Flow, Loud);
        float dt = 1 / fps, beat = 0.8f;
        LightState state = engine.Update(dt, Loud with { Beat = beat }, Palette.Default, Flow, false);
        Assert.Equal(new Ripple(0, 0, 0.8f), Assert.Single(Live(state)));
        double age = 0, lastStrength = 1;
        while (Live(state).Length == 1)
        {
            Ripple r = state.Flow.Ripple0;
            Assert.Equal(0f, r.Origin);
            Assert.Equal(0.45 * age, r.Radius, 1e-5);
            Assert.Equal(0.8 * Math.Exp(-age / 0.4), r.Strength, 1e-6);
            Assert.True(r.Strength < lastStrength);
            lastStrength = r.Strength;
            state = engine.Update(dt, Loud with { Beat = beat *= MathF.Exp(-dt / 0.12f) }, Palette.Default, Flow, false);
            age += dt;
        }
        output.WriteLine($"{fps} fps: gone {age:F3} s after the beat, last strength {lastStrength:E3}");
        Assert.Empty(Live(state));
        Assert.Equal(default, state.Flow.Ripple0);
        Assert.InRange(lastStrength, 1.0 / 512, 1.0 / 512 * Math.Exp(dt / 0.4) + 1e-6);
        Assert.InRange(age, 0.4 * Math.Log(0.8 * 512) - 1e-4, 0.4 * Math.Log(0.8 * 512) + dt + 1e-4);
    }

    [Fact]
    public void AtAFastTempoTheFaintestRippleMakesWay()
    {
        // Beats every 0.1 s: the fifth arrives while four ripples still run. It takes the faintest one's slot, here the
        // soft second beat's (0.6 × e^-0.75 = 0.28), not the oldest one's (e^-1 = 0.37); the others are untouched.
        var engine = Settled(Flow, Loud);
        float[] heights = [1f, 0.6f, 1f, 1f, 0.9f];
        LightState state = default;
        float beat = 0;
        for (int i = 0; i <= 24; i++)
        {
            beat = i % 6 == 0 ? heights[i / 6] : beat * MathF.Exp(-Dt / 0.12f);
            state = engine.Update(Dt, Loud with { Beat = beat }, Palette.Default, Flow, false);
            if (i == 23)
            {
                Ripple[] four = Ripples(state);
                Assert.All(four, r => Assert.True(r.Strength > 0));
                for (int k = 0; k < 4; k++) Assert.Equal(Fraction(k * Golden), four[k].Origin, 1e-5);
                Assert.Equal(0.6 * Math.Exp(-17 * Dt / 0.4), four[1].Strength, 1e-5);
            }
        }
        Ripple[] slots = Ripples(state);
        Assert.Equal(Fraction(4 * Golden), slots[1].Origin, 1e-5);
        Assert.Equal(0f, slots[1].Radius);
        Assert.Equal(0.9f, slots[1].Strength);
        Assert.Equal(0f, slots[0].Origin);
        Assert.Equal(Math.Exp(-1), slots[0].Strength, 1e-5);
        Assert.Equal(Fraction(2 * Golden), slots[2].Origin, 1e-5);
        Assert.Equal(Fraction(3 * Golden), slots[3].Origin, 1e-5);
    }

    [Fact]
    public void ABeatThatKeepsRisingRaisesItsOwnRipple()
    {
        // A beat whose pulse rises over several frames (or repeats on a dt = 0 frame) starts one ripple, which grows to
        // the height the pulse reaches; an older ripple is left alone.
        var engine = Settled(Flow, Loud);
        LightState state = engine.Update(Dt, Loud with { Beat = 1 }, Palette.Default, Flow, false);
        float beat = 1;
        for (int i = 0; i < 30; i++) state = engine.Update(Dt, Loud with { Beat = beat *= 0.87f }, Palette.Default, Flow, false);
        float older = state.Flow.Ripple0.Strength;
        Assert.Equal(Math.Exp(-30 * Dt / 0.4), older, 1e-5);

        state = engine.Update(Dt, Loud with { Beat = 0.3f }, Palette.Default, Flow, false);
        Assert.Equal(0.3f, Newborn(state).Strength);
        state = engine.Update(Dt, Loud with { Beat = 0.7f }, Palette.Default, Flow, false);
        Assert.Equal(2, Live(state).Length);
        Assert.Equal(0.7 * Math.Exp(-Dt / 0.4), state.Flow.Ripple1.Strength, 1e-6);
        state = engine.Update(0, Loud with { Beat = 1 }, Palette.Default, Flow, false);
        Assert.Equal(Math.Exp(-Dt / 0.4), state.Flow.Ripple1.Strength, 1e-6);
        Assert.Equal(0.45 * Dt, state.Flow.Ripple1.Radius, 1e-6);
        Assert.Equal(Golden, state.Flow.Ripple1.Origin, 1e-6);
        Assert.Equal(older * Math.Exp(-2 * Dt / 0.4), state.Flow.Ripple0.Strength, 1e-6);
        state = engine.Update(Dt, Loud with { Beat = 1 }, Palette.Default, Flow, false);
        Assert.Equal(Math.Exp(-2 * Dt / 0.4), state.Flow.Ripple1.Strength, 1e-6);
        Assert.Equal(2, Live(state).Length);
    }

    // ---- the Movement setting ----

    [Fact]
    public void SwitchingMovementCrossfadesOver300Milliseconds()
    {
        // Flow to Pulse: the Flow amount falls and the pulse's share rises back to the whole beat over 300 ms, both
        // smoothstep-eased; at the end the output is Pulse's exactly. And back.
        var engine = Settled(Flow, Loud);
        var beating = Loud with { Beat = 0.5f };
        List<LightState> toPulse = Frames(engine, 18, beating, Pulse);
        Assert.Equal(0.5f, toPulse[8].Flow.Amount, 1e-3f);              // 150 ms
        Assert.Equal(0.5f * (1 - 0.6f * 0.5f), toPulse[8].Pulse, 1e-3f);
        Assert.True(toPulse[16].Flow.Amount > 0);
        Assert.Equal(default, toPulse[17].Flow);                         // 300 ms
        Assert.Equal(0.5f, toPulse[17].Pulse);
        for (int i = 1; i < toPulse.Count; i++)
        {
            Assert.True(toPulse[i].Flow.Amount < toPulse[i - 1].Flow.Amount || toPulse[i].Flow.Amount == 0);
            Assert.True(toPulse[i].Pulse > toPulse[i - 1].Pulse);
            Assert.Equal(1f, toPulse[i].Visibility);
        }

        List<LightState> toFlow = Frames(engine, 18, beating, Flow);
        Assert.Equal(0.5f, toFlow[8].Flow.Amount, 1e-3f);
        Assert.True(toFlow[16].Flow.Amount < 1);
        Assert.Equal(1f, toFlow[17].Flow.Amount);
        Assert.Equal(0.2f, toFlow[17].Pulse, 1e-6f);
    }

    [Theory]
    [InlineData(MovementStyle.Flow, MovementStyle.Pulse, false)]
    [InlineData(MovementStyle.Pulse, MovementStyle.Flow, false)]
    [InlineData(MovementStyle.Flow, MovementStyle.Pulse, true)]
    [InlineData(MovementStyle.Pulse, MovementStyle.Flow, true)]
    public void AMovementChangedWhileHiddenIsThereWhenTheLightReturns(MovementStyle from, MovementStyle to, bool silentHide)
    {
        // Nothing shows, so nothing needs a crossfade: the light comes back with the new movement in full, and changing
        // it keeps a hidden engine static, whether a pause or silence (WhenSilent = Hide) hid it. Silence leaves the
        // gate open, so that case checks the movement's own rule for a hidden light, not the gate's.
        Settings before = Flow with { Movement = from, WhenSilent = silentHide ? SilentBehavior.Hide : SilentBehavior.IdleGlow };
        Settings after = before with { Movement = to };
        AudioFeatures hiddenAudio = silentHide ? Silent : Loud;
        bool hiddenPaused = !silentHide;
        var engine = Settled(before, Loud);
        Frames(engine, silentHide ? 100 : 30, hiddenAudio, before, hiddenPaused);
        Assert.True(engine.IsStatic);
        LightState hidden = engine.Update(Dt, hiddenAudio, Palette.Default, after, hiddenPaused);
        Assert.True(engine.IsStatic);
        Assert.Equal(0f, hidden.Visibility);
        foreach (float dt in new[] { 0f, Dt, Dt, 3600f })
        {
            Assert.Equal(hidden, engine.Update(dt, hiddenAudio, Palette.Default, after, hiddenPaused));
            Assert.True(engine.IsStatic);
        }
        foreach (LightState state in Frames(engine, 18, Loud with { Beat = 0.5f }, after))
        {
            Assert.Equal(to == MovementStyle.Flow ? 1f : 0f, state.Flow.Amount);
            Assert.Equal(to == MovementStyle.Flow ? 0.2f : 0.5f, state.Pulse, 1e-6f);
        }
    }

    [Theory]
    [InlineData(MovementStyle.Flow, MovementStyle.Pulse)]
    [InlineData(MovementStyle.Pulse, MovementStyle.Flow)]
    public void AHideFadeKeepsTheMovementItStartsWith(MovementStyle from, MovementStyle to)
    {
        // The twin of LightEngineTests.AHideFadeKeepsTheLookItStartsWith. Silence (WhenSilent = Hide) has just started
        // fading the light out when the Movement is switched: it fades out over 1.5 s with the comets and the pulse's
        // share it had, instead of crossfading as it goes (Flow to Pulse brightened the edge between the comets mid
        // fade-out). Once it is gone, sound brings it back with the new movement in full, within 150 ms.
        Settings before = Flow with { Movement = from, WhenSilent = SilentBehavior.Hide };
        Settings after = before with { Movement = to };
        var engine = Settled(before, Loud);
        var quiet = Silent with { Beat = 0.5f };
        List<LightState> fade = Frames(engine, 3, quiet, before);
        fade.AddRange(Frames(engine, 97, quiet, after));
        Assert.All(fade, s => Assert.Equal(from == MovementStyle.Flow ? 1f : 0f, s.Flow.Amount));
        Assert.All(fade, s => Assert.Equal(from == MovementStyle.Flow ? 0.2f : 0.5f, s.Pulse, 1e-6f));
        Assert.Equal(0.5f, fade[44].Visibility, 1e-3f);       // 0.75 s
        Assert.True(fade[88].Visibility > 0);
        Assert.Equal(0f, fade[89].Visibility);                  // 1.5 s
        for (int i = 1; i < fade.Count; i++) Assert.True(fade[i].Visibility <= fade[i - 1].Visibility);

        List<LightState> back = Frames(engine, 9, Loud with { Beat = 0.5f }, after);
        Assert.Equal(1f, back[8].Visibility);
        foreach (LightState state in back)
        {
            Assert.Equal(to == MovementStyle.Flow ? 1f : 0f, state.Flow.Amount);
            Assert.Equal(to == MovementStyle.Flow ? 0.2f : 0.5f, state.Pulse, 1e-6f);
        }
    }

    // ---- hidden ----

    [Fact]
    public void HidingClearsTheRipplesAndStopsTheComets()
    {
        // A beat lands just before the gate closes. Its ripple runs and its push moves the comets during the 300 ms
        // fade-out; once the light is hidden the ripple is gone, the comets stand still (what is left of the push is
        // dropped) and the engine is static. The light comes back with no ripple and the comets moving on from where
        // they stopped at their own speed.
        var engine = Settled(Flow, Loud);
        engine.Update(Dt, Loud with { Beat = 1 }, Palette.Default, Flow, false);
        List<LightState> fade = Frames(engine, 30, Loud, Flow, paused: true);
        Assert.True(Live(fade[10]).Length == 1 && fade[10].Visibility > 0);
        Assert.True(engine.IsStatic);
        LightState still = fade[^1];
        Assert.Equal(0f, still.Visibility);
        Assert.Empty(Live(still));
        Assert.Equal(1f, still.Flow.Amount);
        foreach (float dt in new[] { 0f, Dt, 0.5f, 3600f, float.NaN })
            Assert.Equal(still, engine.Update(dt, Loud, Palette.Default, Flow, true));

        float comets = still.Flow.CometPhase;
        double advance = 0;
        foreach (LightState state in Frames(engine, 60, Loud, Flow))
        {
            Assert.Empty(Live(state));
            advance += Advance(comets, state.Flow.CometPhase);
            comets = state.Flow.CometPhase;
        }
        Assert.Equal(0.3 * 60 * Dt, advance, 1e-6);
    }

    [Fact]
    public void SilenceThatHidesTheLightClearsTheRipplesToo()
    {
        // WhenSilent = Hide: the light fades out over 1.5 s with the Flow it had; hidden, nothing moves.
        Settings hide = Flow with { WhenSilent = SilentBehavior.Hide };
        var engine = Settled(hide, Loud);
        engine.Update(Dt, Loud with { Beat = 1 }, Palette.Default, hide, false);
        List<LightState> fade = Frames(engine, 100, Silent, hide);
        Assert.Equal(1f, fade[44].Flow.Amount);
        Assert.True(engine.IsStatic);
        Assert.Equal(0f, fade[^1].Visibility);
        Assert.Empty(Live(fade[^1]));
        Assert.Equal(fade[^2], fade[^1]);
    }

    [Fact]
    public void AStallMovesTheCometsAndRipplesATenthOfASecond()
    {
        // A huge step (render stall, sleep) counts as 0.1 s for the comets and the ripples too, so nothing jumps. The
        // beat lands on a dt = 0 frame, so the whole stall is the push's first 0.1 s.
        var engine = Settled(Flow, Loud);
        LightState state = engine.Update(0, Loud with { Beat = 1 }, Palette.Default, Flow, false);
        float comets = state.Flow.CometPhase;
        Assert.Equal(new Ripple(0, 0, 1), state.Flow.Ripple0);
        state = engine.Update(1000, Loud with { Beat = 1 }, Palette.Default, Flow, false);
        Assert.Equal(0.3 * 0.1 + 0.08 * (1 - Math.Exp(-0.1 / 0.12)), Advance(comets, state.Flow.CometPhase), 1e-6);
        Assert.Equal(0.045f, state.Flow.Ripple0.Radius, 1e-6f);
        Assert.Equal(Math.Exp(-0.1 / 0.4), state.Flow.Ripple0.Strength, 1e-6);
    }

    // ---- determinism ----

    [Fact]
    public void TwoEnginesAgreeAndRipplesKeepTimeAtAnyFrameRate()
    {
        // Two engines, the same audio and settings: identical comets and ripples, frame for frame. And the ripples'
        // fronts are where their age puts them at any frame rate: 1 s after a beat at 30, 60 or 144 fps.
        var a = new LightEngine();
        var b = new LightEngine();
        var random = new Random(9);
        float beat = 0;
        for (int i = 0; i < 6000; i++)
        {
            beat = random.Next(20) == 0 ? (float)random.NextDouble() : beat * 0.9f;
            var audio = new AudioFeatures((float)random.NextDouble(), 0.5f, beat, false);
            Assert.Equal(a.Update(Dt, audio, Palette.Default, Flow, false), b.Update(Dt, audio, Palette.Default, Flow, false));
        }

        foreach (int fps in new[] { 30, 60, 144 })
        {
            var engine = Settled(Flow, Loud);
            engine.Update(1f / fps, Loud with { Beat = 1 }, Palette.Default, Flow, false);
            LightState state = default;
            for (int i = 0; i < fps; i++) state = engine.Update(1f / fps, Loud, Palette.Default, Flow, false);
            Assert.Equal(0.45, state.Flow.Ripple0.Radius, 1e-5);
            Assert.Equal(Math.Exp(-1 / 0.4), state.Flow.Ripple0.Strength, 1e-6);
        }
    }
}
