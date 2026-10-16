namespace Rimlight.AudioTools;

/// <summary>The kinds of procedural song <see cref="StyleTracks"/> renders.</summary>
public enum MusicStyle
{
    /// <summary>
    /// A calm folk ballad at 72 BPM with no drums: fingerpicked acoustic guitar with alternating low-string bass,
    /// a soft baritone voice (fundamental 98–147 Hz in the verses, inside the 30–150 Hz beat band), breaths, plosives
    /// and sibilants. Whispered verses at about −24 dBFS RMS, strummed choruses at about −8 dBFS.
    /// </summary>
    CalmBallad,

    /// <summary>
    /// Rap at 90 BPM: an 808 on every beat (and on the "and" of 4 every other bar) with long saturated sub tails, a
    /// snare and clap on 2 and 4, 16th-note hi-hats, and a rhythmic male vocal on 16ths whose fundamental lies in the
    /// beat band. About −10 dBFS RMS throughout.
    /// </summary>
    Rap,

    /// <summary>
    /// Four-on-the-floor EDM at 128 BPM: a punchy kick on every beat, offbeat bass, claps, hats and a pumping pad in two
    /// drops (about −8 dBFS RMS), with a 12 s breakdown of pad and lead only between them (about −14 dBFS RMS).
    /// </summary>
    Edm,

    /// <summary>
    /// The ballad's instruments at 76 BPM with acoustic drums (kick, snare, hats) entering only at the chorus: a
    /// drumless verse at about −20 dBFS RMS, a drummed chorus at about −10 dBFS, and a drumless verse again.
    /// </summary>
    BalladWithDrums,
}

/// <summary>Which instruments of a <see cref="StyleTrack"/> to render, e.g. the voice alone.</summary>
[Flags]
public enum StyleParts
{
    /// <summary>Nothing.</summary>
    None = 0,
    /// <summary>Acoustic guitar: fingerpicking and strumming.</summary>
    Guitar = 1,
    /// <summary>Singing or rapping, with breaths, plosives and sibilants.</summary>
    Voice = 2,
    /// <summary>Kicks, 808s, snares, claps and hi-hats.</summary>
    Drums = 4,
    /// <summary>Synthesizers: bass, pads and lead.</summary>
    Synth = 8,
    /// <summary>Everything.</summary>
    All = Guitar | Voice | Drums | Synth,
}

/// <summary>A labelled stretch of a <see cref="StyleTrack"/>.</summary>
/// <param name="Name">The section's name, e.g. <c>verse1</c> or <c>chorus1</c>.</param>
/// <param name="Start">Start time in seconds.</param>
/// <param name="End">End time in seconds.</param>
/// <param name="Drums">Whether drums play in this section.</param>
/// <param name="TargetRmsDb">The RMS level the section was mixed to, in dBFS.</param>
public readonly record struct TrackSection(string Name, double Start, double End, bool Drums, double TargetRmsDb);

/// <summary>A rendered style track with its ground truth.</summary>
/// <param name="Style">What it imitates.</param>
/// <param name="Samples">Mono samples.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="Bpm">Tempo.</param>
/// <param name="Sections">The sections, in order and without gaps.</param>
/// <param name="KickTimes">Every kick drum onset, in seconds (empty without drums).</param>
/// <param name="Parts">The instruments rendered.</param>
public sealed record StyleTrack(MusicStyle Style, float[] Samples, int SampleRate, double Bpm, IReadOnlyList<TrackSection> Sections, IReadOnlyList<double> KickTimes, StyleParts Parts)
{
    /// <summary>The track's length in seconds.</summary>
    public double Seconds => Samples.Length / (double)SampleRate;

    /// <summary>Finds a section by name.</summary>
    /// <param name="name">The section name.</param>
    /// <returns>The section.</returns>
    /// <exception cref="ArgumentException">There is no such section.</exception>
    public TrackSection Section(string name)
    {
        foreach (TrackSection section in Sections)
            if (section.Name == name) return section;
        throw new ArgumentException($"{Style} has no section \"{name}\".", nameof(name));
    }
}

