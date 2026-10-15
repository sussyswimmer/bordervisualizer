using System.Globalization;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.Bench;

/// <summary>What the loopback stream carries during a segment.</summary>
internal enum SoakAudio
{
    /// <summary>A synthetic track (doc 03 §1: 10 ms packets).</summary>
    Music,
    /// <summary>Packets of digital silence, as while a player keeps its stream open between tracks.</summary>
    SilentPackets,
    /// <summary>No packets at all, as WASAPI loopback does while nothing plays (doc 03 §1).</summary>
    NoPackets,
}

/// <summary>One stretch of the simulated loopback stream.</summary>
/// <param name="Start">Start time in seconds.</param>
/// <param name="Seconds">Length in seconds.</param>
/// <param name="Audio">What the stream carries.</param>
/// <param name="Track">The track, for <see cref="SoakAudio.Music"/>.</param>
/// <param name="Album">The album palette the media service publishes for the track
/// <see cref="SoakSchedule.AlbumDelaySeconds"/> after it starts; null for a track without art.</param>
internal sealed record SoakSegment(double Start, double Seconds, SoakAudio Audio, SyntheticTrackOptions? Track = null, Palette? Album = null)
{
    /// <summary>End time in seconds.</summary>
    public double End => Start + Seconds;
}

/// <summary>Kinds of <see cref="SoakEvent"/>.</summary>
internal enum SoakEventKind
{
    /// <summary>A new settings snapshot (an edit in Settings, or the hotkey).</summary>
    Settings,
    /// <summary>The overlay pauses (fullscreen app, doc 04 §4).</summary>
    Pause,
    /// <summary>The pause ends.</summary>
    Resume,
    /// <summary>The render thread stalls (a window drag, a GPU hiccup): the next frame comes late.</summary>
    Stall,
}

/// <summary>Something that happens to the app at a moment of the schedule.</summary>
/// <param name="Time">When, in seconds.</param>
/// <param name="Kind">What.</param>
/// <param name="Label">A short description, e.g. "WhenSilent Hide".</param>
/// <param name="Settings">The new snapshot, for <see cref="SoakEventKind.Settings"/>.</param>
/// <param name="StallSeconds">How late the next frame is, for <see cref="SoakEventKind.Stall"/>.</param>
internal sealed record SoakEvent(double Time, SoakEventKind Kind, string Label, SettingsSnapshot? Settings = null, double StallSeconds = 0);

// The simulated day of listening a soak runs: back-to-back segments of the loopback stream (tracks and the silent gaps
// between them) and the events around them (settings edits, pauses, stalls). Everything, settings records and
// palettes included, is built here, before the run, so nothing the schedule needs is allocated on the measured path.
// Generate is a pure function of its arguments (SplitMix64, no Random), so a seed always gives the same day.
internal sealed class SoakSchedule
{
    /// <summary>The media service publishes a track's palette this long after the track starts (K4's debounce + decode).</summary>
    public const double AlbumDelaySeconds = 0.5;

    // Tempos are spread over common genres; the first track is the C3 soak's 124 BPM full mix.
    private static readonly double[] Tempos = [86, 92, 100, 108, 116, 120, 124, 128, 132, 140, 150, 160, 174];

    /// <summary>Creates a schedule.</summary>
    /// <param name="seconds">The simulated length; the segments must cover it.</param>
    /// <param name="initial">The settings at the start.</param>
    /// <param name="segments">Back-to-back segments from 0.</param>
    /// <param name="events">Events in time order.</param>
    /// <exception cref="ArgumentException">The segments leave a hole or don't cover the length, or the events are out of order.</exception>
    public SoakSchedule(double seconds, SettingsSnapshot initial, IReadOnlyList<SoakSegment> segments, IReadOnlyList<SoakEvent> events)
    {
        if (!(seconds > 0)) throw new ArgumentOutOfRangeException(nameof(seconds));
        double end = 0;
        foreach (SoakSegment segment in segments)
        {
            if (Math.Abs(segment.Start - end) > 1e-9 || !(segment.Seconds > 0) || (segment.Audio == SoakAudio.Music) != (segment.Track is not null))
                throw new ArgumentException("Segments must follow each other from 0, and only music has a track.", nameof(segments));
            end = segment.End;
        }
        if (end < seconds) throw new ArgumentException("The segments end before the schedule does.", nameof(segments));
        for (int i = 0; i < events.Count; i++)
        {
            if (i > 0 && events[i].Time < events[i - 1].Time) throw new ArgumentException("Events must be in time order.", nameof(events));
            if ((events[i].Kind == SoakEventKind.Settings) != (events[i].Settings is not null))
                throw new ArgumentException("Only settings events carry settings.", nameof(events));
        }
        Seconds = seconds;
        Initial = initial;
        Segments = segments;
        Events = events;
    }

