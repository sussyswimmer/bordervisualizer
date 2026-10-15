using Rimlight.Core;

namespace Rimlight.App.Overlay;

/// <summary>
/// Demo mode's music (doc 04 §5): a synthetic 120 BPM groove as <see cref="AudioFeatures"/> and four palettes, 6 s
/// each, for recording the README GIF and checking the visuals without music. Everything, the gradient's turn included
/// (<see cref="PhaseAt"/>), is a function of the demo's own clock, so it repeats exactly every <see cref="LoopSeconds"/>
/// (48 beats) and a 24 s recording loops seamlessly. No shorter one does: every 6 s brings a new palette. Render thread
/// only; allocates nothing.
/// </summary>
internal sealed class DemoShow
{
    public const float BeatsPerMinute = 120;
    public const float PaletteSeconds = 6;
    public const float LoopSeconds = PaletteSeconds * 4;

    private const double BeatSeconds = 60.0 / BeatsPerMinute;
    // The gradient turns like the real engine's (doc 07 Phase 3): a 0.01-cycle kick on every beat, landing with
    // τ = 120 ms, plus a steady drift. The engine's 0.015 cycles/s would make 0.84 of a turn per loop; this drift tops
    // the 48 kicks (0.48) up to exactly one turn.
    private const double KickCycles = 0.01;
    private const double KickSeconds = 0.12;
    private const double DriftCyclesPerSecond = (1 - KickCycles * LoopSeconds / BeatSeconds) / LoopSeconds;
    private static readonly double KickTail = Math.Exp(-BeatSeconds / KickSeconds); // what's left of a kick at the next beat
    private const double KickAttackSeconds = 0.010;
    private const double KickReleaseSeconds = 0.18; // doc 03's Bass release
    private const double BeatDecaySeconds = 0.12;   // doc 03's Beat decay
    private const double HatSeconds = 0.06;
    private const double MaxStepSeconds = 0.25;     // a stall doesn't skip ahead

    private double time;

    /// <summary>
    /// The demo's palettes in order: the brand violet and cyan, then sunset, aurora and berry, all bright and saturated
    /// like the glow-ified album colors (doc 05 §2), and each far from the one before so every crossfade shows.
    /// </summary>
    public static IReadOnlyList<Palette> Palettes { get; } =
    [
        Palette.Default,
        FromHex("#FF4E8A", "#FFB547"),
        FromHex("#29F1A3", "#3B82F6"),
        FromHex("#FF3B3B", "#A855F7"),
    ];

    /// <summary>Seconds into the current loop.</summary>
    public double Time => time;

    /// <summary>The palette for the current 6 s segment.</summary>
    public Palette Palette => Palettes[(int)(time / PaletteSeconds) % Palettes.Count];

    /// <summary>Starts over: the first beat and the first palette.</summary>
    public void Restart() => time = 0;

    /// <summary>Moves the demo's clock on and returns the features at the new time.</summary>
    /// <param name="dtSeconds">Frame time; non-finite or negative counts as 0, and a stall as at most 0.25 s.</param>
    public AudioFeatures Advance(float dtSeconds)
    {
        double dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? Math.Min(dtSeconds, MaxStepSeconds) : 0;
        time = (time + dt) % LoopSeconds;
        return FeaturesAt(time);
    }

    /// <summary>
    /// The groove at <paramref name="seconds"/>: a kick on every beat (the first of each bar stronger), an off-beat hat
    /// in Level, and a swell over each palette's 6 s. Beat jumps on each kick and decays with τ = 120 ms like the
    /// analyzer's; Bass is the kick through a 10 ms attack and the analyzer's 180 ms release. Never silent.
    /// </summary>
    public static AudioFeatures FeaturesAt(double seconds)
    {
        double t = double.IsFinite(seconds) && seconds > 0 ? seconds % LoopSeconds : 0;
        long beatIndex = (long)(t / BeatSeconds);
        double sinceBeat = t - beatIndex * BeatSeconds;
        double accent = beatIndex % 4 == 0 ? 1 : 0.8;
        double kick = sinceBeat < KickAttackSeconds
            ? sinceBeat / KickAttackSeconds
            : Math.Exp(-(sinceBeat - KickAttackSeconds) / KickReleaseSeconds);
        double beat = accent * Math.Exp(-sinceBeat / BeatDecaySeconds);
        double sinceHat = sinceBeat - BeatSeconds / 2;
        double hat = sinceHat >= 0 ? Math.Exp(-sinceHat / HatSeconds) : 0;
        double swell = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (t % PaletteSeconds) / PaletteSeconds);
        double bass = 0.15 + 0.8 * accent * kick;
        double level = 0.4 + 0.35 * accent * kick + 0.08 * hat + 0.12 * swell;
        return new AudioFeatures(Unit(level), Unit(bass), Unit(beat), IsSilent: false);
    }

    /// <summary>
    /// The gradient's turn at <paramref name="seconds"/>, in cycles (0 to 1): the drift plus every kick so far. Exactly
    /// one turn per loop, and continuous, at each beat and across the loop's end.
    /// </summary>
    public static double PhaseAt(double seconds)
    {
        double t = double.IsFinite(seconds) && seconds > 0 ? seconds % LoopSeconds : 0;
        long beatIndex = (long)(t / BeatSeconds);
        double sinceBeat = t - beatIndex * BeatSeconds;
        // Each kick lands as 1 − e^(−t/τ) of its 0.01. What's still to come of this beat's kick and of those before it
        // is a geometric series (as if the beat had always played), so the sum doesn't step at a beat or a wrap.
        double kicks = KickCycles * (beatIndex + 1 - Math.Exp(-sinceBeat / KickSeconds) / (1 - KickTail));
        double phase = DriftCyclesPerSecond * t + kicks;
        return phase - Math.Floor(phase);
    }

    private static float Unit(double value) => (float)Math.Clamp(value, 0, 1);

    private static Palette FromHex(string primary, string secondary)
    {
        SrgbHex.TryParse(primary, out Rgb a);
        SrgbHex.TryParse(secondary, out Rgb b);
        return new Palette(a, b, null);
    }
}