/// <summary>
/// Procedural songs in four styles for tuning and testing how the light follows the character and the dynamics of
/// music (no recorded music). They are built from simple physical-ish models: additive plucked strings whose
/// partials decay faster the higher they are, a formant-shaped voice with vibrato, breath and consonant noise, and
/// synthesized drums. Each section is mixed to a target RMS, so the dynamics between sections are known exactly.
/// Rendering is deterministic for a seed and takes about a second for the longest track. Each instrument draws from its
/// own random stream, so rendering only some <see cref="StyleParts"/> gives exactly those instruments of the full mix
/// (each section then mixed to its target RMS on its own).
/// </summary>
public static class StyleTracks
{
    /// <summary>Renders a style track.</summary>
    /// <param name="style">The style.</param>
    /// <param name="sampleRate">Sample rate in Hz, 8000 to 192000.</param>
    /// <param name="seed">Seed for the humanization, noise and melody.</param>
    /// <param name="parts">The instruments to render; at least one.</param>
    /// <returns>The track and its ground truth.</returns>
    public static StyleTrack Render(MusicStyle style, int sampleRate = 48000, ulong seed = 1, StyleParts parts = StyleParts.All)
    {
        if (sampleRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate), "Expected 8000 to 192000 Hz.");
        if ((parts & StyleParts.All) == StyleParts.None) throw new ArgumentOutOfRangeException(nameof(parts), "Render at least one part.");
        var synth = new Synth(sampleRate, seed, parts & StyleParts.All);
        return style switch
        {
            MusicStyle.CalmBallad => CalmBallad(synth),
            MusicStyle.Rap => Rap(synth),
            MusicStyle.Edm => Edm(synth),
            MusicStyle.BalladWithDrums => BalladWithDrums(synth),
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
    }

    // ---- the songs ----

    // G – Em – C – D, one chord per bar. Fingerpicking: the thumb plays the root on beat 1 and the fifth on beat 3
    // (low E and A strings included); the fingers play chord tones on the other eighths.
    private static readonly double[][] PickBass = [[98.00, 146.83], [82.41, 123.47], [130.81, 98.00], [146.83, 110.00]];
    private static readonly double[][] PickTreble = [[196.00, 246.94, 293.66], [196.00, 246.94, 329.63], [196.00, 261.63, 329.63], [220.00, 293.66, 369.99]];
    private static readonly double[][] StrumChords =
    [
        [98.00, 123.47, 146.83, 196.00, 246.94, 392.00],
        [82.41, 123.47, 164.81, 196.00, 246.94, 329.63],
        [130.81, 164.81, 196.00, 261.63, 329.63],
        [146.83, 220.00, 293.66, 369.99],
    ];
    // Baritone melodies (Hz): verses sit low (G2–D3, inside the beat band), choruses higher (D3–A3).
    private static readonly double[] VerseNotes = [98.00, 110.00, 123.47, 130.81, 146.83];
    private static readonly double[] ChorusNotes = [146.83, 164.81, 196.00, 220.00, 196.00, 164.81];

    private static StyleTrack CalmBallad(Synth synth)
    {
        const double bpm = 72;
        TrackSection[] sections =
        [
            new("intro", 0, 8, false, -22),
            new("verse1", 8, 30, false, -24),
            new("chorus1", 30, 50, false, -8),
            new("verse2", 50, 72, false, -24),
            new("chorus2", 72, 92, false, -8),
            new("outro", 92, 100, false, -22),
        ];
        synth.Allocate(sections[^1].End);
        double beat = 60 / bpm;
        foreach (TrackSection section in sections)
        {
            bool chorus = section.Name.StartsWith("chorus", StringComparison.Ordinal);
            if (chorus) Strumming(synth, section.Start, section.End, beat);
            else Fingerpicking(synth, section.Start, section.End, beat);
            if (section.Name.StartsWith("verse", StringComparison.Ordinal)) Singing(synth, section.Start, section.End, beat, whisper: true);
            if (chorus) Singing(synth, section.Start, section.End, beat, whisper: false);
        }
        return synth.Finish(MusicStyle.CalmBallad, bpm, sections);
    }

    private static StyleTrack BalladWithDrums(Synth synth)
    {
        const double bpm = 76;
        TrackSection[] sections =
        [
            new("verse", 0, 24, false, -20),
            new("chorus", 24, 48, true, -10),
            new("verse2", 48, 60, false, -20),
        ];
        synth.Allocate(sections[^1].End);
        double beat = 60 / bpm;
        foreach (TrackSection section in sections)
        {
            if (section.Drums)
            {
                Strumming(synth, section.Start, section.End, beat);
                Singing(synth, section.Start, section.End, beat, whisper: false);
                AcousticDrums(synth, section.Start, section.End, beat);
            }
            else
            {
                Fingerpicking(synth, section.Start, section.End, beat);
                Singing(synth, section.Start, section.End, beat, whisper: true);
            }
        }
        return synth.Finish(MusicStyle.BalladWithDrums, bpm, sections);
    }