    /// <summary>The simulated length in seconds.</summary>
    public double Seconds { get; }
    /// <summary>The settings at the start.</summary>
    public SettingsSnapshot Initial { get; }
    /// <summary>Back-to-back segments from 0 to at least <see cref="Seconds"/>.</summary>
    public IReadOnlyList<SoakSegment> Segments { get; }
    /// <summary>Events in time order.</summary>
    public IReadOnlyList<SoakEvent> Events { get; }

    /// <summary>
    /// A realistic day of listening:
    /// <list type="bullet">
    /// <item>Tracks of 2.5–5.5 minutes at 86–174 BPM, 75 % full mixes and 25 % kick + vocal tones, at −24 to 0 dB (the
    /// analyzer's auto-gain must not care). Each brings a new album palette (800 ms crossfade); 25 % keep the previous
    /// track's colors (same album) and 10 % have no art (manual colors).</item>
    /// <item>A gap before every track after the first, half with packets of digital silence and half with no packets:
    /// 60 % of 0.3–2.5 s (too short for IsSilent), 30 % of 4–30 s and 10 % of 1–10 minutes (the music was paused).</item>
    /// <item>About every 15 minutes a settings edit, from 2 minutes in: WhenSilent toggled (the first edit, so both silent
    /// behaviors run), Brightness, Sensitivity (a new analyzer tuning) or PrimaryRatio (a gradient refill).</item>
    /// <item>About every 100 minutes, from 20 minutes in, 2–15 minutes away: a fullscreen pause or the hotkey (Enabled
    /// off), so the glow fades out and the render thread blocks.</item>
    /// <item>About every 5 minutes, from 1 minute in, a render-thread stall: 70 % of 30–120 ms, 25 % of 120–250 ms and 5 %
    /// of 0.25–1.5 s (the overlay then passes one ordinary frame and the analyzer gets the whole backlog).</item>
    /// </list>
    /// </summary>
    /// <param name="seconds">The simulated length.</param>
    /// <param name="sampleRate">The capture rate of the tracks.</param>
    /// <param name="seed">The seed; equal arguments give equal schedules.</param>
    /// <returns>The schedule.</returns>
    public static SoakSchedule Generate(double seconds, int sampleRate, ulong seed)
    {
        if (!(seconds > 0) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        var random = new DeterministicRandom(DeterministicRandom.Hash(seed, 0x7363686564756C65)); // "schedule"
        double Uniform(double min, double max) => min + (max - min) * random.NextDouble();
        double Exponential(double mean) => -mean * Math.Log(1 - random.NextDouble());

        var segments = new List<SoakSegment>();
        Palette? album = null;
        double t = 0;
        for (int track = 0; t < seconds; track++)
        {
            if (track > 0)
            {
                double kind = random.NextDouble();
                double gap = kind < 0.6 ? Uniform(0.3, 2.5) : kind < 0.9 ? Uniform(4, 30) : Uniform(60, 600);
                segments.Add(new SoakSegment(t, gap, random.NextDouble() < 0.5 ? SoakAudio.SilentPackets : SoakAudio.NoPackets));
                t += gap;
            }
            bool first = track == 0;
            double length = first ? 240 : Uniform(150, 330);
            var options = new SyntheticTrackOptions
            {
                SampleRate = sampleRate,
                Seconds = length,
                Bpm = first ? 124 : Tempos[random.NextInt(Tempos.Length)],
                Gain = first ? 1 : Math.Pow(10, Uniform(-24, 0) / 20),
                Seed = DeterministicRandom.Hash(seed, (ulong)track),
            };
            if (first || random.NextDouble() < 0.75) options = options.WithFullMix();
            string id = string.Create(CultureInfo.InvariantCulture, $"track {track}");
            double art = first ? 1 : random.NextDouble();
            album = art < 0.1 ? null
                : art < 0.35 && album is not null ? album with { SourceTrackId = id }
                : RandomPalette(random, id);
            segments.Add(new SoakSegment(t, length, SoakAudio.Music, options, album));
            t += length;
        }

        // Moments first, then the settings records in time order, so each edit builds on the one before it.
        var moments = new List<(double Time, int Kind, double Value)>();
        const int Edit = 0, AwayStart = 1, AwayEnd = 2, Stall = 3;
        for (double s = 120 + Exponential(900); s < seconds; s += Exponential(900)) moments.Add((s, Edit, 0));
        for (double s = 1200 + Exponential(6000); s < seconds; s += Exponential(6000))
        {
            double length = Uniform(120, 900), hotkey = random.NextDouble() < 0.5 ? 1 : 0;
            moments.Add((s, AwayStart, hotkey));
            if (s + length < seconds) moments.Add((s + length, AwayEnd, hotkey));
            s += length;
        }
        for (double s = 60 + Exponential(300); s < seconds; s += Exponential(300))
        {
            double kind = random.NextDouble();
            moments.Add((s, Stall, kind < 0.7 ? Uniform(0.03, 0.12) : kind < 0.95 ? Uniform(0.12, 0.25) : Uniform(0.25, 1.5)));
        }
        moments.Sort((a, b) => a.Time.CompareTo(b.Time));

        var initial = SettingsSnapshot.For(new Settings());
        Settings current = initial.Settings;
        var events = new List<SoakEvent>(moments.Count);
        bool firstEdit = true;
        foreach ((double time, int kind, double value) in moments)
        {
            switch (kind)
            {
                case Edit:
                    int what = firstEdit ? 0 : random.NextInt(4);
                    firstEdit = false;
                    (current, string label) = what switch
                    {
                        0 => Toggle(current),
                        1 => Edited(current with { Brightness = Round(Uniform(0.3, 1)) }, "Brightness", current.Brightness),
                        2 => Edited(current with { Sensitivity = Round(Uniform(0.5, 2)) }, "Sensitivity", current.Sensitivity),
                        _ => Edited(current with { PrimaryRatio = Round(Uniform(0.3, 0.8)) }, "PrimaryRatio", current.PrimaryRatio),
                    };
                    events.Add(new SoakEvent(time, SoakEventKind.Settings, label, SettingsSnapshot.For(current)));
                    break;
                case AwayStart when value == 1:
                    current = current with { Enabled = false };
                    events.Add(new SoakEvent(time, SoakEventKind.Settings, "hotkey: off", SettingsSnapshot.For(current)));
                    break;
                case AwayStart:
                    events.Add(new SoakEvent(time, SoakEventKind.Pause, "fullscreen: pause"));
                    break;
                case AwayEnd when value == 1:
                    current = current with { Enabled = true };
                    events.Add(new SoakEvent(time, SoakEventKind.Settings, "hotkey: on", SettingsSnapshot.For(current)));
                    break;
                case AwayEnd:
                    events.Add(new SoakEvent(time, SoakEventKind.Resume, "fullscreen: resume"));
                    break;
                default:
                    events.Add(new SoakEvent(time, SoakEventKind.Stall, "stall", StallSeconds: value));
                    break;
            }
        }
        return new SoakSchedule(seconds, initial, segments, events);

        static float Round(double value) => (float)Math.Round(value, 2);
        static (Settings, string) Toggle(Settings s)
        {
            SilentBehavior next = s.WhenSilent == SilentBehavior.Hide ? SilentBehavior.IdleGlow : SilentBehavior.Hide;
            return (s with { WhenSilent = next }, $"WhenSilent {next}");
        }
        static (Settings, string) Edited(Settings s, string name, float before) =>
            (s, string.Create(CultureInfo.InvariantCulture, $"{name} {before:0.##} -> {Value(s, name):0.##}"));
        static float Value(Settings s, string name) => name switch
        {
            "Brightness" => s.Brightness,
            "Sensitivity" => s.Sensitivity,
            _ => s.PrimaryRatio,
        };
    }

    /// <summary>
    /// A short scripted scene that takes every path of the frame in 80 simulated seconds: music, a gap without packets
    /// that settles into Idle Glow, a crossfade to new colors, WhenSilent = Hide with a gap of silent packets (the glow
    /// hides and only listens), a track without art, Sensitivity and ratio edits, a stall, a fullscreen pause and the
    /// hotkey (the render thread blocks), and back. For warming up the JIT before a soak, and for tests.
    /// </summary>
    /// <param name="sampleRate">The capture rate of the tracks.</param>
    /// <returns>The scene.</returns>
    public static SoakSchedule Scene(int sampleRate)
    {
        var a = new Palette(new Rgb(0.9f, 0.2f, 0.1f), new Rgb(0.1f, 0.3f, 0.9f), "scene a");
        var b = new Palette(new Rgb(0.1f, 0.8f, 0.3f), new Rgb(0.8f, 0.7f, 0.1f), "scene b");
        SyntheticTrackOptions Track(double seconds, double bpm, double gain, bool fullMix)
        {
            var options = new SyntheticTrackOptions { SampleRate = sampleRate, Seconds = seconds, Bpm = bpm, Gain = gain, Seed = (ulong)bpm };
            return fullMix ? options.WithFullMix() : options;
        }
        SoakSegment[] segments =
        [
            new(0, 14, SoakAudio.Music, Track(14, 124, 1, true), a),
            new(14, 6, SoakAudio.NoPackets),                                   // IsSilent at ~16 s, Idle Glow at ~18 s
            new(20, 10, SoakAudio.Music, Track(10, 128, 0.1, false), b),       // −20 dB, new colors
            new(30, 6, SoakAudio.SilentPackets),                               // Hide: hidden from ~34 s, listening
            new(36, 24, SoakAudio.Music, Track(24, 100, 1, true), null),       // no art: manual colors
            new(60, 4, SoakAudio.SilentPackets),
            new(64, 16, SoakAudio.Music, Track(16, 140, 0.5, true), a with { SourceTrackId = "scene d" }),
        ];
        var initial = SettingsSnapshot.For(new Settings());
        Settings hide = initial.Settings with { WhenSilent = SilentBehavior.Hide, Brightness = 0.6f };
        Settings edited = hide with { Sensitivity = 1.5f, PrimaryRatio = 0.7f };
        SoakEvent[] events =
        [
            new(26, SoakEventKind.Settings, "WhenSilent Hide", SettingsSnapshot.For(hide)),
            new(40, SoakEventKind.Settings, "Sensitivity 1.5, PrimaryRatio 0.7", SettingsSnapshot.For(edited)),
            new(42, SoakEventKind.Stall, "stall", StallSeconds: 0.4),
            new(44, SoakEventKind.Pause, "fullscreen: pause"),
            new(48, SoakEventKind.Resume, "fullscreen: resume"),
            new(52, SoakEventKind.Settings, "hotkey: off", SettingsSnapshot.For(edited with { Enabled = false })),
            new(55, SoakEventKind.Settings, "hotkey: on", SettingsSnapshot.For(edited)),
            new(66, SoakEventKind.Settings, "WhenSilent IdleGlow", SettingsSnapshot.For(edited with { WhenSilent = SilentBehavior.IdleGlow })),
        ];
        return new SoakSchedule(80, initial, segments, events);
    }

    // A glow-like pair: saturated, not too dark (the extractor's glow-ify keeps OkLCh lightness 0.55..0.85).
    private static Palette RandomPalette(DeterministicRandom random, string trackId)
    {
        Rgb Color()
        {
            float r = (float)random.NextDouble(), g = (float)random.NextDouble(), b = (float)random.NextDouble();
            float max = MathF.Max(0.2f, MathF.Max(r, MathF.Max(g, b)));
            return new Rgb(r / max, g / max, b / max);
        }
        return new Palette(Color(), Color(), trackId);
    }
}
