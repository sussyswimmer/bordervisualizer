namespace Rimlight.AudioTools;

/// <summary>Parameters of a procedural test track. Levels are peak amplitudes (linear, 1 = full scale).</summary>
public sealed record SyntheticTrackOptions
{
    /// <summary>Sample rate in Hz.</summary>
    public int SampleRate { get; init; } = 48000;
    /// <summary>Length of the music in seconds, excluding <see cref="IntroSeconds"/> and <see cref="OutroSeconds"/>.</summary>
    public double Seconds { get; init; } = 30;
    /// <summary>Tempo; one kick per beat, the first at the start of the music.</summary>
    public double Bpm { get; init; } = 120;
    /// <summary>Kick drum level: a decaying 150 → 50 Hz sweep on every beat.</summary>
    public double KickLevel { get; init; } = 0.7;
    /// <summary>Steady white noise level.</summary>
    public double NoiseLevel { get; init; } = 0.02;
    /// <summary>Vocal-ish tone level: a 220–440 Hz melody with harmonics, vibrato and syllable swell, notes starting off the beat.</summary>
    public double VocalLevel { get; init; } = 0.15;
    /// <summary>Bass line level: sustained 49–65 Hz notes changing on each beat. Off by default.</summary>
    public double BassLevel { get; init; }
    /// <summary>Hi-hat level: short high-passed noise bursts on the off-beats. Off by default.</summary>
    public double HatLevel { get; init; }
    /// <summary>Overall linear gain, e.g. 0.01 for a −40 dB copy of the same track.</summary>
    public double Gain { get; init; } = 1;
    /// <summary>Digital silence before the music, in seconds.</summary>
    public double IntroSeconds { get; init; }
    /// <summary>Digital silence after the music, in seconds.</summary>
    public double OutroSeconds { get; init; }
    /// <summary>Seed for the noise, hats and melody; equal options give identical samples.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>The default mix plus the bass line and hi-hats: closer to real music, with bass-band energy between kicks.</summary>
    /// <returns>A copy with <see cref="BassLevel"/> 0.2 and <see cref="HatLevel"/> 0.06.</returns>
    public SyntheticTrackOptions WithFullMix() => this with { BassLevel = 0.2, HatLevel = 0.06 };

    /// <summary>Throws when a parameter is out of range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public void Validate()
    {
        if (SampleRate is < 1000 or > 768000) throw new ArgumentOutOfRangeException(nameof(SampleRate), "Expected 1000 to 768000 Hz.");
        if (!(Seconds > 0 && Seconds <= MaxSeconds)) throw new ArgumentOutOfRangeException(nameof(Seconds), $"Expected more than 0 and at most {MaxSeconds} s.");
        if (!(Bpm >= 30 && Bpm <= 400)) throw new ArgumentOutOfRangeException(nameof(Bpm), "Expected 30 to 400 BPM.");
        if (!(IntroSeconds >= 0 && IntroSeconds <= MaxSeconds)) throw new ArgumentOutOfRangeException(nameof(IntroSeconds));
        if (!(OutroSeconds >= 0 && OutroSeconds <= MaxSeconds)) throw new ArgumentOutOfRangeException(nameof(OutroSeconds));
        Level(KickLevel, nameof(KickLevel));
        Level(NoiseLevel, nameof(NoiseLevel));
        Level(VocalLevel, nameof(VocalLevel));
        Level(BassLevel, nameof(BassLevel));
        Level(HatLevel, nameof(HatLevel));
        Level(Gain, nameof(Gain));
    }

    private const double MaxSeconds = 48 * 3600;

    private static void Level(double value, string name)
    {
        if (!(value >= 0 && value <= 16)) throw new ArgumentOutOfRangeException(name, "Expected a linear level from 0 to 16.");
    }
}

/// <summary>
/// A deterministic procedural music-like track for offline beat tuning, tests and soak runs (no recorded music).
/// Every sample is a pure function of its index and the options, so the track can be streamed in pieces of any size
/// (hours of audio without holding it in memory), and <see cref="Read"/> never allocates.
/// </summary>
public sealed class SyntheticTrack
{
    private const double KickLength = 0.6;   // seconds of kick tail that are synthesized
    private const double FadeOut = 0.02;     // the music ends with a short fade instead of a click
    private const double HatLength = 0.06;
    private static readonly double[] Scale = [220.00, 246.94, 261.63, 293.66, 329.63, 392.00, 440.00]; // A minor, A3–A4
    private static readonly double[] BassNotes = [55.00, 55.00, 65.41, 49.00];

    private readonly double interval, musicEnd, rate;
    private readonly ulong noiseSeed, hatSeed, melodySeed;

    /// <summary>Creates a track.</summary>
    /// <param name="options">The parameters; validated here.</param>
    public SyntheticTrack(SyntheticTrackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options;
        rate = options.SampleRate;
        interval = 60 / options.Bpm;
        musicEnd = options.IntroSeconds + options.Seconds;
        Length = (long)Math.Round((options.IntroSeconds + options.Seconds + options.OutroSeconds) * rate);
        noiseSeed = DeterministicRandom.Hash(options.Seed, 0x6E6F697365);  // separate streams, so changing one
        hatSeed = DeterministicRandom.Hash(options.Seed, 0x686174);        // level never changes the others
        melodySeed = DeterministicRandom.Hash(options.Seed, 0x6D656C6F6479);
    }

    /// <summary>The parameters.</summary>
    public SyntheticTrackOptions Options { get; }

    /// <summary>Total length in samples, including intro and outro.</summary>
    public long Length { get; }

    /// <summary>The next sample <see cref="Read"/> returns; may be moved anywhere in [0, <see cref="Length"/>].</summary>
    public long Position { get; set; }