    private static StyleTrack Rap(Synth synth)
    {
        const double bpm = 90;
        TrackSection[] sections = [new("verse", 0, 40, true, -10)];
        synth.Allocate(sections[^1].End);
        synth.Use(StyleParts.Drums);
        double beat = 60 / bpm;
        double[] bassline = [43.65, 43.65, 51.91, 38.89];   // F1 F1 G#1 D#1, one note per bar
        int beats = (int)Math.Round(40 / beat);
        // 808s: every beat, plus the "and" of 4 in every second bar. A new 808 cuts the previous one (mono voice).
        var hits = new List<double>();
        for (int b = 0; b < beats; b++)
        {
            hits.Add(b * beat);
            if (b % 8 == 7) hits.Add((b + 0.5) * beat);
        }
        for (int i = 0; i < hits.Count; i++)
        {
            double t = hits[i];
            double cut = i + 1 < hits.Count ? hits[i + 1] : 40;
            double f = bassline[(int)(t / (4 * beat)) % bassline.Length];
            synth.Kick808(t, f, 0.9, 0.45, cut);
        }
        for (int b = 0; b < beats; b++)
        {
            double t = b * beat;
            if (b % 2 == 1)
            {
                synth.Snare(t, 0.45);
                synth.Clap(t + 0.004, 0.25);
            }
            for (int s = 0; s < 4; s++)
            {
                double accent = s == 0 ? 1 : s == 2 ? 0.75 : 0.5;
                synth.Hat(t + s * beat / 4 + synth.Jitter(0.003), 0.10 * accent * synth.Vary(0.15), 0.022);
            }
            if (b % 8 == 6) for (int r = 0; r < 4; r++) synth.Hat(t + beat / 2 + r * beat / 8, 0.06, 0.015); // a roll
        }
        // A rapped vocal on 16ths: phrases of 2 bars with a short gap, syllables of one or two 16ths, speech-like pitch.
        synth.Use(StyleParts.Voice);
        double sixteenth = beat / 4;
        for (double bar = 0; bar * 4 * beat < 40 - 0.5; bar += 2)
        {
            double phraseStart = bar * 4 * beat + sixteenth;
            double phraseEnd = phraseStart + 7 * beat;
            double t = phraseStart;
            while (t < phraseEnd && t < 39.5)
            {
                double length = synth.Chance(0.3) ? 2 * sixteenth : sixteenth;
                if (synth.Chance(0.12)) { t += sixteenth; continue; }            // a rest
                double f0 = 118 + 22 * synth.Uniform() + (synth.Chance(0.2) ? 15 : 0);
                synth.Syllable(t, length * 0.9, f0, f0 * (0.92 + 0.06 * synth.Uniform()), synth.PickVowel(), 0.32, 0.06, 0.012, 0.025, 0);
                if (synth.Chance(0.45)) synth.Plosive(t, 0.10);
                else if (synth.Chance(0.25)) synth.Sibilant(t, 0.08, 0.07);
                t += length;
            }
        }
        return synth.Finish(MusicStyle.Rap, bpm, sections);
    }

    private static StyleTrack Edm(Synth synth)
    {
        const double bpm = 128;
        TrackSection[] sections =
        [
            new("drop1", 0, 24, true, -8),
            new("breakdown", 24, 36, false, -14),
            new("drop2", 36, 52, true, -8),
        ];
        synth.Allocate(sections[^1].End);
        double beat = 60 / bpm;
        double[][] chords = [[220.00, 261.63, 329.63], [174.61, 220.00, 261.63], [196.00, 246.94, 293.66], [164.81, 196.00, 246.94]];
        double[] roots = [55.00, 43.65, 49.00, 41.20];
        foreach (TrackSection section in sections)
        {
            int first = (int)Math.Ceiling(section.Start / beat - 1e-9), last = (int)Math.Ceiling(section.End / beat - 1e-9);
            for (int b = first; b < last; b++)
            {
                double t = b * beat;
                int bar = b / 4;
                double[] chord = chords[bar % chords.Length];
                if (section.Drums)
                {
                    synth.Use(StyleParts.Drums);
                    synth.KickPunchy(t, 0.95);
                    if (b % 2 == 1) synth.Clap(t, 0.35);
                    synth.Hat(t + beat / 2, 0.14, 0.07);                          // open hat on the offbeat
                    for (int s = 0; s < 4; s++) synth.Hat(t + s * beat / 4, 0.05 * synth.Vary(0.2), 0.018);
                    synth.Use(StyleParts.Synth);
                    synth.Saw(t + beat / 2, beat / 2 * 0.85, roots[bar % roots.Length], 0.30, 0.004, 1800);
                    // The pad pumps: it ducks under each kick (sidechain) and swells back by the next one.
                    foreach (double f in chord) synth.Saw(t + 0.01, beat * 0.95, f, 0.07, 0.18, 3500, detune: true);
                }
                else
                {
                    synth.Use(StyleParts.Synth);
                    if (b % 4 == 0) foreach (double f in chord) synth.Saw(t, 4 * beat, f, 0.10, 0.6, 2500, detune: true);
                    synth.Saw(t, beat * 0.8, 2 * chord[(b + bar) % 3], 0.05, 0.01, 4000);  // a plucky lead
                }
            }
        }
        return synth.Finish(MusicStyle.Edm, bpm, sections);
    }

