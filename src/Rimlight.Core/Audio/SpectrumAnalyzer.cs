namespace Rimlight.Core.Audio;

// Render-thread-owned rolling window. Empty chunks retain the latest samples;
// no-packet timeout and silence policy belong to the C2 analyzer.
internal sealed class SpectrumAnalyzer
{
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

    public void Process(ReadOnlySpan<float> samples)
    {
        if (samples.Length >= ring.Length)
        {
            samples[^ring.Length..].CopyTo(ring);
            next = 0;
        }
        else
        {
            int first = Math.Min(samples.Length, ring.Length - next);
            samples[..first].CopyTo(ring.AsSpan(next));
            samples[first..].CopyTo(ring);
            next = (next + samples.Length) & (ring.Length - 1);
        }

        // Unwritten startup slots are zero, preceding the samples received so far.
        ring.AsSpan(next).CopyTo(windowed);
        ring.AsSpan(0, next).CopyTo(windowed.AsSpan(ring.Length - next));
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
        next = 0;
    }
}
