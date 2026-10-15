using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.Bench;

/// <summary>What the capture delivers during a segment.</summary>
internal enum SoakSegmentKind
{
    /// <summary>Synthetic music, delivered in packets.</summary>
    Music,
    /// <summary>Digital silence: packets of zeros keep arriving (a player that holds its stream open).</summary>
    Silence,
    /// <summary>No packets at all, as WASAPI loopback behaves while nothing plays (doc 03 §1).</summary>
    NoPackets,
}

/// <summary>A stretch of the program's timeline, in samples.</summary>
/// <param name="Kind">What the capture delivers.</param>
/// <param name="Start">First sample.</param>
/// <param name="Length">Number of samples.</param>
/// <param name="Audio">The music, read from its own sample 0 at <paramref name="Start"/>; null unless Music.</param>
/// <param name="Bpm">The music's tempo; 0 unless Music.</param>
internal readonly record struct SoakSegment(SoakSegmentKind Kind, long Start, long Length, SyntheticTrack? Audio, double Bpm)
{
    public long End => Start + Length;
}

/// <summary>Something the render thread learns at a point of the timeline, on the first frame whose audio reaches it.</summary>
/// <param name="Position">The sample where it happens.</param>
/// <param name="Palette">A new track's album palette: the render thread calls SetTarget with it (800 ms, as for album
/// colors in the app). Null when this is only a session start.</param>
/// <param name="SameAlbum">The track has the previous track's colors under a new track ID (the next song of an album).</param>
/// <param name="Settings">A new listening session's settings; the session also starts on a new output device, so the
/// analyzer is Reset. Null when this is only a track start.</param>
internal readonly record struct SoakEvent(long Position, Palette? Palette, bool SameAlbum, Settings? Settings);

/// <summary>Durations and ranges of a generated program, in seconds. <see cref="Default"/> is a listening day.</summary>
internal sealed record SoakProgramShape
{
    /// <summary>Hours of listening: tracks of 2–5 minutes (a palette change each), in sessions of 40–80 minutes
    /// separated by 3–12 minute pauses with nothing playing.</summary>
    public static SoakProgramShape Default { get; } = new();

    /// <summary>The same structure squeezed into about a minute per session, so a two-minute run covers every segment
    /// kind, both silent behaviors, palette changes and device changes (the quick soak test).</summary>
    public static SoakProgramShape Compressed { get; } = new()
    {
        Name = "compressed",
        TrackSeconds = (12, 20), IntroSeconds = (1, 2), BreakdownSeconds = (2, 4),
        ShortGapSeconds = (0.3, 1.5), LongGapSeconds = (4.5, 7), LongGapEvery = 3,
        SessionSeconds = (45, 60), PauseSeconds = (5, 8),
    };

    /// <summary>The name the command line and the JSON report use: <c>day</c> or <c>compressed</c>.</summary>
    public string Name { get; init; } = "day";
    /// <summary>Length of one track, gaps excluded.</summary>
    public (double Min, double Max) TrackSeconds { get; init; } = (120, 300);
    /// <summary>A track opens without kick and bass (vocals, hats, noise) for this long.</summary>
    public (double Min, double Max) IntroSeconds { get; init; } = (3, 10);
    /// <summary>A breakdown in the middle of each track: vocals and noise only.</summary>
    public (double Min, double Max) BreakdownSeconds { get; init; } = (8, 20);
    /// <summary>Tempo range of the tracks.</summary>
    public (double Min, double Max) Bpm { get; init; } = (80, 170);
    /// <summary>Playback level range: the auto-gain has to follow quiet and loud tracks.</summary>
    public (double Min, double Max) GainDb { get; init; } = (-24, 0);
    /// <summary>Most gaps between tracks: shorter than the analyzer's 2 s silence hold.</summary>
    public (double Min, double Max) ShortGapSeconds { get; init; } = (0.3, 3);
    /// <summary>Every <see cref="LongGapEvery"/>-th gap lasts long enough to count as silence, so the light fades to
    /// Idle Glow or hides.</summary>
    public (double Min, double Max) LongGapSeconds { get; init; } = (4, 20);
    /// <summary>Gaps alternate between digital silence and no packets, so an odd value here makes long gaps alternate
    /// too.</summary>
    public int LongGapEvery { get; init; } = 5;
    /// <summary>Share of tracks from the same album as the one before (same colors, new track ID).</summary>
    public double SameAlbumShare { get; init; } = 0.25;
    /// <summary>Length of a listening session.</summary>
    public (double Min, double Max) SessionSeconds { get; init; } = (40 * 60, 80 * 60);
    /// <summary>Pause at the start of every session but the first: nothing plays, no packets. It is played under the new
    /// session's settings, so a Hide session's pause hides the light.</summary>
    public (double Min, double Max) PauseSeconds { get; init; } = (3 * 60, 12 * 60);
}