    // ---- parts ----

    private static void Fingerpicking(Synth synth, double start, double end, double beat)
    {
        synth.Use(StyleParts.Guitar);
        double eighth = beat / 2;
        int first = (int)Math.Ceiling(start / eighth - 1e-9), last = (int)Math.Ceiling(end / eighth - 1e-9);
        for (int e = first; e < last; e++)
        {
            int bar = e / 8, inBar = e % 8, chord = bar % PickBass.Length;
            double t = e * eighth + synth.Jitter(0.010);
            if (inBar is 0 or 4)
            {
                // The thumb: soft flesh, a strong low string.
                synth.Pluck(t, PickBass[chord][inBar == 0 ? 0 : 1], 0.55 * synth.Vary(0.1), 1.6, 3.0, 0.20, 0.25);
            }
            else
            {
                double[] treble = PickTreble[chord];
                synth.Pluck(t, treble[(inBar + bar) % treble.Length], 0.32 * synth.Vary(0.15), 1.1, 2.0, 0.17, 0.35);
            }
        }
    }

    private static void Strumming(Synth synth, double start, double end, double beat)
    {
        synth.Use(StyleParts.Guitar);
        // Down, down-up, up-down-up per bar on eighths: D . D U . U D U, with a pick.
        bool[] down = [true, false, true, false, false, false, true, false];
        bool[] play = [true, false, true, true, false, true, true, true];
        double eighth = beat / 2;
        int first = (int)Math.Ceiling(start / eighth - 1e-9), last = (int)Math.Ceiling(end / eighth - 1e-9);
        for (int e = first; e < last; e++)
        {
            int inBar = e % 8;
            if (!play[inBar]) continue;
            double[] chord = StrumChords[e / 8 % StrumChords.Length];
            double t = e * eighth + synth.Jitter(0.008);
            double ring = Math.Min(end, (e + 1) * eighth + (play[(inBar + 1) % 8] ? 0 : eighth)) - t;
            double velocity = (inBar is 0 or 4 ? 1 : down[inBar] ? 0.85 : 0.6) * synth.Vary(0.1);
            int strings = down[inBar] ? chord.Length : Math.Min(4, chord.Length);
            double sweep = down[inBar] ? 0.028 : 0.016;
            for (int s = 0; s < strings; s++)
            {
                int index = down[inBar] ? s : chord.Length - 1 - s;
                double at = t + sweep * s / Math.Max(1, strings - 1);
                synth.Pluck(at, chord[index], 0.20 * velocity, 0.9, ring + 0.03, 0.12, 0.7, pick: 0.05);
            }
        }
    }

    // A baritone singer: phrases of two bars (about 4.5 s of singing, then a breath). Whispered verses are breathy and
    // soft with slow onsets; choruses are fully voiced and higher.
    private static void Singing(Synth synth, double start, double end, double beat, bool whisper)
    {
        synth.Use(StyleParts.Voice);
        double bar = 4 * beat, phrase = 2 * bar;
        double[] notes = whisper ? VerseNotes : ChorusNotes;
        for (double p = start + beat; p + 1 < end; p += phrase)
        {
            double t = p, singEnd = Math.Min(end - 0.4, p + phrase - 1.3 * beat);
            int note = synth.NextInt(notes.Length);
            while (t < singEnd)
            {
                double length = beat / 2 * (synth.Chance(0.35) ? 2 : synth.Chance(0.2) ? 3 : 1);
                length = Math.Min(length, singEnd - t);
                if (length < 0.12) break;
                note = Math.Clamp(note + synth.NextInt(3) - 1, 0, notes.Length - 1);
                double f0 = notes[note];
                if (whisper) synth.Syllable(t, length * 0.92, f0, f0, synth.PickVowel(), 0.10, 0.07, 0.07, 0.09, 0.008);
                else synth.Syllable(t, length * 0.95, f0, f0, synth.PickVowel(), 0.32, 0.03, 0.035, 0.07, 0.012);
                if (synth.Chance(0.25)) synth.Plosive(t, whisper ? 0.02 : 0.05);
                else if (synth.Chance(0.2)) synth.Sibilant(t, whisper ? 0.03 : 0.05, 0.09);
                t += length;
            }
            synth.Breath(singEnd + 0.35, whisper ? 0.05 : 0.04);
        }
    }

