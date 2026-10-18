namespace Rimlight.Core.Audio;

// What the newest audio says about a kick, at the step a beat fires (Maxwell's feedback, round 2: rap and EDM bounces
// were late and missed kicks). The FFT window is 2048 samples under a Hann window, centred about 21 ms in the past, so
// the step that fires a beat holds only the start of the kick: graded from the spectrum alone, the pulse either waits a
// frame or two for the kick to fill the window (round 1: it peaked 46–53 ms after the kick on the corpus of real music)
// or guesses. This follows the kick band in the time domain instead, sample by sample at the analysis rate, so by the
// time the flux fires it has already seen the kick's attack (a kick's 30–100 Hz energy peaks 17–22 ms after its onset;
// the beat fires 21–29 ms after it):
//   the kick band   a 4th-order Butterworth low-pass at KickHighHz and a 2nd-order high-pass at KickLowHz (BandAnalyzer
//                   .KickBins' band), its power smoothed over EnvelopeSeconds;
//   the music above a 2nd-order high-pass at UpperHz, its power smoothed over UpperSeconds (BeatCharacter's FastSeconds).
// Peak is the kick band's largest envelope over the newest PeakSeconds and Base its smallest over the PeakSeconds to
// BaseSeconds before that, both from 1 ms blocks. Evidence turns them into decibels: how far the kick band rose above its
// level just before (Rise) and how large that rise is next to the music above the bass band (Prominence). Both are
// ratios, so they don't depend on the playback volume. Measured on the corpus at the step a beat fires (kicks against the
// other bass-band onsets the detector fires on): Prominence separates the kicks of rap, EDM and folk kits (AUC 0.66–0.93
// per class), where the FFT kick band at the same step reads 0.62–0.84. Filters run in double precision (the poles of a
// 30 Hz high-pass at 48 kHz sit 0.004 from the unit circle) and flush to zero once nothing is left (no subnormal
// numbers in long silences). About 25 multiply-adds a sample; all storage is allocated up front.
internal sealed class KickEnvelope
{
    internal const float KickLowHz = 30f;
    internal const float KickHighHz = 100f;
    internal const float UpperHz = 150f;
    internal const float EnvelopeSeconds = 0.004f;
    internal const float UpperSeconds = 0.25f;
    internal const float PeakSeconds = 0.03f;
    internal const float BaseSeconds = 0.08f;
    private const int BlocksPerSecond = 1000;
    private const int MaxBlocks = 96;              // a ring of 1 ms blocks, more than BaseSeconds
    private const double Flush = 1e-25;            // filter states and powers under this are 0
    private const float FloorPower = 1e-24f;       // 10^-12 in amplitude: Evidence's floor, far under any audible kick

    private readonly float[] blockMax = new float[MaxBlocks];
    private readonly float[] blockMin = new float[MaxBlocks];
    private Biquad low1, low2, high, upper;
    private double subPower, upperPower, subKeep, upperKeep;
    private int blockLength, blockFill, head, stored;
    private float partialMax, partialMin = float.MaxValue;

    public KickEnvelope(int sampleRate = 48000) => Configure(sampleRate);

    // Sets the filters for a new analysis rate and clears everything.
    public void Configure(int sampleRate)
    {
        // Q of the two sections of a 4th-order Butterworth low-pass, and of a 2nd-order one.
        low1 = Biquad.LowPass(KickHighHz, 0.5411961, sampleRate);
        low2 = Biquad.LowPass(KickHighHz, 1.3065630, sampleRate);
        high = Biquad.HighPass(KickLowHz, Math.Sqrt(0.5), sampleRate);
        upper = Biquad.HighPass(UpperHz, Math.Sqrt(0.5), sampleRate);
        subKeep = Math.Exp(-1.0 / (EnvelopeSeconds * sampleRate));
        upperKeep = Math.Exp(-1.0 / (UpperSeconds * sampleRate));
        blockLength = Math.Max(1, (int)MathF.Round(sampleRate / (float)BlocksPerSecond));
        Reset();
    }