    /// <summary>Renders a whole track.</summary>
    /// <param name="options">The parameters.</param>
    /// <returns>The mono samples.</returns>
    public static float[] Render(SyntheticTrackOptions options)
    {
        var track = new SyntheticTrack(options);
        if (track.Length > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(options), "Too long to render in one array; stream it with Read.");
        float[] samples = new float[track.Length];
        track.Read(samples);
        return samples;
    }

    /// <summary>Fills a buffer with the next samples, without allocating.</summary>
    /// <param name="destination">The buffer to fill.</param>
    /// <returns>The number of samples written; less than the buffer length only at the end of the track.</returns>
    public int Read(Span<float> destination)
    {
        int count = (int)Math.Clamp(Length - Position, 0, destination.Length);
        for (int i = 0; i < count; i++) destination[i] = SampleAt(Position + i);
        Position += count;
        return count;
    }

    /// <summary>The times of all kicks, in seconds from the start of the file: the ground truth for beat detection.</summary>
    /// <returns>Kick onset times in ascending order.</returns>
    public double[] KickTimes()
    {
        if (Options.KickLevel <= 0 || Options.Gain <= 0) return [];
        int count = (int)Math.Ceiling(Options.Seconds / interval - 1e-9);
        double[] times = new double[count];
        for (int k = 0; k < count; k++) times[k] = Options.IntroSeconds + k * interval;
        return times;
    }

    /// <summary>Computes one sample.</summary>
    /// <param name="index">The sample index, from 0.</param>
    /// <returns>The sample value.</returns>
    public float SampleAt(long index)
    {
        double t = index / rate, m = t - Options.IntroSeconds;
        if (m < 0 || t >= musicEnd) return 0;
        long beat = (long)Math.Floor(m / interval);
        double s = m - beat * interval;
        double x = 0;
        if (Options.KickLevel > 0)
        {
            double kick = Kick(s);
            if (beat > 0 && s + interval < KickLength) kick += Kick(s + interval);
            x += Options.KickLevel * kick;
        }
        if (Options.NoiseLevel > 0) x += Options.NoiseLevel * DeterministicRandom.ToSigned(DeterministicRandom.Hash(noiseSeed, (ulong)index));
        if (Options.VocalLevel > 0) x += Options.VocalLevel * Vocal(m);
        if (Options.BassLevel > 0) x += Options.BassLevel * Bass(beat, s);
        if (Options.HatLevel > 0) x += Options.HatLevel * Hat(index, s - interval / 2);
        double fade = Math.Min(1, (musicEnd - t) / FadeOut);
        return (float)(Options.Gain * fade * x);
    }

    // A kick: fast attack, ~70 ms decay, pitch sweeping from 150 Hz down to 50 Hz (phase integrated, so no clicks).
    private static double Kick(double s)
    {
        if (s >= KickLength) return 0;
        double envelope = (1 - Math.Exp(-s / 0.0015)) * Math.Exp(-s / 0.07);
        double phase = 2 * Math.PI * (50 * s + 100 * 0.03 * (1 - Math.Exp(-s / 0.03)));
        return envelope * Math.Sin(phase);
    }

    // Vocal-ish tones: notes of 1.5 beats starting half a beat after every second beat (syncopated, as vocals are),
    // a 0.5-beat breath between notes, 4 harmonics, 5.5 Hz vibrato of ±1.2 % and a 4 Hz syllable swell.
    // Fundamentals are 220–440 Hz, so nothing lands in the 30–150 Hz bass band that drives beats.
    private double Vocal(double m)
    {
        long note = (long)Math.Floor((m / interval - 0.5) / 2);
        if (note < 0) return 0;
        double s = m - (2 * note + 0.5) * interval, length = 1.5 * interval;
        if (s < 0 || s >= length) return 0;
        double f0 = Scale[DeterministicRandom.Hash(melodySeed, (ulong)note) % (ulong)Scale.Length];
        const double depth = 0.012, vibrato = 5.5;
        double phase = 2 * Math.PI * f0 * (s + depth * (1 - Math.Cos(2 * Math.PI * vibrato * s)) / (2 * Math.PI * vibrato));
        double s1 = Math.Sin(phase), c1 = Math.Cos(phase);
        double s2 = 2 * s1 * c1, c2 = 1 - 2 * s1 * s1, s3 = s1 * (3 - 4 * s1 * s1), s4 = 2 * s2 * c2;
        double envelope = Math.Min(1, s / 0.03) * Math.Min(1, (length - s) / 0.06) * (0.75 + 0.25 * Math.Sin(2 * Math.PI * 4 * s));
        return envelope * (s1 + 0.5 * s2 + 0.3 * s3 + 0.15 * s4) / 1.95;
    }

    // A bass line: one note per beat, 20 ms attack, 10 ms release into the next beat, slow decay to 60 %.
    private double Bass(long beat, double s)
    {
        double f = BassNotes[beat % BassNotes.Length];
        double phase = 2 * Math.PI * f * s;
        double envelope = Math.Min(1, s / 0.02) * Math.Min(1, (interval - s) / 0.01) * (0.6 + 0.4 * Math.Exp(-s / 0.25));
        return envelope * (Math.Sin(phase) + 0.3 * Math.Sin(2 * phase)) / 1.3;
    }

    // Hi-hats: first-difference (high-passed) noise with a 12 ms decay, on the off-beat.
    private double Hat(long index, double s)
    {
        if (s < 0 || s >= HatLength) return 0;
        double white = DeterministicRandom.ToSigned(DeterministicRandom.Hash(hatSeed, (ulong)index));
        double previous = DeterministicRandom.ToSigned(DeterministicRandom.Hash(hatSeed, (ulong)Math.Max(0, index - 1)));
        return 0.5 * (white - previous) * Math.Exp(-s / 0.012);
    }
}