    private static void AcousticDrums(Synth synth, double start, double end, double beat)
    {
        synth.Use(StyleParts.Drums);
        double eighth = beat / 2;
        int first = (int)Math.Ceiling(start / eighth - 1e-9), last = (int)Math.Ceiling(end / eighth - 1e-9);
        for (int e = first; e < last; e++)
        {
            double t = e * eighth;
            int inBar = e % 8;
            if (inBar is 0 or 4 or 5) synth.KickAcoustic(t, inBar == 5 ? 0.6 : 0.8);
            if (inBar is 2 or 6) synth.Snare(t, 0.5);
            synth.Hat(t + synth.Jitter(0.004), (inBar % 2 == 0 ? 0.07 : 0.045) * synth.Vary(0.1), 0.03);
        }
    }

    // ---- synthesis ----

    // Instruments draw from their own random streams (Use), so leaving one out changes none of the others. Calls for an
    // instrument that isn't rendered return at once.
    private sealed class Synth(int rate, ulong seed, StyleParts parts)
    {
        private const double TwoPi = 2 * Math.PI;
        private readonly DeterministicRandom[] streams =
        [
            new(DeterministicRandom.Hash(seed, 1)), new(DeterministicRandom.Hash(seed, 2)),
            new(DeterministicRandom.Hash(seed, 3)), new(DeterministicRandom.Hash(seed, 4)),
        ];
        private readonly List<double> kicks = [];
        private double[] mix = [];
        private DeterministicRandom random = null!;
        private bool on;

        public void Allocate(double seconds) => mix = new double[(int)Math.Round(seconds * rate)];

        // Selects the instrument the following calls belong to: its random stream, and whether it is rendered.
        public void Use(StyleParts part)
        {
            random = streams[System.Numerics.BitOperations.Log2((uint)part)];
            on = (parts & part) != 0;
        }

        // Male vowel formants (Peterson & Barney): a, e, i, o, u.
        private static readonly double[][] Vowels =
        [
            [730, 1090, 2440], [530, 1840, 2480], [270, 2290, 3010], [570, 840, 2410], [300, 870, 2240],
        ];

        public double Uniform() => random.NextDouble();
        public bool Chance(double p) => random.NextDouble() < p;
        public int NextInt(int max) => random.NextInt(max);
        public double Jitter(double seconds) => seconds * random.NextSigned();
        public double Vary(double fraction) => 1 + fraction * random.NextSigned();
        public double[] PickVowel() => Vowels[random.NextInt(Vowels.Length)];

        // A plucked string (Karplus–Strong): one period of noise, low-passed for a soft fingertip and combed by the pluck
        // position, circulates through a delay line whose two-point average damps the higher partials faster, so the tone
        // darkens as it rings. The fundamental decays with time constant `decay`. A guitar body radiates little below
        // ~100 Hz (a high-pass), so the low strings' fundamentals are weaker than their harmonics. After `length` the
        // string is damped over 30 ms. `pick` adds a pick click.
        public void Pluck(double time, double f0, double amplitude, double decay, double length, double position, double brightness, double pick = 0)
        {
            if (!on) return;
            int start = Index(time), count = Math.Min(mix.Length - start, (int)((length + 0.15) * rate));
            if (count <= 0) return;
            int period = Math.Max(4, (int)Math.Round(rate / f0 - 0.5));   // the average adds half a sample of delay
            double[] excitation = new double[period], line = new double[period];
            double smooth = Math.Clamp(1 - brightness, 0, 0.95), low = 0, mean = 0;
            for (int i = 0; i < period; i++)
            {
                low = (1 - smooth) * random.NextSigned() + smooth * low;
                excitation[i] = low;
            }
            int offset = Math.Max(1, (int)Math.Round(position * period));
            for (int i = 0; i < period; i++)
            {
                line[i] = excitation[i] - excitation[(i - offset + period) % period];
                mean += line[i] / period;
            }
            double energy = 0;
            for (int i = 0; i < period; i++)
            {
                line[i] -= mean;
                energy += line[i] * line[i];
            }
            double scale = amplitude / Math.Sqrt(energy / period + 1e-30);
            for (int i = 0; i < period; i++) line[i] *= scale;

            double loop = Math.Exp(-1 / (f0 * decay));
            Filter body = Filter.HighPass(rate, 100, 0.6);
            double fall = Math.Exp(-1 / (0.03 * rate)), damp = 1, before = line[period - 1];
            int releaseAt = (int)(length * rate), index = 0;
            for (int i = 0; i < count; i++)
            {
                double old = line[index];
                line[index] = loop * 0.5 * (old + before);
                before = old;
                if (++index == period) index = 0;
                if (i >= releaseAt) damp *= fall;
                mix[start + i] += damp * body.Process(old);
            }
            if (pick > 0) Noise(time, 0.004, pick * amplitude, 0.0002, 0.0012, Filter.HighPass(rate, 2500, 0.7), null);
        }

