using Rimlight.AudioTools;
using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, rounds 1 and 2: the light must follow the character and the dynamics of the music. Calm songs
// move like a slow wave with no bounce on a whisper, rap and EDM hit hard and on time on every kick, drums entering
// make a song punchier, a bounce is as big as the moment, a bass guitar or a piano is not a drum, and a chorus is
// brighter than a quiet verse. Everything runs through CoreFactory's analyzer and light engine the way the overlay
// does: 60 fps, audio in 10 ms WASAPI packets, default settings (Brightness 0.8) but the Pulse movement, whose pulse is
// the whole beat. The songs are procedural (StyleTracks, no recorded music); round 2 tuned on real recordings with
// independently annotated kicks and kept these as the regression floor. "Before" numbers in the comments are the same
// tracks through Core at 5b387fc, "round 1" the round 1 change as measured then (its first version, and 6ac553b after
// the review), "now" round 2.
public sealed class MusicDynamicsTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private static readonly Lazy<StyleTrack> Ballad = new(() => StyleTracks.Render(MusicStyle.CalmBallad));
    private static readonly Lazy<StyleTrack> Rap = new(() => StyleTracks.Render(MusicStyle.Rap));
    private static readonly Lazy<StyleTrack> Edm = new(() => StyleTracks.Render(MusicStyle.Edm));
    private static readonly Lazy<StyleTrack> DrumsEnter = new(() => StyleTracks.Render(MusicStyle.BalladWithDrums));
    private static readonly Lazy<StyleTrack> FolkMusic = new(() => StyleTracks.Render(MusicStyle.FolkBand, parts: StyleParts.Guitar | StyleParts.Voice));
    private static readonly Lazy<StyleTrack> FolkBass = new(() => StyleTracks.Render(MusicStyle.FolkBand, parts: StyleParts.Bass));
    private static readonly Lazy<StyleTrack> FolkDrums = new(() => StyleTracks.Render(MusicStyle.FolkBand, parts: StyleParts.Drums));
    private static readonly Lazy<List<Frame>> BalladRun = new(() => Run(Ballad.Value.Samples));

    // The folk band with its bass guitar 6 dB under the music and its brush kit at drumsDb (or none).
    private static StyleTrack Folk(double? drumsDb) => drumsDb is double db
        ? StyleTracks.Mix(FolkMusic.Value, (FolkBass.Value, -6), (FolkDrums.Value, db))
        : StyleTracks.Mix(FolkMusic.Value, (FolkBass.Value, -6));

    [Fact]
    public void ACalmBalladNeverBouncesAndMovesSlowly()
    {
        // Before: every voice syllable and low-string pluck the detector found in the whispered verses (1.1–1.8 a second)
        // was a full pulse of 1, the brightness the shader shows (Intensity × (1 + 0.25 Pulse)) had 6.6–10.2 % of its
        // variance above 3 Hz (an unwindowed DFT), and its 95th-percentile slope was 1.38–1.63 per second; round 1: no
        // pulse, 0.2–0.7 % and 0.15–0.30 per second. Measured now: no pulse, 0.00 % above 3 Hz (windowed, Measure) and
        // 0.03–0.12 per second. Round 2 kept round 1's bounds (1.5 % and 0.35 per second), which round 1's own light
        // passed: its review found that a calm Level at the groove's speed (AudioAnalyzer's wave factors at 1) left every
        // test green. The bounds are now near what is measured (TheWaveIsSlowWithoutDrumsAndQuickInAGroove measures the
        // time constants themselves).
        List<Frame> frames = BalladRun.Value;
        foreach (string name in new[] { "intro", "verse1", "chorus1", "verse2", "chorus2" })
        {
            TrackSection s = Ballad.Value.Section(name);
            Section m = Measure(frames, s.Start + (s.Start == 0 ? 3 : 1), s.End);
            output.WriteLine($"{name}: beats {m.Beats}, max pulse {m.MaxPulse:F3}, mean intensity {m.Intensity:F3}, above 3 Hz {m.HighShare:P2}, p95 slope {m.SlopeP95:F3}/s");
            Assert.True(m.Beats > 5, "the detector still hears the bass-band onsets");
            Assert.InRange(m.MaxPulse, 0, 0.15f);
            Assert.InRange(m.HighShare, 0, 0.002);
            Assert.InRange(m.SlopeP95, 0, 0.15);
        }
    }

    [Fact]
    public void AChorusIsBrighterThanTheWhisperedVerse()
    {
        // The whispered verse is 15 dB under the chorus. Before, auto-gain stretched it back over the full range within
        // about 6 s, so the second chorus was only 1.19× as bright as the verse before it (round 1: 1.58× and 1.59× for
        // the choruses). Measured now: 2.05× and 2.00×. The first verse comes before anything louder has been heard, so
        // it is judged against its own opening (and SongLoudness's 4 dB prior): measured 1.16× (before 1.13×, round 1
        // 1.11×).
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
    [InlineData(MusicStyle.Rap, 48000, 1)]
    [InlineData(MusicStyle.Rap, 44100, 1)]
    [InlineData(MusicStyle.Edm, 48000, 1)]
    [InlineData(MusicStyle.Edm, 44100, 1)]
    [InlineData(MusicStyle.Edm, 48000, 5)]
    [InlineData(MusicStyle.Edm, 48000, 6)]
    public void RapAndEdmKeepAStrongPulseOnEveryKick(MusicStyle style, int rate, ulong seed)
    {
        // Measured now: every rap kick at 48 and 44.1 kHz and 72–77 of 77 EDM kicks get a pulse of at least 0.7 within
        // 0.1 s, averaging 0.96–1.00 (the few others the detector doesn't fire on, before or now: they sit next to an
        // off-beat bass note; round 2 measured 75–76 with the beats it let fire inside the refractory time). Tempo
        // counts the accented kicks only: rap 90.1 BPM, EDM 127.7–128.0 at both rates and every seed (before 246–257
        // and early round 1 128–169: the off-beat bass doubled it, BeatCharacter.TempoBeatAge).
        StyleTrack track = rate == Rate && seed == 1 ? (style == MusicStyle.Rap ? Rap.Value : Edm.Value) : StyleTracks.Render(style, rate, seed);
        List<Frame> frames = Run(track.Samples, rate: rate);
        var (kicks, strong, meanPulse) = Kicks(frames, track.KickTimes, 4);
        float bpm = frames[^1].Bpm;
        output.WriteLine($"{style} at {rate} Hz, seed {seed}: {strong}/{kicks} kicks with a pulse of 0.7 or more, mean {meanPulse:F2}, {bpm:F2} BPM");
        Assert.True(strong >= 0.9 * kicks, $"{strong}/{kicks}");
        Assert.True(meanPulse >= 0.9, $"{meanPulse:F2}");
        Assert.InRange(bpm, track.Bpm - 2, track.Bpm + 2);
        // And through the last 6 s, not only on the last frame.
        Assert.All(frames.Where(f => f.Time >= track.Seconds - 6), f => Assert.InRange(f.Bpm, track.Bpm - 2, track.Bpm + 2));
    }

    [Fact]
    public void InAGrooveThePulseLandsOnTheFrameItsBeatFires()
    {
        // Round 1 graded each beat once its 35 ms onset window had filled the FFT, so on round 2's corpus of real music
        // the pulse peaked 46–53 ms after the kick, a frame or two after the beat fired. The grade is now decided on the
        // step the beat fires, from KickEnvelope's time-domain evidence: in a groove the frame that fires a kick's beat
        // already shows its pulse. Measured: 0.9 or more on that frame for all 61 rap kicks after the first 4 s.
        List<Frame> frames = Run(Rap.Value.Samples);
        int beats = 0, onTime = 0;
        foreach (double kick in Rap.Value.KickTimes.Where(k => k >= 4))
        {
            int i = frames.FindIndex(f => f.Time >= kick && f.Time <= kick + 0.1 && f.Fired > 0);
            if (i < 0) continue;
            beats++;
            if (frames[i].Light.Pulse >= 0.9f) onTime++;
        }
        output.WriteLine($"{onTime}/{beats} kick beats pulse 0.9 or more on the frame they fire");
        Assert.True(beats >= 50, $"{beats}");
        Assert.True(onTime >= 0.9 * beats, $"{onTime}/{beats}");
    }

    [Fact]
    public void AGroovePumpsTheLightBetweenKicks()
    {
        // In a groove the light dips between kicks and comes back with each pulse (AudioAnalyzer.PumpDepth). Measured on
        // the brightness the shader shows, from 30 ms before each kick to its peak: the rap's kicks brighten it by 55 %
        // on average and 41 % at least (round 1: 25 % at most, a full pulse's own 1.25). A calm song has no groove and no
        // pump: its Level is the slow wave alone (ACalmBalladNeverBouncesAndMovesSlowly).
        List<Frame> frames = Run(Rap.Value.Samples);
        double Shown(Frame f) => f.Light.Intensity * (1 + 0.25 * f.Light.Pulse);
        var depths = new List<double>();
        foreach (double kick in Rap.Value.KickTimes.Where(k => k >= 4 && k + 0.15 < frames[^1].Time))
        {
            Frame before = frames.Last(f => f.Time <= kick - 0.03);
            double peak = frames.Where(f => f.Time >= kick - 0.02 && f.Time <= kick + 0.15).Max(Shown);
            depths.Add(peak / Shown(before) - 1);
        }
        output.WriteLine($"rap: the shown light rises {depths.Average():P0} on a kick on average (least {depths.Min():P0})");
        Assert.True(depths.Average() >= 0.35, $"{depths.Average():P0}");
        Assert.True(depths.Min() >= 0.2, $"{depths.Min():P0}");
    }

    [Fact]
    public void AnEdmBreakdownWithoutDrumsDoesNotPulse()
    {
        // Pad and lead onsets in the breakdown fired 18 full pulses before; now none over 0.06.
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
        // and the chorus kicks pulse: 16 of the 18 the detector fires on (it misses 5 of the 23, as before and in round
        // 1; round 2 found all 23 with the beats it let fire inside the refractory time, BeatDetector). The kit is a
        // ballad's, under strings and voice: the kick band stays well under the music above it, so the groove is light
        // (BeatCharacter.Weight 0) and its pulses are LightGain-sized, 0.50 (round 2: full ones like rap, its review's
        // blocker). The punch (how drum-driven the music is) still rises with the drums (1.00 in the chorus) and fades
        // once they leave (at most 0.11 from 8 s after). The drive needs two kicks (the first one in a wave is no
        // groove yet), so the first pulse lands 3.3 s after the drums come in (round 1: 3.2 s; before 1.3 s, but a
        // single thump in a calm song was a full pulse too).
        StyleTrack track = DrumsEnter.Value;
        List<Frame> frames = Run(track.Samples);
        TrackSection verse = track.Section("verse"), chorus = track.Section("chorus"), verse2 = track.Section("verse2");
        Assert.InRange(Measure(frames, verse.Start + 1, verse.End).MaxPulse, 0, 0.15f);
        Assert.InRange(Measure(frames, verse2.Start, verse2.End).MaxPulse, 0, 0.15f);

        Frame first = frames.First(f => f.Light.Pulse >= 0.3f);
        output.WriteLine($"first pulse of 0.3 or more at {first.Time:F2} s (drums from {chorus.Start} s)");
        Assert.InRange(first.Time, chorus.Start, chorus.Start + 3.5);

        int detected = 0, pulsed = 0;
        var peaks = new List<float>();
        foreach (double kick in track.KickTimes)
        {
            var window = frames.Where(f => f.Time >= kick && f.Time <= kick + 0.1).ToList();
            if (!window.Any(f => f.Fired > 0)) continue;
            detected++;
            float peak = window.Max(f => f.Light.Pulse);
            if (kick >= first.Time) peaks.Add(peak);
            if (peak >= 0.25f) pulsed++;
        }
        output.WriteLine($"{pulsed}/{detected} detected kicks pulse 0.25 or more; from the first on, mean {peaks.Average():F2}, at most {peaks.Max():F2}");
        Assert.True(detected >= 15 && pulsed >= detected - 3, $"{pulsed}/{detected}");
        Assert.InRange(peaks.Average(), 0.3, BeatCharacter.LightGain + 0.05);
        Assert.InRange(peaks.Max(), 0, BeatCharacter.LightGain + 0.05f);

        double punchInChorus = frames.Where(f => f.Time >= chorus.Start + 4 && f.Time < chorus.End).Average(f => f.Punch);
        double punchAfter = frames.Where(f => f.Time >= verse2.Start + 8).Max(f => f.Punch);
        output.WriteLine($"punch in the chorus {punchInChorus:F2}, at most {punchAfter:F2} from 8 s after the drums stop");
        Assert.True(punchInChorus >= 0.8, $"{punchInChorus:F2}");
        Assert.True(punchAfter <= 0.3, $"{punchAfter:F2}");
    }

    [Theory]
    [InlineData(MusicStyle.CalmBallad, StyleParts.Voice)]
    [InlineData(MusicStyle.CalmBallad, StyleParts.Guitar)]
    [InlineData(MusicStyle.FolkBand, StyleParts.Guitar)]
    [InlineData(MusicStyle.PianoBallad, StyleParts.Piano)]
    public void VoicesStringsAndPianoAreNotKicks(MusicStyle style, StyleParts part)
    {
        // A baritone's syllables (fundamental 98–147 Hz), a guitar's low strings (with the weak fundamentals of the
        // Karplus–Strong guitar, or the strong ones of the additive one) and a piano's left-hand octaves all fill the
        // 30–150 Hz beat band, and the detector fires on each of them alone (at their songs' section levels). None stands
        // out of its own harmonics the way a kick does, so none pulses. Round 1 graded the voice and the Karplus–Strong
        // guitar 0 too, but the reviewers' additive guitar reached full pulses. (A bass guitar on its own is different:
        // with nothing else playing, its notes are the rhythm and stand out like kicks. Under a song it doesn't pulse:
        // ABassGuitarOrAPianoUnderACalmSongKeepsItAWave.)
        StyleTrack stem = StyleTracks.Render(style, parts: part);
        List<Frame> frames = Run(stem.Samples);
        int beats = frames.Sum(f => f.Fired);
        float maxPulse = frames.Max(f => f.Light.Pulse);
        output.WriteLine($"{style} {part} alone: {beats} beats, max pulse {maxPulse:F3}, max punch {frames.Max(f => f.Punch):F2}");
        Assert.True(beats >= 25, $"{beats}");
        Assert.InRange(maxPulse, 0, 0.15f);
    }

    [Fact]
    public void ABassGuitarOrAPianoUnderACalmSongKeepsItAWave()
    {
        // The reviewers' cases: the folk band's guitar and voice with a bass guitar 6 dB under them (round 1: 7–9
        // pulses of 0.5 or more per chorus, 12–17 % of the brightness above 3 Hz), and a piano ballad with left-hand
        // octaves on every beat of its choruses (round 1 on their version: 9–12 big pulses per chorus). 6ac553b: no
        // pulse, punch at most 0.03, at most 1.3 % above 3 Hz (unwindowed). Measured now: no pulse, punch 0, 0.00 %
        // above 3 Hz (windowed).
        foreach (var (name, track) in new[] { ("folk + bass", Folk(null)), ("piano ballad", StyleTracks.Render(MusicStyle.PianoBallad)) })
        {
            List<Frame> frames = Run(track.Samples);
            foreach (TrackSection s in track.Sections.Take(5))
            {
                Section m = Measure(frames, s.Start + (s.Start == 0 ? 3 : 1), s.End);
                double punch = frames.Where(f => f.Time >= s.Start + 1 && f.Time < s.End).Max(f => f.Punch);
                output.WriteLine($"{name} {s.Name}: max pulse {m.MaxPulse:F3}, punch up to {punch:F2}, above 3 Hz {m.HighShare:P2}");
                Assert.InRange(m.MaxPulse, 0, 0.15f);
                Assert.InRange(punch, 0, 0.3);
                Assert.InRange(m.HighShare, 0, 0.03);
            }
        }
    }

    [Fact]
    public void ASoftKitUnderAFolkBandBouncesSoftly()
    {
        // Round 1's reviewers' blocker: a brush kit 12 dB under the folk band bounced like rap. Round 1 graded each
        // kick on how far it stood out of the music above it, which cut real rap and EDM kicks too (round 2's corpus);
        // round 2 then let this kit pulse fully (0.99–1.00 in the choruses) and pump the light like rap, and its review
        // found the same on real folk and pop-rock songs with a kit (bounce depth 40–50 %, rap's 36–38 %): Maxwell's
        // Noah Kahan and Hozier case. No measure of the kick alone separates them (this kit's kicks peak at −6.6 dB
        // against the song's loud parts in the kick band, a rap recording's at −7.3 dB), but the music around them
        // does: here the kick band stays 14 dB under the music above it, where rap and EDM keep it level or above
        // (BeatCharacter.Weight). So the kit is the beat, at LightGain: its chorus kicks pulse (measured mean 0.54),
        // and the light barely pumps (the shown light rises 14 % on a kick on average, against the rap's 55 %). A kit
        // 20 dB under the band, which the ear barely picks out, doesn't make a groove: no pulse at all (round 1: one).
        StyleTrack soft = Folk(-12);
        List<Frame> frames = Run(soft.Samples);
        foreach (string name in new[] { "chorus1", "chorus2" })
        {
            TrackSection s = soft.Section(name);
            var (kicks, _, mean) = Kicks(frames, soft.KickTimes.Where(k => k >= s.Start + 1 && k < s.End).ToList(), 0);
            output.WriteLine($"-12 dB {name}: mean pulse {mean:F2} over {kicks} kicks");
            Assert.InRange(mean, 0.3, BeatCharacter.LightGain + 0.1);
        }
        double rap = Bounce(Run(Rap.Value.Samples), Rap.Value.KickTimes.Where(k => k >= 4).ToList());
        double kit = Bounce(frames, soft.KickTimes.Where(k => soft.Sections.Any(x => x.Name.StartsWith("chorus", StringComparison.Ordinal) && k >= x.Start + 1 && k < x.End)).ToList());
        output.WriteLine($"the shown light rises {kit:P0} on the soft kit's chorus kicks, {rap:P0} on the rap's");
        Assert.True(kit <= 0.5 * rap, $"{kit:P0} vs {rap:P0}");

        StyleTrack softer = Folk(-20);
        List<Frame> quiet = Run(softer.Samples);
        var big = new List<double>();
        for (int i = 1; i + 1 < quiet.Count; i++)
        {
            float pulse = quiet[i].Light.Pulse;
            if (quiet[i].Time >= 3 && pulse > 0.3f && pulse >= quiet[i - 1].Light.Pulse && pulse > quiet[i + 1].Light.Pulse) big.Add(quiet[i].Time);
        }
        output.WriteLine($"-20 dB: pulses over 0.3 at [{string.Join(", ", big.Select(t => t.ToString("F2")))}] s");
        Assert.Empty(big);
    }

    [Fact]
    public void AWhisperedVerseNeverBouncesBigEvenWithLoudDrums()
    {
        // The folk band's kit at the music's own level: full pulses on the chorus kicks, and in the whispered verse
        // after the chorus (15 dB under it) the kicks sit at the groove's floor, LoudFloor × punch, fading with how far
        // under the loud parts the moment is (measured 0.13 on average; round 2, before that fade: 0.30 on each). The
        // verse's loudest strums, where it swells to 11 dB under the chorus, reach 0.49; round 1 measured 0.35 at most
        // with SongLoudness.RangeDb 24, where those strums sat lower in the song. The verse is measured up to 0.25 s
        // before the second chorus: the chorus's pickup strum and bass at 71.9 s are the chorus arriving, at full
        // loudness, and pulse fully as round 1's chorus entrances did.
        StyleTrack band = Folk(0);
        List<Frame> frames = Run(band.Samples);
        TrackSection chorus = band.Section("chorus1"), verse2 = band.Section("verse2");
        var (kicks, strong, _) = Kicks(frames, band.KickTimes.Where(k => k >= chorus.Start + 1 && k < chorus.End).ToList(), 0);
        Section v = Measure(frames, verse2.Start + 1, verse2.End - 0.25);
        var (verseKicks, _, verseMean) = Kicks(frames, band.KickTimes.Where(k => k >= verse2.Start + 1 && k < verse2.End - 0.25).ToList(), 0);
        output.WriteLine($"chorus: {strong}/{kicks} kicks strong; verse2: {v.Pulses} pulses, max {v.MaxPulse:F3}, {verseKicks} kicks at {verseMean:F3} on average");
        Assert.True(strong >= 0.9 * kicks, $"{strong}/{kicks}");
        Assert.InRange(verseMean, 0, 0.35);
        Assert.InRange(v.MaxPulse, 0, 0.55f);
    }

    [Fact]
    public void TheOpeningOfTheNextSongIsJudgedAgainstTheLastOne()
    {
        // The folk band with its soft kit, played after itself and a 2 s gap: the session floor (the last song's loud
        // parts, falling 0.2 dB/s) keeps its whispered first verse quiet within the listening session, so its kicks,
        // which stand out of the sparse verse, bounce less than when it is the first song after launch (measured mean
        // pulse 0.45 against 0.70, at the light groove's gain; round 2 0.53 against 0.97, round 1 0.57 against 0.82). A
        // quieter master after a gap keeps its bounce: the same rap 10 dB down gives full pulses from 5 s in (all 25 of
        // its kicks to 20 s, mean 0.96).
        StyleTrack soft = Folk(-12);
        TrackSection verse1 = soft.Section("verse1");
        double first = Measure(Run(soft.Samples), verse1.Start + 1, verse1.End).MeanPulse;
        float[] twice = soft.Samples.Concat(new float[2 * Rate]).Concat(soft.Samples).ToArray();
        double offset = soft.Seconds + 2;
        double second = Measure(Run(twice), offset + verse1.Start + 1, offset + verse1.End).MeanPulse;
        output.WriteLine($"verse 1 mean pulse: first song {first:F3}, after the same song {second:F3}");
        Assert.True(second <= 0.8 * first, $"{second:F3} vs {first:F3}");

        float[] rap = Rap.Value.Samples;
        float[] quieter = rap.Concat(new float[3 * Rate]).Concat(rap.Select(x => x * 0.316f)).ToArray();
        List<Frame> frames = Run(quieter);
        double start = Rap.Value.Seconds + 3;
        var (kicks, strong, mean) = Kicks(frames, Rap.Value.KickTimes.Select(k => k + start).Where(k => k >= start + 5 && k < start + 20).ToList(), 0);
        output.WriteLine($"rap 10 dB quieter after a gap: {strong}/{kicks} kicks strong from 5 s to 20 s, mean {mean:F2}");
        Assert.True(strong >= 0.9 * kicks, $"{strong}/{kicks}");
    }

    [Fact]
    public void TheWholeSongAtAnotherVolumeLooksTheSame()
    {
        // Doc 03 §5's volume invariance through the whole chain: the same song at −20 dB and at −35 dB gives the same
        // light after 2 s. Every window above near-silence counts as music (a whisper at −35 dB volume dips under the
        // −60 dBFS IsSilent level between phrases; an early round 1 differed by 0.16 there). Measured now: 0.0000 at
        // −20 dB, 0.0003 at −35 dB.
        List<Frame> loud = BalladRun.Value;
        foreach (float gain in new[] { 0.1f, 0.0178f })
        {
            List<Frame> quiet = Run(Ballad.Value.Samples.Select(x => x * gain).ToArray());
            float worst = 0;
            for (int i = 0; i < loud.Count; i++)
                if (loud[i].Time >= 2) worst = MathF.Max(worst, MathF.Abs(loud[i].Light.Intensity - quiet[i].Light.Intensity) + MathF.Abs(loud[i].Light.Pulse - quiet[i].Light.Pulse));
            output.WriteLine($"{20 * MathF.Log10(gain):F0} dB: largest difference from 2 s: {worst:F4}");
            Assert.InRange(worst, 0, 0.02f);
        }
    }

    [Fact]
    public void AVolumeChangeMidSongSettlesAndKeepsTheGroove()
    {
        // A volume change shifts every loudness by the same dB, and within a second a song's own band balance moves by
        // more than that (SongLoudness), so the song reference has to turn over its memory before the light looks as it
        // did. Measured, |ΔIntensity| last above 0.1 after the step: three rap tracks in a row 24.1 s after −20 dB
        // (5b387fc: 5.2 s), at once after +20 dB (5b387fc: 15.3 s); two ballads in a row 23.1 s after −6 dB (5b387fc:
        // 0.7 s) and 15.0 s after +6 dB (5b387fc: 1.5 s). Meanwhile a groove keeps its kicks: in the 10 s after −10 dB
        // the rap's kicks average a pulse of 0.66. (Round 1 measured 0.73: with SongLoudness.RangeDb 24 instead of 18,
        // 10 dB moved the song position by 0.42 instead of 0.56, so the kicks looked less like a quiet passage. Round 2
        // narrowed the range for the calm songs' wave; the bound went from 0.6 to 0.5 with it.) After −20 dB round 2 kept
        // the groove's floor (0.26); measured now 0.03, 0.00, 0.61 and 1.00 over the kicks 0–10, 10–20, 20–30 and 30–40 s
        // after.
        foreach (var (song, copies, step, gainBefore, gainAfter, settle) in new[]
        {
            (Rap.Value, 3, 40.0, 1f, 0.1f, 30.0), (Rap.Value, 3, 40.0, 0.1f, 1f, 10.0),
            (Ballad.Value, 2, 100.0, 1f, 0.5f, 25.0), (Ballad.Value, 2, 100.0, 0.5f, 1f, 25.0),
        })
        {
            float[] x = Enumerable.Repeat(song.Samples, copies).SelectMany(v => v).ToArray();
            List<Frame> reference = Run(x);
            float[] stepped = x.Select((v, i) => v * (i < step * Rate ? gainBefore : gainAfter)).ToArray();
            List<Frame> frames = Run(stepped);
            double last = 0;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].Time >= step && MathF.Abs(frames[i].Light.Intensity - reference[i].Light.Intensity) > 0.1f) last = frames[i].Time - step;
            output.WriteLine($"{song.Style} {20 * MathF.Log10(gainAfter / gainBefore):+0;-0} dB: last difference above 0.1 at +{last:F1} s");
            Assert.InRange(last, 0, settle);
        }
        float[] three = Enumerable.Repeat(Rap.Value.Samples, 3).SelectMany(v => v).ToArray();
        var allKicks = Enumerable.Range(0, 3).SelectMany(c => Rap.Value.KickTimes.Select(k => k + c * Rap.Value.Seconds)).ToList();
        List<double> From(double start) => allKicks.Where(k => k >= start && k < start + 10).ToList();
        List<Frame> down10 = Run(three.Select((v, i) => i < 40 * Rate ? v : v * 0.316f).ToArray());
        var (kicks10, _, mean10) = Kicks(down10, From(40), 0);
        output.WriteLine($"rap -10 dB: mean pulse {mean10:F2} over the {kicks10} kicks of the next 10 s");
        Assert.True(mean10 >= 0.5, $"{mean10:F2}");
        // 20 dB down the song sits where a fade-out's last seconds do, and the groove's floor now fades out there too
        // (round 2's review: 0.3-high pulses 36 dB down a fade), so the bounce waits for the song reference to turn over.
        List<Frame> down20 = Run(three.Select((v, i) => i < 40 * Rate ? v : v * 0.1f).ToArray());
        double[] means = [Kicks(down20, From(40), 0).MeanPulse, Kicks(down20, From(50), 0).MeanPulse, Kicks(down20, From(60), 0).MeanPulse, Kicks(down20, From(70), 0).MeanPulse];
        output.WriteLine($"rap -20 dB: mean pulse over the kicks 0–10 s, 10–20 s, 20–30 s and 30–40 s after: {string.Join(", ", means.Select(m => m.ToString("F2")))}");
        Assert.InRange(means[0], 0, 0.15);
        Assert.True(means[3] >= 0.8, $"{means[3]:F2}");
    }

    [Fact]
    public void ASmallVolumeNudgeKeepsTheBounceAndSettles()
    {
        // Round 1 kept the top fifth of the song position at full height (AudioAnalyzer.LevelKnee), so a 4 dB nudge left
        // the rap's light within 0.05 of what it was. Round 2 dropped the knee: with it a calm song's Intensity stayed
        // within 0.57–0.74 (Brightness 0.8) for 80 % of the song in round 2's corpus, the static light Maxwell saw. A
        // nudge now moves the light like the music getting 4 dB quieter (the two can't be told apart, SongLoudness) until
        // the song reference has turned over: measured, the largest Intensity difference is 0.115 and it stays under 0.05
        // from 22.7 s after the step. The bounce doesn't care: the kicks of the next 10 s keep a mean pulse of 1.00 (full
        // size reaches 8 dB under the song's loud parts).
        float[] three = Enumerable.Repeat(Rap.Value.Samples, 3).SelectMany(v => v).ToArray();
        List<Frame> reference = Run(three);
        List<Frame> frames = Run(three.Select((v, i) => i < 40 * Rate ? v : v * 0.631f).ToArray());
        float worst = 0;
        double settled = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].Time < 40) continue;
            float difference = MathF.Abs(frames[i].Light.Intensity - reference[i].Light.Intensity);
            worst = MathF.Max(worst, difference);
            if (difference > 0.05f) settled = frames[i].Time - 40;
        }
        var kickTimes = Enumerable.Range(0, 3).SelectMany(c => Rap.Value.KickTimes.Select(k => k + c * Rap.Value.Seconds)).Where(k => k >= 40 && k < 50).ToList();
        var (kicks, _, mean) = Kicks(frames, kickTimes, 0);
        output.WriteLine($"−4 dB: largest Intensity difference {worst:F3}, under 0.05 from +{settled:F1} s; mean pulse {mean:F2} over the {kicks} kicks of the next 10 s");
        Assert.InRange(worst, 0, 0.13f);
        Assert.InRange(settled, 0, 30);
        Assert.True(mean >= 0.9, $"{mean:F2}");
    }

    [Theory]
    [InlineData(20f, 200f)]
    [InlineData(30f, 250f)]
    [InlineData(30f, 100f)]
    public void LiveTuningTheBeatBandDoesNotChangeTheGrading(float bassMin, float bassMax)
    {
        // The debug visualizer's Bass band sliders move the detector's band; the grading uses fixed internal bands
        // (BandAnalyzer.KickBins). Round 1 graded on the tunable band: 30–250 Hz took in the baritone's harmonics and the
        // ballad pulsed at 1.0 again, and 30–100 Hz left 48 of 61 rap kicks strong. Measured now: no ballad pulse and
        // every rap kick strong at each tuning.
        var tuning = new AudioTuning { BassMinHz = bassMin, BassMaxHz = bassMax };
        float ballad = Run(Ballad.Value.Samples, tuning: tuning).Where(f => f.Time >= 3).Max(f => f.Light.Pulse);
        var (kicks, strong, _) = Kicks(Run(Rap.Value.Samples, tuning: tuning), Rap.Value.KickTimes, 4);
        output.WriteLine($"{bassMin}–{bassMax} Hz: ballad max pulse {ballad:F3}, rap {strong}/{kicks} strong");
        Assert.InRange(ballad, 0, 0.15f);
        Assert.True(strong >= 0.9 * kicks, $"{strong}/{kicks}");
    }

    [Fact]
    public void SensitivityScalesThePulse()
    {
        // The Sensitivity setting still scales everything: the beat threshold and Level (doc 03 §2) and now each pulse.
        // Measured now: a mean pulse of 0.50 at 0.5 and 1.00 at 2.
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
        // The wave, the song reference, the grading and the tempo run on the render thread: 0 bytes per frame, through
        // drumless verses, choruses and the punch fading in and out.
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

    [Theory]
    [InlineData(-20f)]
    [InlineData(-30f)]
    public void AnOceanRecordingNeverBuildsAGroove(float dbfs)
    {
        // Round 2's review: brown noise swelling every 8 s like waves on a shore fired 71 beats and 43 full pulses a
        // minute, with a full groove pumping the light down to a mean Intensity of 0.52. BeatDetector's looser guard now
        // needs music (OnsetContrast), and BeatCharacter trusts only beats that passed doc 03's own threshold on anything
        // that isn't: the swells' largest rises still fire a beat now and then, as they did in round 1 (measured 2 a
        // minute), but no groove builds and nothing pulses.
        float[] ocean = SyntheticAudio.BrownNoise(Rate, 60, MathF.Pow(10, dbfs / 20), 20, swellSeconds: 8);
        List<Frame> frames = Run(ocean);
        output.WriteLine($"{dbfs} dBFS: {frames.Sum(f => f.Fired)} beats, max pulse {frames.Max(f => f.Light.Pulse):F3}, max punch {frames.Max(f => f.Punch):F2}");
        Assert.InRange(frames.Max(f => f.Light.Pulse), 0, 0.05f);
        Assert.InRange(frames.Max(f => f.Punch), 0, 0.05f);
    }

    [Fact]
    public void TheDropAfterTheBreakdownHitsOnItsFirstKick()
    {
        // Round 2's review: a real EDM drop's first three kicks graded 0.01 (the punch had faded over the breakdown), then
        // the groove came back at once without a pulse, and Level's pump cut the light from 0.72 to 0.55 for a second.
        // The groove's memory now lasts the breakdown, and the drop's first kick brings it back (BeatCharacter.Resume):
        // here a frame after its beat fired, once KickEnvelope has seen its attack rise over the pads arriving with it
        // (round 2 measured 0.06 on it, 0.06 on the bass note after it, then the light fell from 0.67 to 0.55 with no
        // pulse). Measured now: 1.00 on the first kick, the off-beat bass note after it masked (graded 0.03), and the pump
        // only dips the light once the groove has pulsed.
        StyleTrack edm = Edm.Value;
        List<Frame> frames = Run(edm.Samples);
        TrackSection drop = edm.Section("drop2");
        double kick = edm.KickTimes.First(k => k >= drop.Start);
        float pulse = frames.Where(f => f.Time >= kick && f.Time <= kick + 0.1).Max(f => f.Light.Pulse);
        Frame before = frames.Last(f => f.Time <= drop.Start - 0.1);
        Frame firstPulse = frames.First(f => f.Time >= kick && f.Light.Pulse >= 0.5f);
        float lowest = frames.Where(f => f.Time >= before.Time && f.Time <= firstPulse.Time).Min(f => f.Light.Intensity);
        int at = frames.IndexOf(firstPulse);
        float offBeat = 0;                                          // the largest rise of the pulse until the next kick
        for (int i = at + 1; i < frames.Count && frames[i].Time < kick + 0.4; i++)
            offBeat = MathF.Max(offBeat, frames[i].Light.Pulse - frames[i - 1].Light.Pulse);
        output.WriteLine($"first kick of the drop at {kick:F2} s: pulse {pulse:F2}; Intensity {before.Light.Intensity:F3} before it, at least {lowest:F3} until its pulse; the pulse rises at most {offBeat:F2} on the off-beat after it");
        Assert.True(pulse >= 0.9f, $"{pulse:F2}");
        Assert.True(lowest >= before.Light.Intensity - 0.03f, $"{lowest:F3}");
        Assert.InRange(offBeat, 0, 0.1f);
    }

    // ---- the overlay's loop, offline ----

    internal readonly record struct Frame(double Time, AudioFeatures Audio, LightState Light, int Fired, float Punch, float Bpm);

    private readonly record struct Section(int Beats, int Pulses, float MaxPulse, double MeanPulse, double Intensity, double HighShare, double SlopeP95);

    internal static List<Frame> Run(float[] samples, float sensitivity = 1, int rate = Rate, AudioTuning? tuning = null)
    {
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer((tuning ?? new AudioTuning()) with { Sensitivity = sensitivity });
        ILightEngine engine = CoreFactory.CreateLightEngine();
        // The Pulse movement: the whole-edge pulse is the analyzer's graded beat, which these tests measure (in Flow the
        // ripples carry 60 % of it along the edge instead).
        var settings = new Settings { Sensitivity = sensitivity, Movement = MovementStyle.Pulse };
        var character = ((AudioAnalyzer)analyzer).Character;
        int packet = rate / 100;
        double time = 0, end = samples.Length / (double)rate;
        int position = 0, beats = 0;
        var frames = new List<Frame>((int)(end * 60) + 2);
        while (time < end)
        {
            time += 1.0 / 60;
            long arrived = Math.Min(samples.Length, (long)(time * rate) / packet * packet);
            int count = (int)Math.Max(0, arrived - position);
            AudioFeatures audio = analyzer.Process(samples.AsSpan(position, count), rate, 1f / 60);
            position += count;
            LightState light = engine.Update(1f / 60, audio, Palette.Default, settings, false);
            int fired = analyzer.Diagnostics.BeatCount - beats;
            beats = analyzer.Diagnostics.BeatCount;
            frames.Add(new Frame(time, audio, light, fired, character.Punch, analyzer.Diagnostics.EstimatedBpm));
        }
        return frames;
    }

    // Beats, the pulses (local peaks of Pulse above 0.05) and the largest, and the brightness the shader shows
    // (Intensity × (1 + 0.25 Pulse), Glow.hlsl): its mean, the share of its variance above 3 Hz, and the 95th percentile
    // of its slope per second. The share comes from a Hann-windowed DFT of the section, as the corpus metrics' Welch
    // estimate does: a rectangular one treats the section as a loop, so a light that ends the section higher than it
    // started (the wave rising into a chorus) shows a jump at the seam whose spectrum reaches far above 3 Hz (round 2:
    // 3 % "above 3 Hz" on a section whose steepest frame-to-frame step was 0.003).
    private static Section Measure(List<Frame> frames, double from, double to)
    {
        var f = frames.Where(x => x.Time >= from && x.Time < to).ToList();
        var peaks = new List<float>();
        for (int i = 1; i + 1 < f.Count; i++)
            if (f[i].Light.Pulse > 0.05f && f[i].Light.Pulse >= f[i - 1].Light.Pulse && f[i].Light.Pulse > f[i + 1].Light.Pulse) peaks.Add(f[i].Light.Pulse);
        double[] shown = f.Select(x => (double)x.Light.Intensity * (1 + 0.25 * x.Light.Pulse)).ToArray();
        double mean = shown.Average(), total = 0, high = 0;
        int n = shown.Length;
        double[] windowed = shown.Select((v, i) => (v - mean) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)))).ToArray();
        for (int k = 1; k <= n / 2; k++)
        {
            double re = 0, im = 0, w = 2 * Math.PI * k / n;
            for (int i = 0; i < n; i++)
            {
                re += windowed[i] * Math.Cos(w * i);
                im -= windowed[i] * Math.Sin(w * i);
            }
            total += re * re + im * im;
            if (k * 60.0 / n > 3) high += re * re + im * im;
        }
        double[] slopes = new double[n - 1];
        for (int i = 1; i < n; i++) slopes[i - 1] = Math.Abs(shown[i] - shown[i - 1]) * 60;
        Array.Sort(slopes);
        return new Section(f.Sum(x => x.Fired), peaks.Count, f.Max(x => x.Light.Pulse), peaks.Count > 0 ? peaks.Average() : 0,
            f.Average(x => x.Light.Intensity), total > 0 ? high / total : 0, slopes[(int)(0.95 * (slopes.Length - 1))]);
    }

    // How much brighter the light the shader shows (Intensity × (1 + 0.25 Pulse)) gets on these kicks, on average: from
    // 30 ms before each one to its peak within 150 ms (AGroovePumpsTheLightBetweenKicks, the corpus metrics' bounce).
    private static double Bounce(List<Frame> frames, IReadOnlyList<double> kicks)
    {
        double Shown(Frame f) => f.Light.Intensity * (1 + 0.25 * f.Light.Pulse);
        var depths = new List<double>();
        foreach (double kick in kicks.Where(k => k - 0.03 >= frames[0].Time && k + 0.15 < frames[^1].Time))
        {
            Frame before = frames.Last(f => f.Time <= kick - 0.03);
            double peak = frames.Where(f => f.Time >= kick - 0.02 && f.Time <= kick + 0.15).Max(Shown);
            depths.Add(peak / Shown(before) - 1);
        }
        return depths.Average();
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
