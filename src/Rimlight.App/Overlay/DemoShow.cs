using Rimlight.Core;

namespace Rimlight.App.Overlay;

/// <summary>
/// Demo mode's music (doc 04 §5): a synthetic 120 BPM groove as <see cref="AudioFeatures"/> and four palettes, 6 s
/// each, for recording the README GIF and checking the visuals without music. Everything is a function of the demo's
/// own clock, so it repeats exactly every <see cref="LoopSeconds"/> (48 beats). Render thread only; allocates nothing.
/// </summary>
internal sealed class DemoShow
{
    public const float BeatsPerMinute = 120;
    public const float PaletteSeconds = 6;
    public const float LoopSeconds = PaletteSeconds * 4;
    // The fake light engine never turns the gradient (Phase stays 0); the demo then turns it once per loop, so a 24 s
    // recording loops seamlessly. The real engine (C6) drifts and kicks by itself, and the demo leaves it alone.
    public const float DriftCyclesPerSecond = 1 / LoopSeconds;

    private const double BeatSeconds = 60.0 / BeatsPerMinute;
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

    private static float Unit(double value) => (float)Math.Clamp(value, 0, 1);

    private static Palette FromHex(string primary, string secondary)
    {
        SrgbHex.TryParse(primary, out Rgb a);
        SrgbHex.TryParse(secondary, out Rgb b);
        return new Palette(a, b, null);
    }
}
