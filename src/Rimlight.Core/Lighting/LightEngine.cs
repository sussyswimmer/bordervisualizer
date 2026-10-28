namespace Rimlight.Core.Lighting;

// The real ILightEngine (doc 07 Phase 3, doc 01 §2, doc 04 §4, doc 02 idle optimization, HANDOFF H-007, H-008 and
// H-033). One instance belongs to the render thread; Update never allocates. Time comes only from dtSeconds: no clock
// reads, no randomness, so equal input sequences give bit-identical output.
//
// Four fades, each a linear 0..1 progress shown through smoothstep so it starts and ends without a kink:
//   gate   closes for pause, Enabled = false and Animation = Off; 300 ms out and in (doc 04 §4).
//   shown  closes in Music Sync with WhenSilent = Hide while IsSilent; 1.5 s out, 150 ms back (doc 01 §2).
//   music  is the Music Sync weight against Idle Glow: 0 in Idle Glow, and in Music Sync with WhenSilent = IdleGlow
//          while IsSilent; 1.5 s toward Idle Glow, 150 ms back (doc 01 §2). A Hide fade keeps the weight it starts
//          with, as Off does, so the light fades out with the look it had.
//   flow   is the Movement setting: 1 in Flow, 0 in Pulse; 300 ms either way (H-033). A Hide fade keeps the movement
//          it starts with too, as it keeps the music weight.
// Visibility = gate × shown (H-008 item 2: Visibility carries every fade, Intensity none). Intensity, Pulse and the
// drift crossfade with the music weight.
//
// The Flow movement (H-033; Maxwell: "I want the rimlight to be like the video I sent where it moves around and doesn't
// just bounce"). Light travels round the edge in three ways: a comet chases round it, faster when the music is
// louder and pushed along by each beat, at about the reference clip's lap of 2-2.5 s on a loud song with a steady
// beat; the colors drift round it faster; and each beat sends a ripple running both
// ways along it from one spot, while the whole-edge pulse keeps only a share of the beat. The renderers draw the comets
// and the ripples (Platform's GlowProfile is the reference math); the engine only moves them. How much of it shows,
// the Flow amount, is the flow fade × the music weight, so Idle Glow has none and it fades with the music like
// Intensity and Pulse. In Pulse (flow 0) every output is exactly what it was before Flow existed: the Pulse path
// computes the same expressions in the same order, and the Flow output is none.
internal sealed class LightEngine : ILightEngine
{
    internal const float LevelFloor = 0.35f;              // doc 07 Phase 3: Brightness × (0.35 + 0.65 × Level)
    internal const float DriftCyclesPerSecond = 0.015f;   // doc 07 Phase 3
    internal const float BeatKickCycles = 0.01f;          // doc 07 Phase 3, per full beat (Beat 1)
    // Each kick is delivered as a push that decays with this time constant (the analyzer's Beat decay, doc 03):
    // 63 % lands within 120 ms. At 120 BPM an instant 0.01 step would visibly tick the gradient twice a second.
    internal const float KickSeconds = 0.12f;
    internal const float IdleLevel = 0.9f;                // Idle Glow center, × Brightness: the peak is 0.99 × Brightness
    internal const float BreathDepth = 0.1f;              // doc 01 §2: ±10 %
    internal const float BreathPeriodSeconds = 6f;        // doc 01 §2: about a 6 s sine
    internal const float GateFadeSeconds = 0.3f;          // doc 04 §4 pause fade; also Enabled = false and Off
    internal const float QuietFadeSeconds = 1.5f;         // doc 01 §2: fade to the WhenSilent behavior
    internal const float WakeFadeSeconds = 0.15f;         // doc 01 §2: back within 150 ms when audio returns
    // Steps above 0.1 s (a stall, sleep) count as 0.1 s, so no fade or kick jumps; under 10 fps everything slows down
    // instead. On a new engine, and after a static frame, each step counts at most one 60 fps frame until the steps
    // since waking add up to one (dt = 0 updates add nothing): a renderer that spent half a second building swap
    // chains, or idled for an hour, may pass all of that time, and the fade-in must still start from its first frame.
    // The total matters, not just the first step: a wake frame of a few microseconds (K3's first frame, before its
    // GPU setup) or of 1/144 s may be followed by a long one, which would otherwise count 0.1 s, a third of the
    // fade-in (H-011).
    internal const float MaxStepSeconds = 0.1f;
    internal const float WakeStepSeconds = 1f / 60;
    // A beat is a rise of Beat by more than this; it re-arms once Beat falls. Its push is BeatKickCycles × the height
    // the pulse rises to (Maxwell's feedback, round 1: the analyzer grades each beat 0..1, so a soft beat nudges the
    // gradient and a kick pushes it the full 0.01 cycles), counting a rise that continues over the next frames.
    private const float OnsetRise = 0.1f;
    private const float Settle = 1e-4f;                   // fades snap to their target this close to it
    private const double KickRest = 1e-7;                 // a push with less than this left to travel is dropped