        // A sung or rapped syllable: a harmonic stack up to 4.5 kHz shaped by the vowel's formants, gliding from f0Start
        // to f0End with delayed vibrato, plus breath noise through the same formants. Raised-cosine attack and release.
        public void Syllable(double time, double length, double f0Start, double f0End, double[] formants, double voiced, double breath, double attack, double release, double vibrato)
        {
            if (!on) return;
            int start = Index(time), count = (int)(length * rate);
            if (start >= mix.Length || count <= 0) return;
            double f0Mean = 0.5 * (f0Start + f0End);
            int harmonics = (int)Math.Min(48, 4500 / f0Mean);
            Span<double> gains = stackalloc double[48];
            double norm = 0;
            for (int n = 1; n <= harmonics; n++)
            {
                double f = n * f0Mean, g = 0.15;
                for (int i = 0; i < 3; i++)
                {
                    double width = 80 + 40 * i, x = (f - formants[i]) / width;
                    g += Math.Pow(0.5, i) / (1 + x * x);
                }
                gains[n - 1] = g / Math.Pow(n, 0.9);
                norm += gains[n - 1] * gains[n - 1];
            }
            norm = Math.Sqrt(norm) + 1e-12;
            Filter f1 = Filter.BandPass(rate, formants[0], 3), f2 = Filter.BandPass(rate, formants[1], 4), f3 = Filter.BandPass(rate, formants[2], 5);
            double phase = 0;
            for (int i = 0; i < count && start + i < mix.Length; i++)
            {
                double t = i / (double)rate;
                double envelope = Ramp(t / attack) * Ramp((length - t) / release);
                double glide = f0Start + (f0End - f0Start) * t / length;
                double depth = vibrato * Math.Min(1, Math.Max(0, (t - 0.15) / 0.2));
                phase += TwoPi * glide * (1 + depth * Math.Sin(TwoPi * 5.5 * t)) / rate;
                // sin(nφ) for every harmonic by the Chebyshev recurrence.
                double s1 = Math.Sin(phase), c2 = 2 * Math.Cos(phase), previous = 0, current = s1, sum = 0;
                for (int n = 0; n < harmonics; n++)
                {
                    sum += gains[n] * current;
                    double next = c2 * current - previous;
                    previous = current;
                    current = next;
                }
                double white = random.NextSigned();
                double noise = f1.Process(white) + 0.6 * f2.Process(white) + 0.35 * f3.Process(white);
                mix[start + i] += envelope * (voiced * sum / norm + breath * noise);
            }
        }

        // A plosive ("t", "k", "p"): a 10 ms burst of broadband noise with a sharp edge.
        public void Plosive(double time, double amplitude) =>
            Noise(time, 0.012, amplitude, 0.0004, 0.004, Filter.HighPass(rate, 1200, 0.7), null);

        // A sibilant ("s"): high-band noise with a soft 25 ms onset.
        public void Sibilant(double time, double amplitude, double length) =>
            Noise(time, length, amplitude, 0.025, length, Filter.HighPass(rate, 4200, 0.7), Filter.LowPass(rate, 9500, 0.7));

        // An inhale: band-limited noise swelling over 0.15 s.
        public void Breath(double time, double amplitude) =>
            Noise(time, 0.4, amplitude, 0.15, 0.4, Filter.BandPass(rate, 1400, 0.8), null);

        // An 808: a saturated sine sweeping down to f0 with a long tail, a small click, cut by the next hit.
        public void Kick808(double time, double f0, double amplitude, double decay, double cut)
        {
            if (!on) return;
            kicks.Add(time);
            int start = Index(time), count = (int)((cut - time) * rate);
            double phase = 0;
            for (int i = 0; i < count && start + i < mix.Length; i++)
            {
                double t = i / (double)rate;
                phase += TwoPi * f0 * (1 + 1.5 * Math.Exp(-t / 0.03)) / rate;
                double envelope = (1 - Math.Exp(-t / 0.002)) * Math.Exp(-t / decay) * Ramp((cut - time - t) / 0.008);
                mix[start + i] += amplitude * Math.Tanh(2.2 * envelope * Math.Sin(phase)) / Math.Tanh(2.2);
            }
            Noise(time, 0.004, 0.12 * amplitude, 0.0002, 0.0015, Filter.HighPass(rate, 1000, 0.7), null);
        }