    // The newest samples (analysis rate), in order. Non-finite samples were sanitized before (SpectrumAnalyzer's rule);
    // here they count as 0.
    public void Process(ReadOnlySpan<float> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            double x = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -16f, 16f) : 0;
            double sub = high.Next(low2.Next(low1.Next(x)));
            double up = upper.Next(x);
            subPower = sub * sub + (subPower - sub * sub) * subKeep;
            upperPower = up * up + (upperPower - up * up) * upperKeep;
            float p = (float)subPower;
            if (p > partialMax) partialMax = p;
            if (p < partialMin) partialMin = p;
            if (++blockFill == blockLength) Push();
        }
        low1.Flush(Flush);
        low2.Flush(Flush);
        high.Flush(Flush);
        upper.Flush(Flush);
        if (subPower < Flush) subPower = 0;
        if (upperPower < Flush) upperPower = 0;
    }

    // The kick band's amplitude now (the largest over the newest PeakSeconds) and its smallest before that.
    public float Peak => MathF.Sqrt(MathF.Max(Largest(0, PeakBlocks), FloorPower));

    public float Base => MathF.Sqrt(MathF.Max(Smallest(PeakBlocks, BaseBlocks), FloorPower));

    // The amplitude of the music above the bass band (UpperSeconds).
    public float Upper => MathF.Sqrt(MathF.Max((float)upperPower, FloorPower));

    // The evidence for a kick at this step, in dB (BeatCharacter, BeatDetector).
    public KickEvidence Evidence
    {
        get
        {
            float peak = Peak, floor = Base;
            float rise = MathF.Max(peak - floor, 1e-12f);
            return new KickEvidence(20 * MathF.Log10(rise / Upper), 20 * MathF.Log10(peak / floor), peak);
        }
    }

    public void Reset()
    {
        low1.Clear();
        low2.Clear();
        high.Clear();
        upper.Clear();
        subPower = upperPower = 0;
        Array.Clear(blockMax);
        Array.Clear(blockMin);
        head = stored = blockFill = 0;
        partialMax = 0;
        partialMin = float.MaxValue;
    }

    private const int PeakBlocks = (int)(PeakSeconds * BlocksPerSecond + 0.5f);
    private const int BaseBlocks = (int)(BaseSeconds * BlocksPerSecond + 0.5f);

    private void Push()
    {
        blockMax[head] = partialMax;
        blockMin[head] = partialMin;
        head = (head + 1) % MaxBlocks;
        stored = Math.Min(stored + 1, MaxBlocks);
        blockFill = 0;
        partialMax = 0;
        partialMin = float.MaxValue;
    }

    // The largest power over blocks [from, to) back from the newest, the unfinished block counting as the newest.
    private float Largest(int from, int to)
    {
        float max = blockFill > 0 ? partialMax : 0;
        for (int i = from; i < Math.Min(to, stored); i++) max = MathF.Max(max, blockMax[(head - 1 - i + MaxBlocks) % MaxBlocks]);
        return max;
    }

    // The smallest power over blocks [from, to) back; with no block that old yet, the oldest there is (the start of the
    // music is its own base).
    private float Smallest(int from, int to)
    {
        if (stored == 0) return blockFill > 0 ? partialMin : 0;
        int first = Math.Min(from, stored - 1);
        float min = float.MaxValue;
        for (int i = first; i < Math.Min(to, stored); i++) min = MathF.Min(min, blockMin[(head - 1 - i + MaxBlocks) % MaxBlocks]);
        return min;
    }

    // A biquad in transposed direct form II (RBJ's audio-EQ cookbook), in double precision.
    private struct Biquad
    {
        private double b0, b1, b2, a1, a2, z1, z2;

        public static Biquad LowPass(double hz, double q, int sampleRate)
        {
            (double cos, double alpha) = Shape(hz, q, sampleRate);
            double a0 = 1 + alpha;
            return new Biquad { b0 = (1 - cos) / 2 / a0, b1 = (1 - cos) / a0, b2 = (1 - cos) / 2 / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0 };
        }

        public static Biquad HighPass(double hz, double q, int sampleRate)
        {
            (double cos, double alpha) = Shape(hz, q, sampleRate);
            double a0 = 1 + alpha;
            return new Biquad { b0 = (1 + cos) / 2 / a0, b1 = -(1 + cos) / a0, b2 = (1 + cos) / 2 / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0 };
        }

        // A cutoff at or above Nyquist (a decimated low rate, never in practice) is held just under it.
        private static (double Cos, double Alpha) Shape(double hz, double q, int sampleRate)
        {
            double w = 2 * Math.PI * Math.Min(hz, 0.45 * sampleRate) / sampleRate;
            return (Math.Cos(w), Math.Sin(w) / (2 * q));
        }

        public double Next(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }

        public void Flush(double tiny)
        {
            if (Math.Abs(z1) < tiny) z1 = 0;
            if (Math.Abs(z2) < tiny) z2 = 0;
        }

        public void Clear() => z1 = z2 = 0;
    }
}

// KickEnvelope's reading at one analysis step. ProminenceDb: the kick band's rise over the music above the bass band.
// RiseDb: over its own level just before. SubPeak: the kick band's amplitude now (BeatCharacter's pulse size).
internal readonly record struct KickEvidence(float ProminenceDb, float RiseDb, float SubPeak)
{
    // No evidence of anything (a property, not a static field: Core keeps no static state, InstanceIndependenceTests).
    public static KickEvidence None => new(float.NegativeInfinity, 0, 0);
}