    // The Flow movement (H-033). Speeds are in perimeters (cycles) a second, at the full Flow amount.
    internal const float FlowFadeSeconds = 0.3f;          // the Movement setting's crossfade between Flow and Pulse
    internal const float CometBaseCyclesPerSecond = 0.15f;    // the comet with the music at Level 0: a lap in 6.7 s
    internal const float CometLevelCyclesPerSecond = 0.3f;    // added at Level 1, so 0.45 laps a second at full loudness
    // Each full beat pushes the comet this far, × the height Beat rises to, delivered like the colors' kick
    // (KickSeconds): a quick shove along, not a jump. At Level 0.5 and 120 BPM it laps in about 2.2 s.
    internal const float CometKickCycles = 0.08f;
    internal const float FlowDriftCyclesPerSecond = 0.04f;    // the colors' drift in Flow at Level 0 (Pulse: 0.015)
    internal const float FlowDriftLevelCyclesPerSecond = 0.06f; // added at Level 1, so 0.1 cycles a second at most
    internal const float RippleSpeed = 0.45f;             // perimeters a second each of a ripple's two fronts runs
    internal const float RippleDecaySeconds = 0.4f;       // a ripple's strength is its start × exp(-age / this)
    // Each ripple starts this far round from the one before (the golden ratio's fraction): never the same spot twice
    // in a row, and over a song the beats land evenly all round the edge without a visible pattern.
    internal const float RippleOriginStep = 0.618034f;
    internal const float RippleRest = 1f / 512;           // a weaker ripple is dropped (under a code value at most)
    internal const int RippleSlots = 4;                   // at most four at once: LightState carries four
    // In Flow the whole-edge pulse keeps this share of the beat; the ripple carries the rest, along the edge.
    internal const float FlowPulseShare = 0.4f;

    private static readonly Settings Defaults = new();

    private float gate, shown = 1, music = 1, lastBeat;
    private float flow = 1;                               // starts at the default's target; a new engine jumps anyway
    private double phase, breath, kick;                   // cycles, seconds, cycles per second
    private double cometPhase, cometKick;                 // cycles, cycles per second
    private bool armed = true, wasHidden;
    private float awakeSeconds;                           // time moved since waking (see above), counted up to WakeStepSeconds

    // The beats' ripples, in slots that never move: a free slot has Start 0 (a beat that starts one is over 0.1).
    // newestRipple is the slot the latest beat started, while that beat's rise may continue; -1 when there is none.
    private readonly RippleSlot[] ripples = new RippleSlot[RippleSlots];
    private int newestRipple = -1;
    private float nextOrigin;

    // True after the second Update in a row with the light hidden for good (see CoreFactory.CreateLightEngine).
    public bool IsStatic { get; private set; }

    // Tests only: the unrounded phase, to reach values a float rounds up to 1.
    internal double PhaseCycles
    {
        get => phase;
        set => phase = value - Math.Floor(value);
    }

    // Tests only: the unrounded comet phase, likewise.
    internal double CometPhaseCycles
    {
        get => cometPhase;
        set => cometPhase = value - Math.Floor(value);
    }

    public LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(settings);
        float stepCap = awakeSeconds < WakeStepSeconds ? WakeStepSeconds : MaxStepSeconds;
        float step = float.IsFinite(dtSeconds) && dtSeconds > 0 ? MathF.Min(dtSeconds, stepCap) : 0;

        // Targets come straight from the inputs every frame, so no settings cache can go stale. An undefined
        // Animation value behaves like Music Sync, an undefined WhenSilent like IdleGlow and an undefined Movement like
        // Flow (the defaults).
        AnimationMode animation = settings.Animation;
        float gateTarget = !paused && settings.Enabled && animation != AnimationMode.Off ? 1 : 0;
        float shownTarget = shown, musicTarget = music;   // Off and a Hide fade keep the look they fade out with
        float flowTarget = settings.Movement == MovementStyle.Pulse ? 0 : 1;
        if (animation != AnimationMode.Off)
        {
            bool idle = animation == AnimationMode.IdleGlow;
            bool quiet = !idle && audio.IsSilent;
            if (quiet && settings.WhenSilent == SilentBehavior.Hide)
            {
                shownTarget = 0;
                flowTarget = flow;                    // the movement too; the wake frame jumps it to the setting
            }
            else
            {
                shownTarget = 1;
                musicTarget = idle || quiet ? 0 : 1;
            }
        }

