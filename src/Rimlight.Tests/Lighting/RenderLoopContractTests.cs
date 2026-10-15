using Rimlight.Core;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Lighting;

// H-011: what the K3 render loop (Platform's FramePacer and OverlayHost, App's MusicGlowSource) assumes about the light
// engine, one group per assumption. The engine comes from CoreFactory and is driven through ILightEngine the way K3
// drives it: frames at the full rate, 10 Hz frames while idle or listening, no calls at all while still, and one
// ordinary frame's dt after a gap.
//   1. IsStatic means time alone changes nothing.
//   2. Fully hidden is Visibility == 0; anything under 0.5/255 counts as hidden (the overlay windows are hidden then).
//   3. Idle Glow changes at most about 0.25 per second, and the silence → Idle Glow fade is done 1.75 s after IsSilent.
//   4. After a gap, Update gets one ordinary frame's dt, so fades start on the first frame after the input; a long frame
//      right after a short wake frame doesn't skip into the fade either.
public sealed class RenderLoopContractTests(ITestOutputHelper output)
{
    private const float Dt = 1f / 60;
    private const float SlowDt = 0.1f;                      // K3's 10 Hz frames (FramePacer.SlowInterval)
    private const float HiddenVisibility = 0.5f / 255;     // GlowConstants.MinVisibility: windows hide below this
    private const float FastChangePerSecond = 0.25f;        // FramePacer.FastChangePerSecond
    private const float IdleSettleSeconds = 1.75f;          // MusicGlowSource: 10 Hz from 1.75 s after IsSilent

