namespace Rimlight.Core.Audio;

// Integer-factor decimation for high-rate capture devices (88.2–384 kHz). With N fixed at 2048 the bass band
// would shrink to 1–3 bins at 96/192 kHz, which makes its flux noisy enough to fire false beats on steady noise.
// Decimating by D keeps the analysis rate in 44.1–75 kHz (≈ 21–37 Hz per bin) and the CPU cost constant.
// Anti-aliasing: Blackman-windowed sinc, cutoff 0.5625 × output rate (27 kHz at 48 kHz out), so content that
// would fold into the analyzed 0–12 kHz range is attenuated by > 50 dB. All taps are built at construction.
internal sealed class Decimator
{
    private const int MaxFactor = 8;
    private readonly float[][] taps = new float[MaxFactor + 1][];
    private readonly float[] history = new float[TapCount(MaxFactor)];
    private int historyHead, phase;

    public Decimator()
    {
        for (int factor = 2; factor <= MaxFactor; factor *= 2) taps[factor] = Design(factor);
    }

    public int Factor { get; private set; } = 1;

    // Analysis-rate rule (also documented for Lane B's bin→Hz mapping): D = 8 from 300 kHz, 4 from 150 kHz,
    // 2 from 75 kHz, otherwise 1.
    public static int FactorFor(int sampleRate) => sampleRate >= 300_000 ? 8 : sampleRate >= 150_000 ? 4 : sampleRate >= 75_000 ? 2 : 1;

    public void Configure(int sampleRate)
    {
        Factor = FactorFor(sampleRate);
        Reset();
    }

    public void Reset()
    {
        Array.Clear(history);
        historyHead = 0;
        phase = 0;
    }

    // Writes floor((phase + input.Length) / Factor) samples to output (which must be large enough) and returns the count.
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (Factor == 1)
        {
            input.CopyTo(output);
            return input.Length;
        }
        float[] h = taps[Factor];
        int length = h.Length, written = 0;
        for (int n = 0; n < input.Length; n++)
        {
            history[historyHead] = input[n];
            historyHead = (historyHead + 1) % length;
            if (++phase < Factor) continue;
            phase = 0;
            float sum = 0;
            for (int t = 0, index = historyHead; t < length; t++)
            {
                index = index == 0 ? length - 1 : index - 1;
                sum += h[t] * history[index];
            }
            output[written++] = sum;
        }
        return written;
    }

    private static int TapCount(int factor) => 16 * factor - 1;

    private static float[] Design(int factor)
    {
        int count = TapCount(factor);
        var h = new float[count];
        double cutoff = 0.5625 / factor; // cycles per input sample
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double m = i - (count - 1) / 2.0;
            double sinc = m == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * m) / (Math.PI * m);
            double window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (count - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (count - 1));
            h[i] = (float)(sinc * window);
            sum += h[i];
        }
        for (int i = 0; i < count; i++) h[i] = (float)(h[i] / sum); // unity gain at DC
        return h;
    }
}