        // While nothing shows, nothing needs a fade: jump to the targets so the light reappears in the right state
        // (Idle Glow straight away, not a Music Sync look that then crossfades).
        if (gate == 0)
        {
            shown = shownTarget;
            music = musicTarget;
        }
        else if (shown == 0)
        {
            music = musicTarget;
        }
        // The Movement jumps too whenever the last frame showed nothing (gate or shown at 0): there is nothing to fade,
        // and it never moves with time while IsStatic lets the renderer stop calling.
        if (gate == 0 || shown == 0) flow = flowTarget;
        // The gate jumps too while silence hides the light (shown = 0), so nothing moves while IsStatic lets the
        // renderer stop calling. Otherwise the gate would close or reopen one capped step per Update, and how far it
        // got would decide whether the light came back within 150 ms of sound or with the rest of a 300 ms fade.
        if (shown == 0) gate = gateTarget;
        gate = Approach(gate, gateTarget, step / GateFadeSeconds);
        // A closing gate freezes the other three, so the fade-out keeps the look it started with. Otherwise a light
        // that silence is fading out (WhenSilent = Hide) could be paused as sound returns, and `shown` would rise while
        // the gate falls: a flash.
        if (gateTarget == 1)
        {
            shown = Approach(shown, shownTarget, step / (shownTarget < shown ? QuietFadeSeconds : WakeFadeSeconds));
            music = Approach(music, musicTarget, step / (musicTarget < music ? QuietFadeSeconds : WakeFadeSeconds));
            flow = Approach(flow, flowTarget, step / FlowFadeSeconds);
        }
        // Hidden: Visibility is 0 and stays 0 until an input changes. (shown = 0 under a closed gate is the first case:
        // the gate jumped shut above.)
        bool hidden = (gate == 0 && gateTarget == 0) || (shown == 0 && shownTarget == 0);

        float beat = Unit(audio.Beat);
        float rise = 0;                                   // how much of a beat's height arrives on this frame
        bool onset = false;
        if (armed && beat > lastBeat + OnsetRise)
        {
            armed = false;
            rise = beat;
            onset = true;
        }
        else if (!armed && beat > lastBeat)
        {
            rise = beat - lastBeat;
        }
        else if (beat < lastBeat)
        {
            armed = true;
        }
        lastBeat = beat;

        float weight = Smooth(music);
        float flowShare = Smooth(flow);
        float amount = flowShare * weight;                // how much of the Flow movement shows
        float level = Unit(audio.Level);
        if (hidden)
        {
            // Nothing moves while hidden, so the output depends only on the inputs and a renderer may stop calling.
            kick = 0;
            cometKick = 0;
            ClearRipples();
        }
        else
        {
            kick += BeatKickCycles / KickSeconds * weight * rise;
            // Exact integration of a push decaying with τ = KickSeconds: the total is BeatKickCycles at any frame rate.
            double decay = Math.Exp(-step / KickSeconds);
            double push = kick * KickSeconds * (1 - decay);
            kick *= decay;
            if (kick * KickSeconds < KickRest) kick = 0;
            if (flowShare == 0)
            {
                phase += (double)DriftCyclesPerSecond * weight * step + push;
            }
            else
            {
                // Flow streams the colors faster, and faster still when the music is louder.
                float flowDrift = FlowDriftCyclesPerSecond + FlowDriftLevelCyclesPerSecond * level;
                float drift = DriftCyclesPerSecond + (flowDrift - DriftCyclesPerSecond) * flowShare;
                phase += (double)drift * weight * step + push;
            }
            phase -= Math.Floor(phase);
            breath += step;
            if (breath >= BreathPeriodSeconds) breath -= BreathPeriodSeconds;

            // The comets: a speed from Level, and each beat's push delivered like the colors' kick.
            cometKick += CometKickCycles / KickSeconds * amount * rise;
            double cometPush = cometKick * KickSeconds * (1 - decay);
            cometKick *= decay;
            if (cometKick * KickSeconds < KickRest) cometKick = 0;
            float cometSpeed = CometBaseCyclesPerSecond + CometLevelCyclesPerSecond * level;
            cometPhase += (double)cometSpeed * amount * step + cometPush;
            cometPhase -= Math.Floor(cometPhase);

            MoveRipples(step, beat, rise, onset, amount);
        }