    private static readonly Settings Music = new();
    private static readonly Settings Hide = new() { WhenSilent = SilentBehavior.Hide };
    private static readonly Settings Idle = new() { Animation = AnimationMode.IdleGlow };
    private static readonly Settings Off = new() { Animation = AnimationMode.Off };
    private static readonly Settings Disabled = new() { Enabled = false };
    private static readonly AudioFeatures Loud = new(0.6f, 0.5f, 0f, false);
    private static readonly AudioFeatures Silent = new(0f, 0f, 0f, true); // also what MusicGlowSource passes outside music sync

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    // The ways K3 sees a glow go static, and the input that brings each back.
    private static (Settings Settings, AudioFeatures Audio, bool Paused) Hiding(string reason) => reason switch
    {
        "paused" => (Music, Loud, true),
        "off" => (Off, Loud, false),
        "disabled" => (Disabled, Loud, false),
        "silent-hide" => (Hide, Silent, false),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static Settings Showing(string reason) => reason == "silent-hide" ? Hide : Music;

    // Seconds the fade that brings the light back takes: the 300 ms gate, or 150 ms when sound returns.
    private static float WakeFadeSeconds(string reason) => reason == "silent-hide" ? 0.15f : 0.3f;

    private static ILightEngine Visible(Settings settings, AudioFeatures audio)
    {
        ILightEngine engine = CoreFactory.CreateLightEngine();
        for (int i = 0; i < 180; i++) engine.Update(Dt, audio, Palette.Default, settings, false);
        Assert.Equal(1f, engine.Update(Dt, audio, Palette.Default, settings, false).Visibility);
        return engine;
    }

    // Visible first, then hidden by `reason` until static, with the state of the first static frame.
    private static (ILightEngine Engine, LightState State) Static(string reason)
    {
        var (settings, audio, paused) = Hiding(reason);
        ILightEngine engine = Visible(Showing(reason), Loud);
        LightState state = default;
        for (int i = 0; i < 200 && !engine.IsStatic; i++) state = engine.Update(Dt, audio, Palette.Default, settings, paused);
        Assert.True(engine.IsStatic);
        return (engine, state);
    }

    private static int[] Bits(in LightState s) =>
    [
        BitConverter.SingleToInt32Bits(s.ColorA.R), BitConverter.SingleToInt32Bits(s.ColorA.G), BitConverter.SingleToInt32Bits(s.ColorA.B),
        BitConverter.SingleToInt32Bits(s.ColorB.R), BitConverter.SingleToInt32Bits(s.ColorB.G), BitConverter.SingleToInt32Bits(s.ColorB.B),
        BitConverter.SingleToInt32Bits(s.Ratio), BitConverter.SingleToInt32Bits(s.Intensity), BitConverter.SingleToInt32Bits(s.Spread),
        BitConverter.SingleToInt32Bits(s.CoreThicknessDip), BitConverter.SingleToInt32Bits(s.Phase), BitConverter.SingleToInt32Bits(s.Pulse),
        BitConverter.SingleToInt32Bits(s.Visibility),
    ];

    // FramePacer.ChangeRate restated: the fastest change of anything the glow shows, per second (colors reach the
    // screen through the gradient and are left out).
    private static float ChangeRate(in LightState a, in LightState b, float dt)
    {
        float phase = MathF.Abs(b.Phase - a.Phase) % 1;
        float change = MathF.Max(
            MathF.Max(MathF.Max(MathF.Abs(b.Intensity - a.Intensity), MathF.Abs(b.Visibility - a.Visibility)),
                MathF.Max(MathF.Abs(b.Pulse - a.Pulse), MathF.Abs(b.Spread - a.Spread))),
            MathF.Max(MathF.Max(MathF.Abs(b.Ratio - a.Ratio), MathF.Abs(b.CoreThicknessDip - a.CoreThicknessDip) / 40),
                MathF.Min(phase, 1 - phase)));
        return change / dt;
    }

    // ---- 1. IsStatic means time alone changes nothing ----

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    [InlineData("silent-hide")]
    public void WhileStaticNoDtChangesTheState(string reason)
    {
        // With the same inputs, any dt K3 might pass (or one it never should) returns the first static state bit for bit.
        var (settings, audio, paused) = Hiding(reason);
        var (engine, first) = Static(reason);
        Assert.Equal(0f, first.Visibility);
        float[] dts = [0, 1f / 240, Dt, SlowDt, 0.25f, 1, 3600, float.NaN, -1, float.PositiveInfinity, float.Epsilon];
        for (int i = 0; i < 1000; i++)
        {
            LightState state = engine.Update(dts[i % dts.Length], audio, Palette.Default, settings, paused);
            Assert.Equal(Bits(first), Bits(state));
            Assert.True(engine.IsStatic);
        }
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    [InlineData("silent-hide")]
    public void HowOftenK3CallsAStaticEngineChangesNothingAfterwards(string reason)
    {
        // K3 stops calling a still glow (Off, disabled, paused) and polls a listening one (Hide) at 10 Hz. Neither, nor
        // 60 fps frames, may change how the light comes back: the wake sequences match bit for bit.
        var (settings, audio, paused) = Hiding(reason);
        List<int[]>? reference = null;
        foreach ((float dt, int calls) in new[] { (0f, 0), (SlowDt, 300), (Dt, 1800), (0.37f, 7) })
        {
            var (engine, _) = Static(reason);
            for (int i = 0; i < calls; i++) engine.Update(dt, audio, Palette.Default, settings, paused);
            var wake = new List<int[]>();
            for (int i = 0; i < 60; i++) wake.Add(Bits(engine.Update(Dt, Loud, Palette.Default, Showing(reason), false)));
            reference ??= wake;
            Assert.Equal(reference, wake);
        }
        Assert.Equal(BitConverter.SingleToInt32Bits(1f), reference![^1][12]); // and the light is fully back
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void AStillGlowWakesOnlyOnAnInputK3Watches(string reason)
    {
        // K3 stops calling Update when a glow is static outside music sync, so audio must never be what would wake it:
        // only a settings or pause change, which K3 sees, can. Audio here is anything the analyzer could report.
        var (settings, _, paused) = Hiding(reason);
        var (engine, _) = Static(reason);
        var random = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var audio = new AudioFeatures((float)random.NextDouble(), (float)random.NextDouble(), random.Next(4) == 0 ? 1 : 0, random.Next(2) == 0);
            Assert.Equal(0f, engine.Update(i % 3 == 0 ? SlowDt : Dt, audio, Palette.Default, settings, paused).Visibility);
            Assert.True(engine.IsStatic);
        }
        engine.Update(0, Loud, Palette.Default, Music, false);   // the input changes; K3 renders a frame at once
        Assert.False(engine.IsStatic);
        Assert.True(engine.Update(Dt, Loud, Palette.Default, Music, false).Visibility > HiddenVisibility);
    }

    [Fact]
    public void TheTenHertzPollHearsTheMusicReturn()
    {
        // Music sync with When silent = Hide: K3 keeps polling at 10 Hz. The poll that hears sound wakes the engine and
        // is already above the hidden threshold, so K3 shows the window and goes to the full rate on that frame.
        var (engine, _) = Static("silent-hide");
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(0f, engine.Update(SlowDt, Silent, Palette.Default, Hide, false).Visibility);
            Assert.True(engine.IsStatic);
        }
        LightState heard = engine.Update(SlowDt, Loud, Palette.Default, Hide, false);
        Assert.False(engine.IsStatic);
        Assert.True(heard.Visibility > HiddenVisibility, $"Visibility {heard.Visibility}");
        var back = new List<float>();
        for (int i = 0; i < 8; i++) back.Add(engine.Update(Dt, Loud, Palette.Default, Hide, false).Visibility);
        Assert.True(back[6] < 1);
        Assert.Equal(1f, back[7]);                              // 150 ms: the poll counts as one 60 fps frame
    }

    // ---- 2. Fully hidden is Visibility == 0 ----

    [Theory]
    [InlineData("paused", 30f)]
    [InlineData("paused", 240f)]
    [InlineData("off", 60f)]
    [InlineData("disabled", 144f)]
    [InlineData("silent-hide", 30f)]
    [InlineData("silent-hide", 60f)]
    [InlineData("silent-hide", 240f)]
    public void EveryFadeOutEndsOnExactlyZeroAndThenGoesStatic(string reason, float fps)
    {
        // Visibility falls steadily, lands on 0 exactly (never a tiny positive value that would leave the windows
        // hidden but the engine moving), stays there, and IsStatic follows on the next frame. The values under 0.5/255
        // last at most the final 40 ms of the fade (the 1.5 s Hide fade's smoothstep tail).
        var (settings, audio, paused) = Hiding(reason);
        ILightEngine engine = Visible(Showing(reason), Loud);
        float dt = 1 / fps, previous = 1, underThresholdSeconds = 0;
        int zeroFrame = -1;
        for (int i = 0; i < (int)(2 * fps); i++)
        {
            float v = engine.Update(dt, audio, Palette.Default, settings, paused).Visibility;
            Assert.True(v <= previous, $"Visibility rose {previous} → {v}");
            previous = v;
            if (v > 0 && v < HiddenVisibility) underThresholdSeconds += dt;
            if (zeroFrame < 0 && v == 0)
            {
                zeroFrame = i;
                Assert.Equal(0, BitConverter.SingleToInt32Bits(v)); // +0, not −0 or a denormal
            }
            Assert.Equal(zeroFrame >= 0 && i > zeroFrame, engine.IsStatic);
        }
        output.WriteLine($"{reason} at {fps} fps: 0 at frame {zeroFrame}, {underThresholdSeconds * 1000:F1} ms under 0.5/255 first");
        Assert.True(zeroFrame > 0);
        Assert.InRange(underThresholdSeconds, 0, 0.04f + dt);
    }

    [Fact]
    public void VisibilityNeverRestsBetweenZeroAndTheHiddenThreshold()
    {
        // Over an hour of random mode, pause, silence and Hide switches at random frame rates: whenever the inputs stay
        // the same, Visibility is never left between 0 and 0.5/255, a Visibility of 0 on two frames in a row means
        // IsStatic, and IsStatic always means Visibility == 0.
        ILightEngine engine = CoreFactory.CreateLightEngine();
        var random = new Random(11);
        Settings[] modes = [Music, Hide, Idle, Off, Disabled];
        Settings settings = Music;
        AudioFeatures audio = Loud;
        bool paused = false;
        float lastVisibility = -1;
        bool inputsChanged = true;
        int restingChecks = 0;
        for (int i = 0; i < 200_000; i++)
        {
            if (random.Next(150) == 0) { settings = modes[random.Next(modes.Length)]; inputsChanged = true; }
            if (random.Next(300) == 0) { paused = !paused; inputsChanged = true; }
            if (random.Next(200) == 0) { audio = audio.IsSilent ? Loud : Silent; inputsChanged = true; }
            float dt = random.Next(4) switch { 0 => 1f / 240, 1 => Dt, 2 => 1f / 30, _ => SlowDt };
            float v = engine.Update(dt, audio, Palette.Default, settings, paused).Visibility;
            if (engine.IsStatic) Assert.Equal(0f, v);
            if (!inputsChanged)
            {
                restingChecks++;
                Assert.False(v > 0 && v < HiddenVisibility && v == lastVisibility, $"Visibility rests at {v}");
                if (v == 0 && lastVisibility == 0) Assert.True(engine.IsStatic, $"frame {i}: hidden but not static");
            }
            lastVisibility = v;
            inputsChanged = false;
        }
        Assert.True(restingChecks > 190_000);
    }

    // ---- 3. Idle Glow is slow; the silence → Idle Glow fade is done 1.75 s after IsSilent ----

    [Theory]
    [InlineData(1f, 10f)]
    [InlineData(1f, 60f)]
    [InlineData(0.8f, 10f)]
    [InlineData(0.1f, 10f)]
    public void IdleGlowNeverChangesFasterThanAQuarterPerSecond(float brightness, float fps)
    {
        // K3 draws Idle Glow at 10 fps; anything over 0.25 per second in Intensity, Visibility, Pulse, Spread, Ratio,
        // core thickness or phase would visibly step there. Breathing ±10 % over 6 s moves at most 0.094 × Brightness
        // per second. Audio is whatever the analyzer reports: Idle Glow ignores it.
        ILightEngine engine = CoreFactory.CreateLightEngine();
        Settings settings = Idle with { Brightness = brightness };
        var random = new Random(3);
        float dt = 1 / fps, fastest = 0;
        LightState previous = default;
        for (int i = 0; i < (int)(40 * fps); i++)
        {
            var audio = new AudioFeatures((float)random.NextDouble(), 0.5f, random.Next(8) == 0 ? 1 : 0, random.Next(2) == 0);
            LightState state = engine.Update(dt, audio, Palette.Default, settings, false);
            if (i * dt >= 0.5f) fastest = MathF.Max(fastest, ChangeRate(previous, state, dt)); // after the 300 ms fade-in
            previous = state;
        }
        output.WriteLine($"Brightness {brightness} at {fps} fps: fastest change {fastest:F4} per second");
        Assert.InRange(fastest, 0.05f * brightness, FastChangePerSecond);
        Assert.InRange(fastest, 0, 0.1f * brightness);
    }

    [Theory]
    [InlineData(60f, 0f, false)]
    [InlineData(30f, 0f, false)]
    [InlineData(144f, 0.2f, false)]
    [InlineData(240f, 0.2f, false)]
    [InlineData(60f, 0.2f, true)]
    public void SilenceHandsOverToIdleGlowBeforeK3SlowsDown(float fps, float jitter, bool beatsWhileSilent)
    {
        // Music sync with When silent = Idle Glow. K3 drops to 10 fps once IsSilent has held for 1.75 s (counted like
        // MusicGlowSource: the dt of every frame since the first silent one). By then the crossfade must be over: Level
        // no longer matters (two engines that hear different levels agree bit for bit), Pulse is 0 even if the analyzer
        // still reports a beat, the phase has stopped (unless beats keep arriving) and nothing moves faster than 0.25
        // per second. The last beat lands on the first silent frame, the latest a beat's push can start.
        ILightEngine a = CoreFactory.CreateLightEngine(), b = CoreFactory.CreateLightEngine();
        var random = new Random(5);
        float beat = 0, silentSeconds = 0, settledPhase = float.NaN;
        float done = float.NaN; // silentSeconds from which the two engines agree for good
        LightState previous = default;
        double time = 0;
        while (time < 15)
        {
            float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
            time += dt;
            bool silent = time >= 5;
            silentSeconds = silent ? silentSeconds + dt : 0;
            bool onBeat = (!silent && random.Next((int)(fps / 2)) == 0) || silentSeconds == dt
                || (beatsWhileSilent && silent && random.Next((int)(fps / 2)) == 0);
            beat = onBeat ? 1 : beat * MathF.Exp(-dt / 0.12f);
            float level = (float)random.NextDouble();
            LightState stateA = a.Update(dt, new AudioFeatures(level, 0.5f, beat, silent), Palette.Default, Music, false);
            LightState stateB = b.Update(dt, new AudioFeatures(silent ? 1 - level : level, 0.5f, beat, silent), Palette.Default, Music, false);
            bool agree = Bits(stateA).SequenceEqual(Bits(stateB));
            if (!silent || !agree) done = float.NaN;
            else if (float.IsNaN(done)) done = silentSeconds;
            if (silentSeconds >= IdleSettleSeconds)
            {
                Assert.Equal(Bits(stateA), Bits(stateB));
                Assert.Equal(0f, stateA.Pulse);
                Assert.Equal(1f, stateA.Visibility);
                Assert.InRange(stateA.Intensity, 0.8f * 0.9f * 0.9f - 1e-6f, 0.8f * 0.9f * 1.1f + 1e-6f);
                if (float.IsNaN(settledPhase)) settledPhase = stateA.Phase;
                else
                {
                    if (!beatsWhileSilent) Assert.Equal(settledPhase, stateA.Phase);
                    Assert.InRange(ChangeRate(previous, stateA, dt), 0, FastChangePerSecond);
                }
            }
            previous = stateA;
        }
        output.WriteLine($"{fps} fps ±{jitter * 100}%: Idle Glow reached {done:F3} s after IsSilent (K3 slows at {IdleSettleSeconds} s)");
        Assert.InRange(done, 1.4f, IdleSettleSeconds);          // a fade (1.5 s in doc 01 §2), done in time
    }

    // ---- 4. dt after a gap: one ordinary frame, so fades start on the first frame after the input ----

    [Theory]
    [InlineData("paused", 1f / 240)]
    [InlineData("paused", 1f / 60)]
    [InlineData("off", 1f / 144)]
    [InlineData("disabled", 1f / 30)]
    [InlineData("silent-hide", 1f / 240)]
    [InlineData("silent-hide", 1f / 60)]
    [InlineData("silent-hide", 1f / 30)]
    public void AWakeFadeStartsOnTheFirstFrameFromItsBeginning(string reason, float frameDt)
    {
        // After a static gap of any length K3 passes one ordinary frame's dt. The fade that brings the light back starts
        // on that frame, at its beginning (after a static frame the engine counts each step as at most 1/60 s until they
        // add up to 1/60 s), and runs at the frame rate from there. A host that passed the whole gap instead would get
        // one 60 fps step.
        float fade = WakeFadeSeconds(reason), first = Smooth(MathF.Min(frameDt, Dt) / fade);
        foreach ((float gapDt, float expected) in new[] { (frameDt, first), (5f, Smooth(Dt / fade)), (3600f, Smooth(Dt / fade)) })
        {
            var (engine, _) = Static(reason);
            Assert.Equal(expected, engine.Update(gapDt, Loud, Palette.Default, Showing(reason), false).Visibility, 1e-6f);
            Assert.False(engine.IsStatic);
        }

        var (woken, _) = Static(reason);
        float elapsed = 0, v = 0;
        int frames = 0;
        while (v < 1 && frames < 1000)
        {
            v = woken.Update(frameDt, Loud, Palette.Default, Showing(reason), false).Visibility;
            elapsed += frames == 0 ? MathF.Min(frameDt, Dt) : frameDt;
            frames++;
            Assert.True(v > 0);
        }
        output.WriteLine($"{reason}, frames of {frameDt * 1000:F2} ms: first frame {first:E2}, full after {frames} frames ({elapsed * 1000:F1} ms of fade)");
        Assert.InRange(elapsed, fade - 1e-4f, fade + frameDt);
    }

    [Theory]
    [InlineData("start", 50e-6f, 0.2f)]
    [InlineData("start", 1f / 144, 0.1f)]
    [InlineData("paused", 50e-6f, 0.25f)]
    [InlineData("paused", 1f / 144, 0.1f)]
    [InlineData("disabled", 1f / 240, 0.1f)]
    [InlineData("off", 1f / 360, 0.1f)]
    [InlineData("silent-hide", 1f / 240, 0.1f)]
    public void AShortWakeStepDoesNotLetALongOneJump(string reason, float firstDt, float longDt)
    {
        // A wake frame can be short and the next one long, and the second still must not jump into the fade. K3's very
        // first frame comes microseconds after its loop starts, and the GPU setup that frame runs makes the next dt up to
        // 0.25 s; above about 130 Hz a 300 ms fade-in's first frame is still under 0.5/255, and a host that then waits
        // 100 ms passes that. The engine counts each step at most 1/60 s until the steps since waking add up to 1/60 s,
        // so the long frame continues the fade from its beginning (it used to count 0.1 s: about 26 % at once).
        bool start = reason == "start";
        float fade = start ? 0.3f : WakeFadeSeconds(reason);
        Settings showing = start ? Music : Showing(reason);
        ILightEngine engine = start ? CoreFactory.CreateLightEngine() : Static(reason).Engine;

        Assert.Equal(Smooth(firstDt / fade), engine.Update(firstDt, Loud, Palette.Default, showing, false).Visibility, 1e-6f);
        float second = engine.Update(longDt, Loud, Palette.Default, showing, false).Visibility;
        output.WriteLine($"{reason}: {firstDt * 1000:F2} ms then {longDt * 1000:F0} ms → Visibility {second:E2}");
        Assert.Equal(Smooth((firstDt + Dt) / fade), second, 1e-5f);
        Assert.True(second > HiddenVisibility);
        // Once the steps since waking reach 1/60 s, ordinary steps count in full again (up to 0.1 s).
        float third = engine.Update(longDt, Loud, Palette.Default, showing, false).Visibility;
        Assert.Equal(Smooth(MathF.Min(1, (firstDt + Dt + MathF.Min(longDt, 0.1f)) / fade)), third, 1e-5f);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void AnInputBetweenTenHertzFramesStartsItsFadeOnTheNextFrame(string reason)
    {
        // Idle Glow at 10 fps; an input arrives part way between two frames and K3 renders at once with one ordinary
        // frame's dt (not the time since the last 10 Hz frame). The 300 ms fade-out starts from its beginning there and
        // ends on the 18th 60 fps frame. Then the input is undone after a still gap and the fade-in starts the same way.
        bool paused = reason == "paused";
        Settings idleHidden = reason switch
        {
            "off" => Idle with { Animation = AnimationMode.Off },
            "disabled" => Idle with { Enabled = false },
            _ => Idle,
        };
        ILightEngine engine = CoreFactory.CreateLightEngine();
        for (int i = 0; i < 30; i++) engine.Update(SlowDt, Silent, Palette.Default, Idle, false);
        var fadeOut = new List<float>();
        for (int i = 0; i < 18; i++) fadeOut.Add(engine.Update(Dt, Silent, Palette.Default, idleHidden, paused).Visibility);
        Assert.Equal(Smooth(1 - Dt / 0.3f), fadeOut[0], 1e-5f);
        Assert.True(fadeOut[16] > 0);
        Assert.Equal(0f, fadeOut[17]);
        engine.Update(Dt, Silent, Palette.Default, idleHidden, paused);
        Assert.True(engine.IsStatic);                           // K3 stops calling: a still glow

        LightState back = engine.Update(Dt, Silent, Palette.Default, Idle, false);
        Assert.Equal(Smooth(Dt / 0.3f), back.Visibility, 1e-6f);
        Assert.True(back.Visibility > HiddenVisibility);
    }
}
