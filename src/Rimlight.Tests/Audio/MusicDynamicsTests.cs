using Rimlight.AudioTools;
using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 1: the light must follow the character and the dynamics of the music. Calm songs move like a
// slow wave with no bounce on a whisper, rap and EDM keep bouncing on every kick, drums entering make a song punchier,
// and a chorus is brighter than a quiet verse. Everything runs through CoreFactory's analyzer and light engine the way
// the overlay does: 60 fps, audio in 10 ms WASAPI packets, default settings (Brightness 0.8). The songs are procedural
// (StyleTracks, no recorded music). "Before" numbers in the comments are the same tracks through Core at 5b387fc.
public sealed class MusicDynamicsTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private static readonly Lazy<StyleTrack> Ballad = new(() => StyleTracks.Render(MusicStyle.CalmBallad));
    private static readonly Lazy<StyleTrack> Rap = new(() => StyleTracks.Render(MusicStyle.Rap));
    private static readonly Lazy<StyleTrack> Edm = new(() => StyleTracks.Render(MusicStyle.Edm));
    private static readonly Lazy<StyleTrack> DrumsEnter = new(() => StyleTracks.Render(MusicStyle.BalladWithDrums));
    private static readonly Lazy<List<Frame>> BalladRun = new(() => Run(Ballad.Value.Samples));

    [Fact]
    public void ACalmBalladNeverBouncesAndMovesSlowly()
    {
        // Before: every voice syllable and low-string pluck the detector found in the whispered verses (1.1–1.8 a second)
        // was a full pulse of 1, the brightness the shader shows (Intensity × (1 + 0.25 Pulse)) had 6.6–10.2 % of its
        // variance above 3 Hz, and its 95th-percentile slope was 1.38–1.63 per second. Measured now: no pulse at all,
        // 0.2–0.7 % above 3 Hz and 0.15–0.24 per second; the bounds leave room on both.
        List<Frame> frames = BalladRun.Value;
        foreach (string name in new[] { "intro", "verse1", "chorus1", "verse2", "chorus2" })
        {
            TrackSection s = Ballad.Value.Section(name);
            Section m = Measure(frames, s.Start + (s.Start == 0 ? 3 : 1), s.End);
            output.WriteLine($"{name}: beats {m.Beats}, max pulse {m.MaxPulse:F3}, mean intensity {m.Intensity:F3}, above 3 Hz {m.HighShare:P2}, p95 slope {m.SlopeP95:F3}/s");
            Assert.True(m.Beats > 5, "the detector still hears the bass-band onsets");
            Assert.InRange(m.MaxPulse, 0, 0.15f);
            Assert.InRange(m.HighShare, 0, 0.015);
            Assert.InRange(m.SlopeP95, 0, 0.35);
        }
    }

    [Fact]
    public void AChorusIsBrighterThanTheWhisperedVerse()
    {
        // The whispered verse is 15 dB under the chorus. Before, auto-gain stretched it back over the full range within
        // about 6 s, so the second chorus was only 1.19× as bright as the verse before it. Measured now: 1.68× for both
        // choruses against the second verse. The first verse comes before anything louder has been heard, so it is judged
        // against its own opening (and SongLoudness's 4 dB prior): measured 1.18× (before 1.13×).
        List<Frame> frames = BalladRun.Value;
        double Mean(string name)
        {
            TrackSection s = Ballad.Value.Section(name);
            return Measure(frames, s.Start + 1, s.End).Intensity;
        }
        double verse1 = Mean("verse1"), chorus1 = Mean("chorus1"), verse2 = Mean("verse2"), chorus2 = Mean("chorus2");
        output.WriteLine($"verse1 {verse1:F3}, chorus1 {chorus1:F3}, verse2 {verse2:F3}, chorus2 {chorus2:F3}");
        Assert.True(chorus1 >= 1.4 * verse2, $"{chorus1:F3} vs {verse2:F3}");
        Assert.True(chorus2 >= 1.4 * verse2, $"{chorus2:F3} vs {verse2:F3}");
        Assert.True(chorus1 >= 1.05 * verse1, $"{chorus1:F3} vs {verse1:F3}");
    }

    [Theory]
    [InlineData(MusicStyle.Rap)]
    [InlineData(MusicStyle.Edm)]
    public void RapAndEdmKeepAStrongPulseOnEveryKick(MusicStyle style)
    {
        // Measured: every rap kick and 74 of 77 EDM kicks get a pulse of at least 0.7 within 0.1 s, as before (the three
        // the detector doesn't fire on, before or now, sit next to an off-beat bass note). Tempo: rap 90.1 BPM (before
        // 90.1); EDM 128.2 (before 246.4: the off-beat bass doubled it, see BeatDetector.TempoAccentRatio).
        StyleTrack track = style == MusicStyle.Rap ? Rap.Value : Edm.Value;
        List<Frame> frames = Run(track.Samples);
        var (kicks, strong, meanPulse) = Kicks(frames, track.KickTimes, 4);
        float bpm = frames[^1].Bpm;
        output.WriteLine($"{style}: {strong}/{kicks} kicks with a pulse of 0.7 or more, mean {meanPulse:F2}, {bpm:F2} BPM");
        Assert.True(strong >= 0.9 * kicks, $"{strong}/{kicks}");
        Assert.True(meanPulse >= 0.9, $"{meanPulse:F2}");
        Assert.InRange(bpm, track.Bpm - 2, track.Bpm + 2);
    }

    [Fact]
    public void AnEdmBreakdownWithoutDrumsDoesNotPulse()
    {
        // Pad and lead onsets in the breakdown fired 18 full pulses before; now none.
        List<Frame> frames = Run(Edm.Value.Samples);
        TrackSection breakdown = Edm.Value.Section("breakdown");
        Section m = Measure(frames, breakdown.Start + 0.5, breakdown.End);
        output.WriteLine($"breakdown: beats {m.Beats}, max pulse {m.MaxPulse:F3}");
        Assert.InRange(m.MaxPulse, 0, 0.15f);
    }

    [Fact]
    public void PulsesStartWhenTheDrumsComeInAndStopWhenTheyLeave()
    {
        // The verse fired 22 full pulses before, and the verse after the drums 13. Now the drumless verses give none,
        // the chorus kicks the detector finds give full ones, and the punch (how drum-driven the music is) rises with the
        // drums and fades once they leave. The detector misses 5 of the 23 acoustic kicks, before and now.
        StyleTrack track = DrumsEnter.Value;
        List<Frame> frames = Run(track.Samples);
        TrackSection verse = track.Section("verse"), chorus = track.Section("chorus"), verse2 = track.Section("verse2");
        Assert.InRange(Measure(frames, verse.Start + 1, verse.End).MaxPulse, 0, 0.15f);
        Assert.InRange(Measure(frames, verse2.Start, verse2.End).MaxPulse, 0, 0.15f);

        Frame first = frames.First(f => f.Light.Pulse >= 0.7f);
        output.WriteLine($"first strong pulse at {first.Time:F2} s (drums from {chorus.Start} s)");
        Assert.InRange(first.Time, chorus.Start, chorus.Start + 2.5);

        int detected = 0, strong = 0;
        foreach (double kick in track.KickTimes)
        {
            var window = frames.Where(f => f.Time >= kick && f.Time <= kick + 0.1).ToList();
            if (!window.Any(f => f.Fired > 0)) continue;
            detected++;
            if (window.Max(f => f.Light.Pulse) >= 0.7f) strong++;
        }
        output.WriteLine($"{strong}/{detected} detected kicks strong");
        Assert.True(detected >= 15 && strong >= 0.9 * detected, $"{strong}/{detected}");

        double punchInChorus = frames.Where(f => f.Time >= chorus.Start + 4 && f.Time < chorus.End).Average(f => f.Punch);
        double punchAfter = frames.Where(f => f.Time >= verse2.Start + 8).Max(f => f.Punch);
        output.WriteLine($"punch in the chorus {punchInChorus:F2}, at most {punchAfter:F2} from 8 s after the drums stop");
        Assert.True(punchInChorus >= 0.8, $"{punchInChorus:F2}");
        Assert.True(punchAfter <= 0.3, $"{punchAfter:F2}");
    }

    [Theory]
    [InlineData(StyleParts.Voice)]
    [InlineData(StyleParts.Guitar)]
    public void VoiceAndGuitarOnsetsAreNotKicks(StyleParts part)
    {
        // A baritone's syllables (fundamental 98–147 Hz) and the guitar's low strings fill the 30–150 Hz beat band, and the
        // detector fires on them alone (each at the ballad's section levels); none is kick-like, so none pulses.
        StyleTrack stem = StyleTracks.Render(MusicStyle.CalmBallad, parts: part);
        List<Frame> frames = Run(stem.Samples);
        int beats = frames.Sum(f => f.Fired);
        float maxPulse = frames.Max(f => f.Light.Pulse);
        output.WriteLine($"{part} alone: {beats} beats, max pulse {maxPulse:F3}");
        Assert.True(beats >= 50, $"{beats}");
        Assert.InRange(maxPulse, 0, 0.15f);
    }

    [Fact]
    public void TheWholeSongAtAnotherVolumeLooksTheSame()
    {
        // Doc 03 §5's volume invariance through the whole chain: the same song at −20 dB gives the same light after 2 s.
        List<Frame> loud = BalladRun.Value;
        List<Frame> quiet = Run(Ballad.Value.Samples.Select(x => x * 0.1f).ToArray());
        float worst = 0;
        for (int i = 0; i < loud.Count; i++)
            if (loud[i].Time >= 2) worst = MathF.Max(worst, MathF.Abs(loud[i].Light.Intensity - quiet[i].Light.Intensity) + MathF.Abs(loud[i].Light.Pulse - quiet[i].Light.Pulse));
        output.WriteLine($"largest difference from 2 s: {worst:F4}");
        Assert.InRange(worst, 0, 0.02f);
    }

    [Fact]
    public void AVolumeChangeMidSongSettlesWithinHalfAMinute()
    {
        // A volume change shifts every loudness by the same dB, so the song reference has to turn over its memory before
        // the light looks as it did: the new warm-up (doc 03 §2 asks for 2 s, against a 4 s auto-gain that also flattened
        // the song's own dynamics). Measured on three rap tracks in a row: |ΔIntensity| last above 0.1 23.5 s after a
        // 20 dB step down (before: 5.2 s) and 6.0 s after a 20 dB step up (before: 15.3 s).
        float[] song = Enumerable.Repeat(Rap.Value.Samples, 3).SelectMany(x => x).ToArray();
        const double Step = 40;
        List<Frame> reference = Run(song);
        foreach (var (gainBefore, gainAfter, settle) in new[] { (1f, 0.1f, 30.0), (0.1f, 1f, 10.0) })
        {
            float[] stepped = song.Select((x, i) => x * (i < Step * Rate ? gainBefore : gainAfter)).ToArray();
            List<Frame> frames = Run(stepped);
            double last = 0;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].Time >= Step && MathF.Abs(frames[i].Light.Intensity - reference[i].Light.Intensity) > 0.1f) last = frames[i].Time - Step;
            output.WriteLine($"{20 * MathF.Log10(gainAfter / gainBefore):+0;-0} dB: last difference above 0.1 at +{last:F1} s");
            Assert.InRange(last, 0, settle);
        }
    }

    [Fact]
    public void SensitivityScalesThePulse()
    {
        // The Sensitivity setting still scales everything: the beat threshold and Level (doc 03 §2) and now each pulse.
        foreach (var (sensitivity, low, high) in new[] { (0.5f, 0.4, 0.6), (2f, 0.95, 1.0) })
        {
            List<Frame> frames = Run(Rap.Value.Samples, sensitivity);
            var (kicks, _, meanPulse) = Kicks(frames, Rap.Value.KickTimes, 4);
            output.WriteLine($"Sensitivity {sensitivity}: mean pulse {meanPulse:F2} over {kicks} kicks");
            Assert.InRange(meanPulse, low, high);
        }
    }

    [Fact]
    public void AStyleTrackAllocatesNothingPerFrame()
    {
        // The wave, the song reference, the grading and the tempo accent run on the render thread: 0 bytes per frame,
        // through drumless verses, choruses and the punch fading in and out.
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        ILightEngine engine = CoreFactory.CreateLightEngine();
        var settings = new Settings();
        float[] samples = DrumsEnter.Value.Samples;
        int position = 0;
        void Step(int count)
        {
            AudioFeatures audio = analyzer.Process(samples.AsSpan(position, count), Rate, 1f / 60);
            engine.Update(1f / 60, audio, Palette.Default, settings, false);
            position += count;
        }
        for (int i = 0; i < 300; i++) Step(i % 2 == 0 ? 480 : 960);
        long before = GC.GetAllocatedBytesForCurrentThread();
        while (position + 960 <= samples.Length) Step(position / 480 % 2 == 0 ? 480 : 960);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- the overlay's loop, offline ----

    internal readonly record struct Frame(double Time, AudioFeatures Audio, LightState Light, int Fired, float Punch, float Bpm);

    private readonly record struct Section(int Beats, float MaxPulse, double Intensity, double HighShare, double SlopeP95);

    internal static List<Frame> Run(float[] samples, float sensitivity = 1)
    {
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = sensitivity });
        ILightEngine engine = CoreFactory.CreateLightEngine();
        var settings = new Settings { Sensitivity = sensitivity };
        var character = ((AudioAnalyzer)analyzer).Character;
        const int Packet = Rate / 100;
        double time = 0, end = samples.Length / (double)Rate;
        int position = 0, beats = 0;
        var frames = new List<Frame>((int)(end * 60) + 2);
        while (time < end)
        {
            time += 1.0 / 60;
            long arrived = Math.Min(samples.Length, (long)(time * Rate) / Packet * Packet);
            int count = (int)Math.Max(0, arrived - position);
            AudioFeatures audio = analyzer.Process(samples.AsSpan(position, count), Rate, 1f / 60);
            position += count;
            LightState light = engine.Update(1f / 60, audio, Palette.Default, settings, false);
            int fired = analyzer.Diagnostics.BeatCount - beats;
            beats = analyzer.Diagnostics.BeatCount;
            frames.Add(new Frame(time, audio, light, fired, character.Punch, analyzer.Diagnostics.EstimatedBpm));
        }
        return frames;
    }

    // Beats, the largest pulse, and the brightness the shader shows (Intensity × (1 + 0.25 Pulse), Glow.hlsl): its mean,
    // the share of its variance above 3 Hz, and the 95th percentile of its slope per second.
    private static Section Measure(List<Frame> frames, double from, double to)
    {
        var f = frames.Where(x => x.Time >= from && x.Time < to).ToList();
        double[] shown = f.Select(x => (double)x.Light.Intensity * (1 + 0.25 * x.Light.Pulse)).ToArray();
        double mean = shown.Average(), total = 0, high = 0;
        int n = shown.Length;
        for (int k = 1; k <= n / 2; k++)
        {
            double re = 0, im = 0, w = 2 * Math.PI * k / n;
            for (int i = 0; i < n; i++)
            {
                re += (shown[i] - mean) * Math.Cos(w * i);
                im -= (shown[i] - mean) * Math.Sin(w * i);
            }
            total += re * re + im * im;
            if (k * 60.0 / n > 3) high += re * re + im * im;
        }
        double[] slopes = new double[n - 1];
        for (int i = 1; i < n; i++) slopes[i - 1] = Math.Abs(shown[i] - shown[i - 1]) * 60;
        Array.Sort(slopes);
        return new Section(f.Sum(x => x.Fired), f.Max(x => x.Light.Pulse), f.Average(x => x.Light.Intensity),
            total > 0 ? high / total : 0, slopes[(int)(0.95 * (slopes.Length - 1))]);
    }

    // Kicks from `from` on: how many, how many got a pulse of 0.7 or more within 0.1 s, and the mean of those peaks.
    private static (int Kicks, int Strong, double MeanPulse) Kicks(List<Frame> frames, IReadOnlyList<double> kicks, double from)
    {
        int count = 0, strong = 0;
        double sum = 0;
        foreach (double kick in kicks)
        {
            if (kick < from || kick + 0.1 > frames[^1].Time) continue;
            float peak = frames.Where(f => f.Time >= kick && f.Time <= kick + 0.1).Max(f => f.Light.Pulse);
            count++;
            sum += peak;
            if (peak >= 0.7f) strong++;
        }
        return (count, strong, count > 0 ? sum / count : 0);
    }
}
