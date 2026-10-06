using System.Numerics;

namespace Rimlight.Core.Audio;

// Periodic Hann for spectral analysis: w[n] = (1 - cos(2*pi*n/N))/2.
internal sealed class HannWindow
{
    private readonly float[] coefficients;

    public HannWindow(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        coefficients = new float[size];
        for (int i = 0; i < size; i++) coefficients[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / size);
    }

    public ReadOnlySpan<float> Coefficients => coefficients;

    public void Apply(Span<float> samples)
    {
        if (samples.Length != coefficients.Length) throw new ArgumentException("Window size mismatch.", nameof(samples));
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= samples.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var values = new Vector<float>(samples.Slice(i));
                var weights = new Vector<float>(coefficients.AsSpan(i));
                (values * weights).CopyTo(samples.Slice(i));
            }
        }
        for (; i < samples.Length; i++) samples[i] *= coefficients[i];
    }
}
