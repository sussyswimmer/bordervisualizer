using Rimlight.Core;

namespace Rimlight.Tests.Lighting;

// The light engine as it was before the Flow movement (H-033, base commit f5fd226), copied operation for operation:
// the Pulse movement must give exactly this LightState for every input sequence (PulseMovementTests). Don't tidy or
// "fix" it; it is the record of what Pulse looked like, not code to maintain. The comments that explain each rule are
// in LightEngine.
internal sealed class PulseReferenceEngine
{
    private const float LevelFloor = 0.35f;
    private const float DriftCyclesPerSecond = 0.015f;
    private const float BeatKickCycles = 0.01f;
    private const float KickSeconds = 0.12f;
    private const float IdleLevel = 0.9f;
    private const float BreathDepth = 0.1f;
    private const float BreathPeriodSeconds = 6f;
    private const float GateFadeSeconds = 0.3f;
    private const float QuietFadeSeconds = 1.5f;
    private const float WakeFadeSeconds = 0.15f;
    private const float MaxStepSeconds = 0.1f;
    private const float WakeStepSeconds = 1f / 60;
    private const float OnsetRise = 0.1f;
    private const float Settle = 1e-4f;
    private const double KickRest = 1e-7;

    private static readonly Settings Defaults = new();

    private float gate, shown = 1, music = 1, lastBeat;
    private double phase, breath, kick;
    private bool armed = true, wasHidden;
    private float awakeSeconds;

    public bool IsStatic { get; private set; }

    public LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(settings);
        float stepCap = awakeSeconds < WakeStepSeconds ? WakeStepSeconds : MaxStepSeconds;
        float step = float.IsFinite(dtSeconds) && dtSeconds > 0 ? MathF.Min(dtSeconds, stepCap) : 0;

        AnimationMode animation = settings.Animation;
        float gateTarget = !paused && settings.Enabled && animation != AnimationMode.Off ? 1 : 0;
        float shownTarget = shown, musicTarget = music;
        if (animation != AnimationMode.Off)
        {
            bool idle = animation == AnimationMode.IdleGlow;
            bool quiet = !idle && audio.IsSilent;
            if (quiet && settings.WhenSilent == SilentBehavior.Hide)
            {
                shownTarget = 0;
            }
            else
            {
                shownTarget = 1;
                musicTarget = idle || quiet ? 0 : 1;
            }
        }

        if (gate == 0)
        {
            shown = shownTarget;
            music = musicTarget;
        }
        else if (shown == 0)
        {
            music = musicTarget;
        }
        if (shown == 0) gate = gateTarget;
        gate = Approach(gate, gateTarget, step / GateFadeSeconds);
        if (gateTarget == 1)
        {
            shown = Approach(shown, shownTarget, step / (shownTarget < shown ? QuietFadeSeconds : WakeFadeSeconds));
            music = Approach(music, musicTarget, step / (musicTarget < music ? QuietFadeSeconds : WakeFadeSeconds));
        }
        bool hidden = (gate == 0 && gateTarget == 0) || (shown == 0 && shownTarget == 0);

        float beat = Unit(audio.Beat);
        float rise = 0;
        if (armed && beat > lastBeat + OnsetRise)
        {
            armed = false;
            rise = beat;
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
        if (hidden)
        {
            kick = 0;
        }
        else
        {
            kick += BeatKickCycles / KickSeconds * weight * rise;
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
        if (outputPhase >= 1) outputPhase = 0;

        IsStatic = hidden && wasHidden;
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
