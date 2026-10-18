using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 2: the beat detector's internal exceptions to doc 03's rule (BeatDetector: the looser median
// guard with time-domain evidence of a kick, and the two ways a beat may fire again inside the refractory time). Fed
// directly with spectra whose bass band moves as one, at 60 steps a second: a step's flux is how far the band's total
// magnitude rose (flux units). The background repeats rises of B, B and 0, so its median flux is B, its mean 2B/3 and
// mean + 1.5σ about 1.4B: the threshold is the median guard, 10 × B, or 5 × B with evidence of a kick. White noise never
// shows that evidence (KickEnvelopeTests) and still never fires (AudioAnalyzerTests).
public sealed class BeatDetectorTests
{
    private const int Window = 2048;
    private const int Rate = 48000;
    private const float Step = 1f / 60;
    private const float B = 0.01f;
    private static readonly AudioTuning Tuning = new();

    private sealed class Feed
    {
        private readonly float[] magnitudes = new float[Window / 2 + 1];
        private readonly int start, end;
        private float level;
        private int phase;

        public Feed()
        {
            (start, end) = BandAnalyzer.BassBins(magnitudes.Length, (float)Rate / Window, Tuning);
            Detector = new BeatDetector(magnitudes.Length);
            for (int i = 0; i < 90; i++) Background();          // 1.5 s: the history covers FluxHistorySeconds
        }

        public BeatDetector Detector { get; }

        // One step whose bass band rises by `flux` (or falls to `fallTo`), with KickEnvelope's evidence and the
        // fresh-attack flag AudioAnalyzer passes.
        public bool Next(float flux, float evidenceDb = float.NegativeInfinity, bool fresh = false, float? fallTo = null)
        {
            level = fallTo ?? level + flux;
            for (int k = start; k < end; k++) magnitudes[k] = level / (end - start) * Window / 4;
            return Detector.Update(magnitudes, true, Window, Rate, Step, Tuning, 0.1f, evidenceDb, fresh).IsBeat;
        }

        // The background: rises of B, B, then back down.
        public bool Background()
        {
            phase = (phase + 1) % 3;
            return phase == 0 ? Next(0, fallTo: 0) : Next(B);
        }

        // A drop back to the background level: no flux, the dip a new attack needs.
        public bool Dip() => Next(0, fallTo: 0);
    }

    [Fact]
    public void TheBackgroundAloneNeverFires()
    {
        var feed = new Feed();
        for (int i = 0; i < 600; i++) Assert.False(feed.Background());
    }

    [Theory]
    [InlineData(7f, float.NegativeInfinity, false)]   // over the loose guard, no evidence: the median guard holds it
    [InlineData(7f, -5f, false)]                      // too little evidence
    [InlineData(7f, -4f, true)]                       // BeatDetector.KickEvidenceDb
    [InlineData(4f, 20f, false)]                      // under the loose guard, whatever the evidence
    [InlineData(11f, float.NegativeInfinity, true)]   // over doc 03's median guard: no evidence needed
    public void EvidenceOfAKickLowersTheMedianGuard(float fluxOverB, float evidenceDb, bool fires)
    {
        var feed = new Feed();
        Assert.Equal(fires, feed.Next(fluxOverB * B, evidenceDb));
    }

    // A detector that has just fired on a kick (20 B of flux), then dipped back down for `dips` steps.
    private static Feed AfterAKick(int dips)
    {
        var feed = new Feed();
        Assert.True(feed.Next(20 * B, 10));
        for (int i = 0; i < dips; i++) feed.Dip();
        return feed;
    }

    [Fact]
    public void ADoubleKickFiresAgainInsideTheRefractoryTime()
    {
        // 100 ms after the first (RetriggerSeconds 0.09 < 0.1 < BeatRefractorySeconds 0.18): a new attack, after the kick
        // band fell to half (fresh), with RetriggerEvidenceDb of evidence.
        Assert.True(AfterAKick(5).Next(7 * B, 3, fresh: true));
        Assert.False(AfterAKick(5).Next(7 * B, 3, fresh: false));     // the first kick still ringing
        Assert.False(AfterAKick(5).Next(7 * B, 2, fresh: true));      // not enough evidence
        Assert.False(AfterAKick(4).Next(7 * B, 3, fresh: true));      // 83 ms: too soon
        Assert.False(AfterAKick(5).Next(4 * B, 20, fresh: true));     // under the loose guard
    }

    [Fact]
    public void AKickRightAfterAWeakerOnsetReplacesIt()
    {
        // A weaker onset fires (12 B, over the median guard), the flux dips under SupersedeDip of it, and 67 ms later the
        // kick comes with SupersedeRatio of its flux or more: it fires too, so the pulse lands on the kick.
        Feed Weaker()
        {
            var feed = new Feed();
            Assert.True(feed.Next(12 * B));
            return feed;
        }
        var replaced = Weaker();
        for (int i = 0; i < 3; i++) Assert.False(replaced.Dip());
        Assert.True(replaced.Next(16 * B));

        var smaller = Weaker();
        for (int i = 0; i < 3; i++) smaller.Dip();
        Assert.False(smaller.Next(15 * B));                           // under 1.3 × 12 B

        var noDip = Weaker();                                         // the onset kept rising: the same event
        for (int i = 0; i < 3; i++) Assert.False(noDip.Next(7 * B));
        Assert.False(noDip.Next(16 * B));

        var tooSoon = Weaker();
        tooSoon.Dip();
        Assert.False(tooSoon.Next(16 * B));                           // 33 ms: under SupersedeSeconds
    }
}