        // An EDM kick: a fast 200 → 48 Hz sweep, 160 ms body, a click.
        public void KickPunchy(double time, double amplitude)
        {
            if (!on) return;
            kicks.Add(time);
            int start = Index(time), count = (int)(0.45 * rate);
            double phase = 0;
            for (int i = 0; i < count && start + i < mix.Length; i++)
            {
                double t = i / (double)rate;
                phase += TwoPi * 48 * (1 + 3.5 * Math.Exp(-t / 0.012)) / rate;
                mix[start + i] += amplitude * (1 - Math.Exp(-t / 0.0003)) * Math.Exp(-t / 0.16) * Math.Sin(phase);
            }
            Noise(time, 0.003, 0.2 * amplitude, 0.0001, 0.001, Filter.HighPass(rate, 2000, 0.7), null);
        }

        // An acoustic kick: a short 60 Hz thump with a beater click and some shell noise.
        public void KickAcoustic(double time, double amplitude)
        {
            if (!on) return;
            kicks.Add(time);
            int start = Index(time), count = (int)(0.35 * rate);
            double phase = 0;
            for (int i = 0; i < count && start + i < mix.Length; i++)
            {
                double t = i / (double)rate;
                phase += TwoPi * 58 * (1 + 0.8 * Math.Exp(-t / 0.015)) / rate;
                mix[start + i] += amplitude * (1 - Math.Exp(-t / 0.0006)) * Math.Exp(-t / 0.09) * Math.Sin(phase);
            }
            Noise(time, 0.006, 0.15 * amplitude, 0.0002, 0.002, Filter.HighPass(rate, 3000, 0.7), null);
            Noise(time, 0.08, 0.1 * amplitude, 0.001, 0.03, Filter.LowPass(rate, 500, 0.7), null);
        }

        // A snare: noise above 700 Hz with a 120 ms tail, and a tuned body at 190 and 330 Hz.
        public void Snare(double time, double amplitude)
        {
            if (!on) return;
            Noise(time, 0.35, amplitude, 0.0005, 0.12, Filter.HighPass(rate, 700, 0.7), null);
            AddPartial(time, 190, 0.6 * amplitude, 0.05, 0.0005, 0.3, 0.01);
            AddPartial(time, 330, 0.35 * amplitude, 0.04, 0.0005, 0.3, 0.01);
        }

        // A clap: three quick noise bursts around 1.2 kHz, then a short tail.
        public void Clap(double time, double amplitude)
        {
            if (!on) return;
            for (int b = 0; b < 3; b++) Noise(time + 0.01 * b, 0.02, amplitude * 0.7, 0.0003, 0.005, Filter.BandPass(rate, 1200, 0.8), null);
            Noise(time + 0.03, 0.25, amplitude, 0.001, 0.07, Filter.BandPass(rate, 1200, 0.8), null);
        }

        // A hi-hat: noise above 7 kHz.
        public void Hat(double time, double amplitude, double decay) =>
            Noise(time, Math.Min(0.4, 6 * decay), amplitude, 0.0002, decay, Filter.HighPass(rate, 7000, 0.7), Filter.HighPass(rate, 7000, 0.7));

