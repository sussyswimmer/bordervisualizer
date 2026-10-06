namespace Rimlight.Core.Lighting;

// The real ILightEngine (doc 07 Phase 3, doc 01 §2, doc 04 §4, doc 02 idle optimization, HANDOFF H-007 and H-008).
// One instance belongs to the render thread; Update never allocates. Time comes only from dtSeconds: no clock reads,
// no randomness, so equal input sequences give bit-identical output.
//
// Three fades, each a linear 0..1 progress shown through smoothstep so it starts and ends without a kink:
//   gate   closes for pause, Enabled = false and Animation = Off; 300 ms out and in (doc 04 §4).
//   shown  closes in Music Sync with WhenSilent = Hide while IsSilent; 1.5 s out, 150 ms back (doc 01 §2).
//   music  is the Music Sync weight against Idle Glow: 0 in Idle Glow, and in Music Sync with WhenSilent = IdleGlow
//          while IsSilent; 1.5 s toward Idle Glow, 150 ms back (doc 01 §2).
// Visibility = gate × shown (H-008 item 2: Visibility carries every fade, Intensity none). Intensity, Pulse and the
// drift crossfade with the music weight.
internal sealed class LightEngine : ILightEngine
{
    internal const float LevelFloor = 0.35f;              // doc 07 Phase 3: Brightness × (0.35 + 0.65 × Level)
    internal const float DriftCyclesPerSecond = 0.015f;   // doc 07 Phase 3
    internal const float BeatKickCycles = 0.01f;          // doc 07 Phase 3, per beat
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
    // instead. The first step after a static frame counts at most one 60 fps frame: a renderer that idled for an hour
    // may pass the whole hour, and the fade-in must still start from its first frame.
    internal const float MaxStepSeconds = 0.1f;
    internal const float WakeStepSeconds = 1f / 60;
    // A beat is a rise of Beat by more than this; it re-arms once Beat falls. The analyzer jumps from ≤ 0.22 to 1.
    private const float OnsetRise = 0.1f;
    private const float Settle = 1e-4f;                   // fades snap to their target this close to it
    private const double KickRest = 1e-7;                 // a push with less than this left to travel is dropped

    private static readonly Settings Defaults = new();

    private float gate, shown = 1, music = 1, lastBeat;
    private double phase, breath, kick;                   // cycles, seconds, cycles per second
    private bool armed = true, wasHidden;

    // True after the second Update in a row with the light hidden for good (see CoreFactory.CreateLightEngine).
    public bool IsStatic { get; private set; }

    // Tests only: the unrounded phase, to reach values a float rounds up to 1.
    internal double PhaseCycles
    {
        get => phase;
        set => phase = value - Math.Floor(value);
    }

    public LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(settings);
        float step = float.IsFinite(dtSeconds) && dtSeconds > 0 ? MathF.Min(dtSeconds, IsStatic ? WakeStepSeconds : MaxStepSeconds) : 0;

        // Targets come straight from the inputs every frame, so no settings cache can go stale. An undefined
        // Animation value behaves like Music Sync and an undefined WhenSilent like IdleGlow (the defaults).
        AnimationMode animation = settings.Animation;
        float gateTarget = !paused && settings.Enabled && animation != AnimationMode.Off ? 1 : 0;
        float shownTarget = shown, musicTarget = music;   // Off keeps the look it fades out with
        if (animation != AnimationMode.Off)
        {
            bool idle = animation == AnimationMode.IdleGlow;
            bool hideWhenSilent = settings.WhenSilent == SilentBehavior.Hide;
            bool quiet = !idle && audio.IsSilent;
            shownTarget = quiet && hideWhenSilent ? 0 : 1;
            musicTarget = idle || (quiet && !hideWhenSilent) ? 0 : 1;
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
        gate = Approach(gate, gateTarget, step / GateFadeSeconds);
        // A closing gate freezes the other two, so the fade-out keeps the look it started with. Otherwise pausing a
        // light hidden by silence (WhenSilent = Hide) could raise `shown` while the gate falls: a flash.
        if (gateTarget == 1)
        {
            shown = Approach(shown, shownTarget, step / (shownTarget < shown ? QuietFadeSeconds : WakeFadeSeconds));
            music = Approach(music, musicTarget, step / (musicTarget < music ? QuietFadeSeconds : WakeFadeSeconds));
        }
        // Hidden: Visibility is 0 and stays 0 until an input changes.
        bool hidden = (gate == 0 && gateTarget == 0) || (shown == 0 && (shownTarget == 0 || gateTarget == 0));

        float beat = Unit(audio.Beat);
        bool onset = armed && beat > lastBeat + OnsetRise;
        if (onset) armed = false;
        else if (beat < lastBeat) armed = true;
        lastBeat = beat;

        float weight = Smooth(music);
        if (hidden)
        {
            // Nothing moves while hidden, so the output depends only on the inputs and a renderer may stop calling.
            kick = 0;
        }
        else
        {
            if (onset) kick += BeatKickCycles / KickSeconds * weight;
            // Exact integration of a push decaying with τ = KickSeconds: the total is BeatKickCycles at any frame rate.
            double decay = Math.Exp(-step / KickSeconds);
            double push = kick * KickSeconds * (1 - decay);
            kick *= decay;
            if (kick * KickSeconds < KickRest) kick = 0;
            phase += (double)DriftCyclesPerSecond * weight * step + push;
            phase -= Math.Floor(phase);
            breath += step;
            if (breath >= BreathPeriodSeconds) breath -= BreathPeriodSeconds;
        }

        float brightness = Clamp(settings.Brightness, 0.1f, 1f, Defaults.Brightness);
        float musicIntensity = brightness * (LevelFloor + (1 - LevelFloor) * Unit(audio.Level));
        float idleIntensity = brightness * IdleLevel * (1 + BreathDepth * MathF.Sin(2 * MathF.PI * (float)(breath / BreathPeriodSeconds)));
        float outputPhase = (float)phase;
        if (outputPhase >= 1) outputPhase = 0;            // a phase just below 1 can round up to 1 as a float

        IsStatic = hidden && wasHidden;                   // the frame that reached Visibility 0 is still presented
        wasHidden = hidden;
        return new LightState(
            Color(palette.Primary),
            Color(palette.Secondary),
            Clamp(settings.PrimaryRatio, 0.1f, 0.9f, Defaults.PrimaryRatio),
            Math.Clamp(idleIntensity + (musicIntensity - idleIntensity) * weight, 0, 1),
            Clamp(settings.Glow, 0, 1, Defaults.Glow),
            Clamp(settings.CoreThicknessDip, 0, 40, Defaults.CoreThicknessDip),
            outputPhase,
            beat * weight,
            Smooth(gate) * Smooth(shown));
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
}
