using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 2: the beat detector's internal exception to doc 03's rule (the looser median guard with
// time-domain evidence of a kick, in music only), the onset contrast that tells music from a steady noise, and the
// contract's minimum interval between beats. Fed directly with spectra whose bass band moves as one, at 60 steps a
// second: a step's flux is how far the band's total magnitude rose (flux units). The background repeats rises of B, B
// and 0, so its median flux is B, its mean 2B/3 and mean + 1.5σ about 1.4B: the threshold is the median guard, 10 × B,
// or 5 × B with evidence of a kick in music. Above the bass band plays either music (a chord on every third step, held
// over the next two: its relative flux jumps) or a steady noise (half of the bins trade places with the other half on
// every step: the same relative flux every step).
public sealed class BeatDetectorTests
{
    private const int Window = 2048;
    private const int Rate = 48000;
    private const float Step = 1f / 60;
    private const float B = 0.01f;
    private static readonly AudioTuning Defaults = new();

    private sealed class Feed
    {
        private readonly float[] magnitudes = new float[Window / 2 + 1];
        private readonly int start, end, upperStart, upperEnd;
        private readonly AudioTuning tuning;
        private readonly bool music;
        private float level;
        private int phase, step;

        public Feed(bool music = true, AudioTuning? tuning = null)
        {
            this.music = music;
            this.tuning = tuning ?? Defaults;
            (start, end) = BandAnalyzer.BassBins(magnitudes.Length, (float)Rate / Window, this.tuning);
            (upperStart, upperEnd) = BandAnalyzer.UpperBins(magnitudes.Length, (float)Rate / Window);
            Detector = new BeatDetector(magnitudes.Length);
            for (int i = 0; i < 1200; i++) Background();        // 20 s: the history and the onset contrast settle
        }

        public BeatDetector Detector { get; }

        // One step whose bass band rises by `flux` (or falls to `fallTo`), with KickEnvelope's evidence. partlySilent and
        // upperScale: the window is partly near-silent, with the music above the bass band at that scale.
        public BeatResult Next(float flux, float evidenceDb = float.NegativeInfinity, float? fallTo = null, bool partlySilent = false, float upperScale = 1)
        {
            level = fallTo ?? level + flux;
            for (int k = start; k < end; k++) magnitudes[k] = level / (end - start) * Window / 4;
            step++;
            for (int k = upperStart; k < upperEnd; k++)
                magnitudes[k] = upperScale * (music ? (step % 3 == 0 ? 1.5f : 1f) : ((k + step) % 2 == 0 ? 1.2f : 1f));
            return Detector.Update(magnitudes, true, Window, Rate, Step, tuning, 0.1f, evidenceDb, partlySilent);
        }

        // The background: rises of B, B, then back down.
        public BeatResult Background()
        {
            phase = (phase + 1) % 3;
            return phase == 0 ? Next(0, fallTo: 0) : Next(B);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBackgroundAloneNeverFires(bool music)
    {
        var feed = new Feed(music);
        for (int i = 0; i < 600; i++) Assert.False(feed.Background().IsBeat);
    }

    [Fact]
    public void OnsetContrastTellsMusicFromASteadyNoise()
    {
        // The coefficient of variation of the upper band's relative flux: the chord's steps against the held ones
        // (√2 for one rise in three), and none at all for a noise whose every step changes it by the same share.
        var music = new Feed(music: true).Detector;
        var noise = new Feed(music: false).Detector;
        Assert.InRange(music.OnsetContrast, 1.3f, 1.5f);
        Assert.True(music.IsMusic);
        Assert.InRange(noise.OnsetContrast, 0, 0.01f);
        Assert.False(noise.IsMusic);
    }

    [Fact]
    public void OnsetContrastIgnoresTheMusicStartingOrStopping()
    {
        // A window that is partly near-silent (AudioFrontEnd.WindowPartlySilent), and the step after it, hold the
        // contrast: the music filling the window after a gap is no onset of the music itself.
        var feed = new Feed(music: false);
        feed.Next(0, fallTo: 0, partlySilent: true, upperScale: 0.01f);
        feed.Next(0, fallTo: 0, upperScale: 1);
        for (int i = 0; i < 60; i++) feed.Background();
        Assert.False(feed.Detector.IsMusic);
        Assert.InRange(feed.Detector.OnsetContrast, 0, 0.01f);
    }

    [Theory]
    [InlineData(7f, float.NegativeInfinity, false)]   // over the loose guard, no evidence: the median guard holds it
    [InlineData(7f, -5f, false)]                      // too little evidence
    [InlineData(7f, -4f, true)]                       // BeatDetector.KickEvidenceDb
    [InlineData(4f, 20f, false)]                      // under the loose guard, whatever the evidence
    [InlineData(11f, float.NegativeInfinity, true)]   // over doc 03's median guard: no evidence needed
    public void InMusicEvidenceOfAKickLowersTheMedianGuard(float fluxOverB, float evidenceDb, bool fires)
    {
        var feed = new Feed(music: true);
        BeatResult result = feed.Next(fluxOverB * B, evidenceDb);
        Assert.Equal(fires, result.IsBeat);
        // The threshold the step was held to (ThresholdHistory, the visualizer's line), and whether the flux also passed
        // doc 03's own.
        bool loose = evidenceDb >= BeatDetector.KickEvidenceDb;
        Assert.Equal((loose ? BeatDetector.LooseOnsetRatio : BeatDetector.MedianOnsetRatio) * B, result.Threshold, 5);
        Assert.Equal(fluxOverB > BeatDetector.MedianOnsetRatio, result.Strict);
    }

    [Theory]
    [InlineData(7f, -4f, false)]                      // a random swell of brown noise's kick band: no looser guard
    [InlineData(7f, 20f, false)]
    [InlineData(11f, float.NegativeInfinity, true)]   // doc 03's own rule still holds
    public void OnASteadyNoiseTheEvidenceLowersNothing(float fluxOverB, float evidenceDb, bool fires)
    {
        // Round 2's review: in brown noise or an ocean recording the kick band carries most of the power, and its own
        // swings passed KickEvidenceDb on 96 % of the steps.
        var feed = new Feed(music: false);
        BeatResult result = feed.Next(fluxOverB * B, evidenceDb);
        Assert.Equal(fires, result.IsBeat);
        Assert.Equal(BeatDetector.MedianOnsetRatio * B, result.Threshold, 5);
    }

    [Theory]
    [InlineData(0.18f)]                               // the default Beat spacing
    [InlineData(0.4f)]                                // tuned up: round 2 still fired beats 51 ms apart
    public void NoTwoBeatsComeCloserThanTheBeatSpacing(float spacing)
    {
        // AudioTuning.BeatRefractorySeconds is the contract's minimum interval between beats. Round 2 let a beat fire
        // again inside it (a double kick 90 ms after the first, or a kick 50 ms after a weaker onset); on the corpus
        // most of those were a second flash on one hit. Kicks with evidence every 0.1 s: a beat on every kick the
        // spacing allows.
        var feed = new Feed(tuning: Defaults with { BeatRefractorySeconds = spacing });
        var beats = new List<int>();
        for (int i = 0; i < 360; i++)
            if ((i % 6 == 0 ? feed.Next(20 * B, 10) : feed.Background()).IsBeat) beats.Add(i);
        int expected = (int)(6 / (MathF.Ceiling(spacing / 0.1f - 1e-4f) * 0.1f));
        Assert.InRange(beats.Count, expected - 1, expected + 1);
        for (int i = 1; i < beats.Count; i++) Assert.True((beats[i] - beats[i - 1]) * Step >= spacing - 1e-4f, $"{(beats[i] - beats[i - 1]) * Step:F3} s");
    }
}