        float brightness = Clamp(settings.Brightness, 0.1f, 1f, Defaults.Brightness);
        float musicIntensity = brightness * (LevelFloor + (1 - LevelFloor) * level);
        float idleIntensity = brightness * IdleLevel * (1 + BreathDepth * MathF.Sin(2 * MathF.PI * (float)(breath / BreathPeriodSeconds)));
        float outputPhase = (float)phase;
        if (outputPhase >= 1) outputPhase = 0;            // a phase just below 1 can round up to 1 as a float
        // In Flow the ripples carry most of the beat; in Pulse (flowShare 0) the pulse is the beat as before, exactly.
        float pulse = flowShare == 0 ? beat * weight : beat * weight * (1 - (1 - FlowPulseShare) * flowShare);

        IsStatic = hidden && wasHidden;                   // the frame that reached Visibility 0 is still presented
        wasHidden = hidden;
        awakeSeconds = IsStatic ? 0 : MathF.Min(awakeSeconds + step, WakeStepSeconds);
        return new LightState(
            Color(palette.Primary),
            Color(palette.Secondary),
            Clamp(settings.PrimaryRatio, 0.1f, 0.9f, Defaults.PrimaryRatio),
            Math.Clamp(idleIntensity + (musicIntensity - idleIntensity) * weight, 0, 1),
            Clamp(settings.Glow, 0, 1, Defaults.Glow),
            Clamp(settings.CoreThicknessDip, 0, 40, Defaults.CoreThicknessDip),
            outputPhase,
            pulse,
            Smooth(gate) * Smooth(shown))
        {
            Flow = FlowOutput(amount),
        };
    }

    // Ages the ripples, drops the ones that have faded, and starts or raises one for this frame's beat.
    private void MoveRipples(float step, float beat, float rise, bool onset, float amount)
    {
        for (int i = 0; i < RippleSlots; i++)
        {
            ref RippleSlot slot = ref ripples[i];
            if (slot.Start == 0) continue;
            slot.Age += step;
            slot.Fade();
            if (slot.Strength < RippleRest)
            {
                slot = default;
                if (newestRipple == i) newestRipple = -1;
            }
        }

        if (onset)
        {
            // A beat with no Flow showing (Pulse, Idle Glow) starts none, and its rise raises none.
            newestRipple = amount > 0 ? Weakest() : -1;
            if (newestRipple < 0) return;
            ripples[newestRipple] = new RippleSlot { Origin = nextOrigin, Age = 0, Start = beat, Strength = beat };
            nextOrigin += RippleOriginStep;
            nextOrigin -= MathF.Floor(nextOrigin);
        }
        else if (rise > 0 && newestRipple >= 0)
        {
            // The beat's rise goes on over the next frames: its ripple grows to the height the pulse reaches.
            ref RippleSlot slot = ref ripples[newestRipple];
            slot.Start = MathF.Max(slot.Start, beat);
            slot.Fade();
        }
    }

    // A free slot if there is one, else the faintest ripple's (at a fast tempo with slow decays); ties go to the lower
    // slot.
    private int Weakest()
    {
        int weakest = 0;
        float faintest = float.PositiveInfinity;
        for (int i = 0; i < RippleSlots; i++)
        {
            if (ripples[i].Start == 0) return i;
            float strength = ripples[i].Strength;
            if (strength < faintest)
            {
                faintest = strength;
                weakest = i;
            }
        }
        return weakest;
    }

    private void ClearRipples()
    {
        Array.Clear(ripples);
        newestRipple = -1;
    }

    // With no Flow showing (Pulse, Idle Glow) the output is none, so the renderers have nothing to compute and Pulse is
    // the light from before Flow existed, field for field.
    private FlowMotion FlowOutput(float amount)
    {
        if (!(amount > 0)) return default;
        float comets = (float)cometPhase;
        if (comets >= 1) comets = 0;                      // like Phase: just below 1 can round up to 1
        return new FlowMotion(amount, comets, RippleOutput(0), RippleOutput(1), RippleOutput(2), RippleOutput(3));
    }

    private Ripple RippleOutput(int slot)
    {
        RippleSlot r = ripples[slot];
        return r.Start == 0 ? default : new Ripple(r.Origin, RippleSpeed * r.Age, r.Strength);
    }

    private static float Approach(float value, float target, float delta)
    {
        value = value < target ? MathF.Min(value + delta, target) : MathF.Max(value - delta, target);
        return MathF.Abs(target - value) < Settle ? target : value;
    }

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    private static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static Rgb Color(Rgb c) => new(Unit(c.R), Unit(c.G), Unit(c.B));

    // One beat's ripple: where it started on the perimeter, its age in seconds, the beat's height (0: a free slot) and
    // its strength now, worked out once a frame.
    private struct RippleSlot
    {
        public float Origin, Age, Start, Strength;

        public void Fade() => Strength = Start * MathF.Exp(-Age / RippleDecaySeconds);
    }
}