/// <summary>Counts that describe a program, for the report.</summary>
internal sealed record SoakProgramSummary(
    double Seconds, int Sessions, int Tracks, int PaletteChanges, int SameAlbumTracks, double MinBpm, double MaxBpm,
    double MusicSeconds, double SilenceSeconds, double NoPacketSeconds, long Kicks);

/// <summary>
/// A deterministic synthetic listening program for the soak: sessions of tracks with tempo, level and palette changes,
/// silence and no-packet gaps between them, and pauses between sessions. Every track is a sequence of
/// <see cref="SyntheticTrack"/> sections (intro, body, breakdown, body), which stream without allocating, and the whole
/// schedule is built up front, so playing the program allocates nothing.
/// </summary>
internal sealed class SoakProgram
{
    private readonly SoakSegment[] segments;
    private readonly SoakEvent[] events;

    private SoakProgram(int sampleRate, SoakSegment[] segments, SoakEvent[] events)
    {
        SampleRate = sampleRate;
        this.segments = segments;
        this.events = events;
        Length = segments.Length == 0 ? 0 : segments[^1].End;
        Summary = Summarize();
    }

    /// <summary>The settings a session uses while silence fades to Idle Glow (the app's default).</summary>
    public static Settings IdleGlowSettings { get; } = new();

    /// <summary>The settings a session uses while silence hides the light.</summary>
    public static Settings HideSettings { get; } = new() { WhenSilent = SilentBehavior.Hide };

    public int SampleRate { get; }

    /// <summary>Total length in samples.</summary>
    public long Length { get; }

    public double Seconds => Length / (double)SampleRate;

    public ReadOnlySpan<SoakSegment> Segments => segments;

    public ReadOnlySpan<SoakEvent> Events => events;

    public SoakProgramSummary Summary { get; }