        // A saw-ish synth note (harmonics with 1/n roll-off up to `cutoff`), optionally three detuned voices.
        public void Saw(double time, double length, double f0, double amplitude, double attack, double cutoff, bool detune = false)
        {
            if (!on) return;
            int voices = detune ? 3 : 1;
            int start = Index(time), count = Math.Min((int)(length * rate), mix.Length - start);
            if (count <= 0) return;
            double[] envelope = new double[count];
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)rate;
                envelope[i] = Ramp(t / attack) * Ramp((length - t) / 0.02);
            }
            for (int v = 0; v < voices; v++)
            {
                double f = f0 * (detune ? 1 + 0.006 * (v - 1) : 1);
                double phaseOffset = random.NextDouble() * TwoPi;
                int harmonics = (int)Math.Min(40, cutoff / f);
                for (int n = 1; n <= harmonics; n++)
                {
                    double gain = amplitude / voices / n, w = TwoPi * n * f / rate;
                    double cr = Math.Cos(w), ci = Math.Sin(w), zr = Math.Cos(n * phaseOffset), zi = Math.Sin(n * phaseOffset);
                    for (int i = 0; i < count; i++)
                    {
                        mix[start + i] += gain * envelope[i] * zi;
                        (zr, zi) = (zr * cr - zi * ci, zr * ci + zi * cr);
                    }
                }
            }
        }

        // One exponentially decaying sine with an exponential attack, damped over `release` after `length`.
        // It stops once it has decayed below Floor (about −94 dBFS before the section gain).
        private void AddPartial(double time, double frequency, double amplitude, double tau, double attack, double length, double release)
        {
            const double Floor = 2e-5;
            if (frequency >= rate / 2.0 || amplitude <= Floor) return;
            int start = Index(time);
            int count = Math.Min((int)(Math.Min(tau * Math.Log(amplitude / Floor), length + 5 * release) * rate), mix.Length - start);
            double w = TwoPi * frequency / rate, r = Math.Exp(-1 / (tau * rate));
            double cr = r * Math.Cos(w), ci = r * Math.Sin(w), zr = amplitude, zi = 0;
            double rise = Math.Exp(-1 / (attack * rate)), fall = Math.Exp(-1 / (release * rate));
            double onset = 1, damp = 1;
            int releaseAt = (int)(length * rate);
            for (int i = 0; i < count; i++)
            {
                if (i >= releaseAt) damp *= fall;
                mix[start + i] += (1 - onset) * damp * zi;
                onset *= rise;
                (zr, zi) = (zr * cr - zi * ci, zr * ci + zi * cr);
            }
        }

        // Filtered white noise with an exponential attack and decay, cut after `length`.
        private void Noise(double time, double length, double amplitude, double attack, double decay, Filter first, Filter? second)
        {
            if (!on) return;
            int start = Index(time), count = (int)(length * rate);
            double rise = Math.Exp(-1 / (attack * rate)), fall = Math.Exp(-1 / (decay * rate)), onset = 1, envelope = 1;
            for (int i = 0; i < count && start + i < mix.Length; i++)
            {
                double x = first.Process(random.NextSigned());
                if (second is not null) x = second.Process(x);
                mix[start + i] += amplitude * (1 - onset) * envelope * x;
                onset *= rise;
                envelope *= fall;
            }
        }

        private int Index(double time) => (int)Math.Round(Math.Max(0, time) * rate);

        private static double Ramp(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 0.5 - 0.5 * Math.Cos(Math.PI * x);

        // Mixes each section to its target RMS, with 0.5 s gain ramps across the boundaries.
        public StyleTrack Finish(MusicStyle style, double bpm, TrackSection[] sections)
        {
            var gains = new double[sections.Length];
            for (int s = 0; s < sections.Length; s++)
            {
                int from = Index(sections[s].Start), to = Math.Min(mix.Length, Index(sections[s].End));
                double squares = 0;
                for (int i = from; i < to; i++) squares += mix[i] * mix[i];
                double rms = Math.Sqrt(squares / Math.Max(1, to - from));
                gains[s] = rms > 0 ? Math.Pow(10, sections[s].TargetRmsDb / 20) / rms : 0;
            }
            var samples = new float[mix.Length];
            const double ramp = 0.5;
            for (int i = 0; i < mix.Length; i++)
            {
                double t = i / (double)rate, gain = gains[0];
                for (int s = 1; s < sections.Length; s++)
                {
                    double x = (t - (sections[s].Start - ramp / 2)) / ramp;
                    if (x <= 0) break;
                    gain = gains[s - 1] + (gains[s] - gains[s - 1]) * Math.Min(1, x);
                }
                samples[i] = (float)Math.Clamp(gain * mix[i], -1, 1);
            }
            kicks.Sort();
            return new StyleTrack(style, samples, rate, bpm, sections, kicks.ToArray(), parts);
        }
    }

    // A biquad (RBJ cookbook), direct form I.
    private sealed class Filter(double b0, double b1, double b2, double a1, double a2)
    {
        private double x1, x2, y1, y2;

        public static Filter LowPass(int rate, double f, double q) => Make(rate, f, q, 0);
        public static Filter HighPass(int rate, double f, double q) => Make(rate, f, q, 1);
        public static Filter BandPass(int rate, double f, double q) => Make(rate, f, q, 2);

        public double Process(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }

        private static Filter Make(int rate, double f, double q, int kind)
        {
            f = Math.Min(f, 0.45 * rate);
            double w = 2 * Math.PI * f / rate, alpha = Math.Sin(w) / (2 * q), cos = Math.Cos(w), a0 = 1 + alpha;
            return kind switch
            {
                0 => new Filter((1 - cos) / 2 / a0, (1 - cos) / a0, (1 - cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0),
                1 => new Filter((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0),
                _ => new Filter(alpha / a0, 0, -alpha / a0, -2 * cos / a0, (1 - alpha) / a0),
            };
        }
    }
}
