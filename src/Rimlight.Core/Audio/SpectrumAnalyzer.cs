namespace Rimlight.Core.Audio;

// Render-thread-owned rolling window. Empty chunks retain the latest samples;
// no-packet timeout and silence policy belong to the C2 analyzer.
internal sealed class SpectrumAnalyzer
{
    // Driver glitches can deliver NaN, Inf or absurd values. They become 0 or are clamped on the way in,
    // so one bad sample can't poison the spectrum, the gains or the envelopes.
    private const float SampleLimit = 16f;

    private readonly Fft fft;
    private readonly HannWindow window;
    private readonly float[] ring;
    private readonly float[] windowed;
    private readonly float[] real;
    private readonly float[] imaginary;
    private readonly float[] magnitudes;
    private int next;

    public SpectrumAnalyzer(int size = 2048)
    {
        fft = new Fft(size);
        window = new HannWindow(size);
        ring = new float[size];
        windowed = new float[size];
        real = new float[size];
        imaginary = new float[size];
        magnitudes = new float[size / 2 + 1];
    }

    public ReadOnlySpan<float> Magnitudes => magnitudes;

    // Time-domain RMS (before the Hann window) of the quietest quarter of the current window. When any
    // quarter of the window is silent, the Hann-weighted band magnitudes collapse even though the whole-window
    // RMS can still be high, e.g. while a gap or the start of music only partly fills the window.
    // The supported two-sample FFT uses one-sample blocks because it has no complete quarter.
    public float QuietestQuarterRms { get; private set; }

    public void Process(ReadOnlySpan<float> samples)
    {
        // No new samples: the window, magnitudes and RMS would be recomputed bit-identically.
        if (samples.IsEmpty) return;

        if (samples.Length >= ring.Length)
        {
            CopySanitized(samples[^ring.Length..], ring);
            next = 0;
        }
        else
        {
            int first = Math.Min(samples.Length, ring.Length - next);
            CopySanitized(samples[..first], ring.AsSpan(next));
            CopySanitized(samples[first..], ring);
            next = (next + samples.Length) & (ring.Length - 1);
        }

        // Unwritten startup slots are zero, preceding the samples received so far.
        ring.AsSpan(next).CopyTo(windowed);
        ring.AsSpan(0, next).CopyTo(windowed.AsSpan(ring.Length - next));
        int quarter = Math.Max(1, windowed.Length / 4);
        float quietest = float.MaxValue;
        for (int start = 0; start < windowed.Length; start += quarter)
        {
            float squares = 0;
            for (int i = start; i < start + quarter; i++) squares += windowed[i] * windowed[i];
            quietest = MathF.Min(quietest, squares);
        }
        QuietestQuarterRms = MathF.Sqrt(quietest / quarter);
        window.Apply(windowed);
        fft.Transform(windowed, real, imaginary);
        for (int k = 0; k < magnitudes.Length; k++)
            magnitudes[k] = MathF.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]);
    }

    public void Reset()
    {
        Array.Clear(ring);
        Array.Clear(windowed);
        Array.Clear(real);
        Array.Clear(imaginary);
        Array.Clear(magnitudes);
        QuietestQuarterRms = 0;
        next = 0;
    }

    private static void CopySanitized(ReadOnlySpan<float> source, Span<float> destination)
    {
        for (int i = 0; i < source.Length; i++)
        {
            float x = source[i];
            destination[i] = float.IsFinite(x) ? Math.Clamp(x, -SampleLimit, SampleLimit) : 0f;
        }
    }
}