    /// <summary>
    /// Generates the soak program: sessions alternate between <see cref="IdleGlowSettings"/> and
    /// <see cref="HideSettings"/>, start on a new device and, after the first, open with a pause; tracks have random
    /// tempo, level, sections and palettes; the gaps between tracks alternate between digital silence and no packets,
    /// and every <see cref="SoakProgramShape.LongGapEvery"/>-th one is long. Equal arguments give an identical program.
    /// </summary>
    /// <param name="seconds">Length; the last segment is cut there.</param>
    /// <param name="sampleRate">Capture rate in Hz.</param>
    /// <param name="seed">Seed for every random choice and the music itself.</param>
    /// <param name="shape">Durations and ranges.</param>
    public static SoakProgram Generate(double seconds, int sampleRate, ulong seed, SoakProgramShape shape)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        var random = new DeterministicRandom(DeterministicRandom.Hash(seed, 0x50524F4752414DUL));
        var builder = new Builder(sampleRate, seed);
        Palette? previous = null;
        int gaps = 0;
        for (int session = 0; builder.Seconds < seconds; session++)
        {
            // The new device and settings take effect first, so the pause before the music is played under them: a
            // pause into a Hide session hides the light and turns it static, one into an Idle Glow session breathes.
            builder.StartSession(session % 2 == 0 ? IdleGlowSettings : HideSettings);
            if (session > 0) builder.AddGap(Uniform(random, shape.PauseSeconds), packets: false);
            double sessionEnd = builder.Seconds + Uniform(random, shape.SessionSeconds);
            bool firstTrack = true;
            while (builder.Seconds < sessionEnd && builder.Seconds < seconds)
            {
                if (!firstTrack)
                {
                    bool longGap = ++gaps % shape.LongGapEvery == 0;
                    builder.AddGap(Uniform(random, longGap ? shape.LongGapSeconds : shape.ShortGapSeconds), packets: gaps % 2 == 1);
                }
                firstTrack = false;
                bool sameAlbum = previous is not null && random.NextDouble() < shape.SameAlbumShare;
                Palette palette = sameAlbum ? previous! with { SourceTrackId = builder.NextTrackId() } : RandomPalette(random, builder.NextTrackId());
                double length = Uniform(random, shape.TrackSeconds);
                double intro = Math.Min(Uniform(random, shape.IntroSeconds), length / 4);
                double breakdown = Math.Min(Uniform(random, shape.BreakdownSeconds), length / 4);
                double bodyA = (length - intro - breakdown) * (0.4 + 0.2 * random.NextDouble());
                builder.AddTrack(Math.Round(Uniform(random, shape.Bpm), 1), Uniform(random, shape.GainDb), palette, sameAlbum,
                    intro, bodyA, breakdown, length - intro - breakdown - bodyA, fullMixEnd: random.NextDouble() < 0.5);
                previous = palette;
            }
        }
        return builder.Build(seconds);
    }

    /// <summary>
    /// The warm-up tour: about a minute and a half that visits every path the soak measures (every segment kind,
    /// short and long gaps of both kinds, both silent behaviors and the hidden, static light, a device change, new
    /// palettes and a same-album track), so the JIT has compiled all of it before measuring starts.
    /// </summary>
    /// <param name="sampleRate">Capture rate in Hz.</param>
    /// <param name="seed">Seed for the music and palettes.</param>
    public static SoakProgram Tour(int sampleRate, ulong seed)
    {
        var random = new DeterministicRandom(DeterministicRandom.Hash(seed, 0x544F5552UL));
        var builder = new Builder(sampleRate, seed ^ 0x544F5552UL);
        builder.StartSession(IdleGlowSettings);
        builder.AddTrack(124, 0, RandomPalette(random, builder.NextTrackId()), false, 2, 6, 3, 5, fullMixEnd: true);
        builder.AddGap(3.5, packets: true);   // digital silence past the 2 s hold: Idle Glow crossfade
        Palette album = RandomPalette(random, builder.NextTrackId());
        builder.AddTrack(96, -18, album, false, 1, 4, 2, 4, fullMixEnd: false);
        builder.AddGap(3.5, packets: false);  // no packets: the analyzer feeds zeros, then skips the FFT
        builder.AddTrack(140, -6, album with { SourceTrackId = builder.NextTrackId() }, true, 1, 3, 2, 3, fullMixEnd: true);
        builder.AddGap(0.5, packets: true);
        builder.AddTrack(108, -10, RandomPalette(random, builder.NextTrackId()), false, 1, 3, 2, 3, fullMixEnd: false);
        builder.AddGap(5, packets: false);    // a pause, then a new session on a new device
        builder.StartSession(HideSettings);
        builder.AddTrack(172, -3, RandomPalette(random, builder.NextTrackId()), false, 1, 4, 2, 4, fullMixEnd: true);
        builder.AddGap(5, packets: true);     // silence hides the light; static from 3.5 s
        builder.AddTrack(80, -14, RandomPalette(random, builder.NextTrackId()), false, 1, 4, 2, 4, fullMixEnd: false);
        builder.AddGap(5, packets: false);
        builder.AddGap(0.5, packets: true);
        builder.AddTrack(150, 0, RandomPalette(random, builder.NextTrackId()), false, 1, 3, 2, 3, fullMixEnd: true);
        return builder.Build(null);
    }

    /// <summary>
    /// Copies the samples of [<paramref name="from"/>, <paramref name="to"/>) that arrive as packets into
    /// <paramref name="destination"/>, in order: music and zeros for silence, nothing for no-packet stretches.
    /// Allocates nothing.
    /// </summary>
    /// <param name="from">First sample position.</param>
    /// <param name="to">End position (exclusive), at most <see cref="Length"/>.</param>
    /// <param name="destination">At least <paramref name="to"/> − <paramref name="from"/> floats.</param>
    /// <param name="cursor">The reader's segment index; start each pass at 0. Positions must not go backwards.</param>
    /// <returns>The number of samples delivered.</returns>
    public int Read(long from, long to, Span<float> destination, ref int cursor)
    {
        int count = 0;
        while (from < to)
        {
            while (segments[cursor].End <= from) cursor++;
            SoakSegment segment = segments[cursor];
            int n = (int)(Math.Min(to, segment.End) - from);
            switch (segment.Kind)
            {
                case SoakSegmentKind.Music:
                    segment.Audio!.Position = from - segment.Start;
                    segment.Audio.Read(destination.Slice(count, n));
                    count += n;
                    break;
                case SoakSegmentKind.Silence:
                    destination.Slice(count, n).Clear();
                    count += n;
                    break;
            }
            from += n;
        }
        return count;
    }

    private SoakProgramSummary Summarize()
    {
        double music = 0, silence = 0, noPackets = 0, minBpm = double.PositiveInfinity, maxBpm = 0;
        long kicks = 0;
        foreach (SoakSegment s in segments)
        {
            double seconds = s.Length / (double)SampleRate;
            switch (s.Kind)
            {
                case SoakSegmentKind.Music:
                    music += seconds;
                    minBpm = Math.Min(minBpm, s.Bpm);
                    maxBpm = Math.Max(maxBpm, s.Bpm);
                    if (s.Audio!.Options.KickLevel > 0) kicks += (long)Math.Ceiling(seconds * s.Bpm / 60 - 1e-9);
                    break;
                case SoakSegmentKind.Silence:
                    silence += seconds;
                    break;
                default:
                    noPackets += seconds;
                    break;
            }
        }
        int sessions = 0, tracks = 0, sameAlbum = 0;
        foreach (SoakEvent e in events)
        {
            if (e.Settings is not null) sessions++;
            if (e.Palette is not null) tracks++;
            if (e.SameAlbum) sameAlbum++;
        }
        return new SoakProgramSummary(Seconds, sessions, tracks, tracks, sameAlbum, double.IsInfinity(minBpm) ? 0 : minBpm, maxBpm,
            music, silence, noPackets, kicks);
    }

    private static double Uniform(DeterministicRandom random, (double Min, double Max) range) =>
        range.Min + (range.Max - range.Min) * random.NextDouble();

    // A glow-like album palette: two saturated, bright colors at least 60° apart in hue, as linear RGB in 0..1.
    private static Palette RandomPalette(DeterministicRandom random, string trackId)
    {
        double hue = random.NextDouble() * 360;
        return new Palette(Color(hue, random), Color(hue + 60 + random.NextDouble() * 240, random), trackId);
    }

    private static Rgb Color(double hue, DeterministicRandom random)
    {
        double s = 0.55 + 0.45 * random.NextDouble(), v = 0.75 + 0.25 * random.NextDouble();
        double h = (hue % 360) / 60, c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)h switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(Linear(r + m), Linear(g + m), Linear(b + m));
    }

    private static float Linear(double srgb) =>
        (float)Math.Clamp(srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4), 0, 1);

    /// <summary>Appends segments and events; <see cref="Build"/> cuts the result to length.</summary>
    internal sealed class Builder(int sampleRate, ulong seed)
    {
        private readonly List<SoakSegment> segments = [];
        private readonly List<SoakEvent> events = [];
        private long position;
        private int trackIds;

        public double Seconds => position / (double)sampleRate;

        public string NextTrackId() => $"soak-{++trackIds}";

        /// <summary>A new session: its settings, and a new output device (the analyzer is Reset).</summary>
        public void StartSession(Settings settings) => events.Add(new SoakEvent(position, null, false, settings));

        /// <summary>A track: intro (vocals, hats), body (full mix), breakdown (vocals), body (full or plain mix).</summary>
        public void AddTrack(double bpm, double gainDb, Palette palette, bool sameAlbum,
            double intro, double bodyA, double breakdown, double bodyB, bool fullMixEnd)
        {
            events.Add(new SoakEvent(position, palette, sameAlbum, null));
            var track = new SyntheticTrackOptions { SampleRate = sampleRate, Bpm = bpm, Gain = Math.Pow(10, gainDb / 20) };
            AddMusic(track with { KickLevel = 0, BassLevel = 0, HatLevel = 0.06 }, intro, bpm);
            AddMusic(track.WithFullMix(), bodyA, bpm);
            AddMusic(track with { KickLevel = 0 }, breakdown, bpm);
            AddMusic(fullMixEnd ? track.WithFullMix() : track, bodyB, bpm);
        }

        /// <summary>A gap: digital silence when <paramref name="packets"/>, otherwise no packets at all.</summary>
        public void AddGap(double seconds, bool packets) =>
            Add(packets ? SoakSegmentKind.Silence : SoakSegmentKind.NoPackets, seconds, null, 0);

        public SoakProgram Build(double? seconds)
        {
            long end = seconds is double s ? (long)Math.Round(s * sampleRate) : position;
            var kept = new List<SoakSegment>();
            foreach (SoakSegment segment in segments)
            {
                if (segment.Start >= end) break;
                kept.Add(segment with { Length = Math.Min(segment.End, end) - segment.Start });
            }
            if (kept.Count == 0 || kept[^1].End < end) throw new ArgumentOutOfRangeException(nameof(seconds), "The program is shorter than the requested length.");
            return new SoakProgram(sampleRate, [.. kept], [.. events.Where(e => e.Position < end)]);
        }

        private void AddMusic(SyntheticTrackOptions options, double seconds, double bpm)
        {
            var audio = new SyntheticTrack(options with { Seconds = seconds, Seed = DeterministicRandom.Hash(seed, (ulong)segments.Count) });
            Add(SoakSegmentKind.Music, seconds, audio, bpm);
        }

        private void Add(SoakSegmentKind kind, double seconds, SyntheticTrack? audio, double bpm)
        {
            long length = audio?.Length ?? (long)Math.Round(seconds * sampleRate);
            if (length <= 0) return;
            segments.Add(new SoakSegment(kind, position, length, audio, bpm));
            position += length;
        }
    }
}
