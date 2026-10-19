using Rimlight.Core;

namespace Rimlight.App.Overlay;

/// <summary>
/// Demo mode's music (doc 04 §5): a synthetic 120 BPM track as the analyzer would hear it, and four palettes that take
/// turns every 6 s. It is what the README GIF shows (doc 08 §1), so it aims to look like real music: a kick on every
/// beat with an accent on each bar's downbeat, an off-beat bass note and open hat, and a level that swells gently over
/// 12 s, never a flat square wave. Everything is a function of the demo's own time, so a run is deterministic (no
/// clock, no randomness) and nothing is allocated per frame. Owned by the overlay thread.
/// </summary>
internal sealed class DemoBeat
{
    /// <summary>The tempo: 120 BPM, two beats a second.</summary>
    public const double BeatSeconds = 0.5;

    /// <summary>How long each palette shows: 12 beats, so every change lands on a bar's downbeat.</summary>
    public const double PaletteSeconds = 6;

    /// <summary>
    /// Palette changes crossfade like an album change (doc 05 §3): 800 ms, <c>MusicGlowSource.AlbumFade</c>. Written
    /// out rather than read from it so the Mac app can compile this file without the Windows frame source.
    /// </summary>
    public static readonly TimeSpan PaletteFade = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// The four looks, in turn: the brand violet into cyan (doc 08), then sunset orange into pink, aurora teal into
    /// violet and ember red into amber (the presets of the same names, doc 06 §1). Warm and cool alternate, so every
    /// change is easy to see in a short clip.
    /// </summary>
    public static readonly Palette[] Palettes =
    [
        Look("#7C5CFF", "#22D3EE"),
        Look("#FF8A2A", "#FF4F9A"),
        Look("#14E3C5", "#9D4DFF"),
        Look("#FF3A1F", "#FFB01F"),
    ];

    private const int BeatsPerBar = 4;
    private const double BeatDecaySeconds = 0.12;  // the analyzer's Beat decay (doc 03 §2)
    private const double KickAttackSeconds = 0.015; // the analyzer's Bass envelope (doc 03 §2)
    private const double KickReleaseSeconds = 0.18;
    private const double HatAttackSeconds = 0.01;
    private const double HatReleaseSeconds = 0.15;
    private const double SwellSeconds = 12;         // two palettes: one rise and fall of the pads

    // Kick strength by position in the bar: the downbeat hits hardest, the third beat a little less.
    private static readonly double[] Accents = [1.0, 0.72, 0.86, 0.72];

    private double time;

    /// <summary>Seconds since the demo started.</summary>
    public double Time => time;

    /// <summary>The palette for the current time: an index into <see cref="Palettes"/>.</summary>
    public int PaletteIndex => (int)(time / PaletteSeconds % Palettes.Length);

    /// <summary>Starts the track again from its first downbeat.</summary>
    public void Restart() => time = 0;

    /// <summary>Moves the track on by one frame and returns what the analyzer would report for it.</summary>
    /// <param name="dtSeconds">The frame's time step; a negative or non-finite step counts as 0.</param>
    public AudioFeatures Advance(float dtSeconds)
    {
        if (float.IsFinite(dtSeconds) && dtSeconds > 0) time += dtSeconds;
        return FeaturesAt(time);
    }

    /// <summary>The analyzer's features at <paramref name="t"/> seconds into the track.</summary>
    /// <param name="t">Seconds since the first downbeat; negative counts as 0.</param>
    /// <returns>Level, Bass and Beat in 0..1, never silent.</returns>
    public static AudioFeatures FeaturesAt(double t)
    {
        t = Math.Max(t, 0);
        long beat = (long)Math.Floor(t / BeatSeconds);
        double sinceBeat = t - beat * BeatSeconds;
        double accent = Accents[(int)(beat % BeatsPerBar)];

        // Beat jumps on each kick and decays like the analyzer's pulse, weaker off the downbeat.
        double pulse = accent * Math.Exp(-sinceBeat / BeatDecaySeconds);

        // Bass: the kick, plus an off-beat bass note half as strong, over a low sustained bass line.
        double kick = accent * KickShape(sinceBeat);
        double offBeat = 0.45 * KickShape(sinceBeat - BeatSeconds / 2);
        double bass = Clamp01(0.22 + 0.7 * kick + 0.3 * offBeat);

        // Mid: pads that swell over 12 s. High: an open hat on every off-beat, as the analyzer's 250 ms Level release
        // smooths it (sharper hats would make the glow flicker at 8 Hz).
        double swell = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / SwellSeconds);
        double mid = 0.45 + 0.25 * swell;
        double high = 0.3 + 0.35 * HatShape(sinceBeat - BeatSeconds / 2);

        // Doc 03 §2's Level mix.
        double level = Clamp01(0.55 * bass + 0.35 * mid + 0.10 * high);
        return new AudioFeatures((float)level, (float)bass, (float)pulse, IsSilent: false);
    }

    // A kick's envelope: a 15 ms rise, then a 180 ms release, as the analyzer's Bass envelope shapes one.
    private static double KickShape(double since)
    {
        if (since < 0) return 0;
        if (since < KickAttackSeconds)
        {
            double x = since / KickAttackSeconds;
            return x * x * (3 - 2 * x);
        }
        return Math.Exp(-(since - KickAttackSeconds) / KickReleaseSeconds);
    }

    // An open hat's energy after the analyzer's envelopes: a 10 ms rise and a 150 ms fall.
    private static double HatShape(double since)
    {
        if (since < 0) return 0;
        if (since < HatAttackSeconds) return since / HatAttackSeconds;
        return Math.Exp(-(since - HatAttackSeconds) / HatReleaseSeconds);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static Palette Look(string primary, string secondary)
    {
        SrgbHex.TryParse(primary, out Rgb a);
        SrgbHex.TryParse(secondary, out Rgb b);
        return new Palette(a, b, null);
    }
}
