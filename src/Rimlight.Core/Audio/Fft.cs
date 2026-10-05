namespace Rimlight.Core.Audio;

// Unnormalized forward radix-2 FFT. Input is real; output includes all N complex bins.
// Tables are immutable after construction; caller owns the work buffers.
internal sealed class Fft
{
    private readonly int[] reversed;
    private readonly float[] cosine;
    private readonly float[] sine;

    public Fft(int size)
    {
        if (size < 2 || size > 1 << 20 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Expected a power of two from 2 through 1048576.");
        Size = size;
        reversed = new int[size];
        cosine = new float[size / 2];
        sine = new float[size / 2];
        for (int i = 0; i < size; i++)
        {
            int value = i, result = 0;
            for (int bit = size / 2; bit > 0; bit >>= 1)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }
            reversed[i] = result;
        }
        for (int i = 0; i < size / 2; i++)
        {
            float angle = -2 * MathF.PI * i / size;
            (sine[i], cosine[i]) = MathF.SinCos(angle);
        }
    }

    public int Size { get; }

    // The three buffers must be disjoint and exactly N elements long.
    public void Transform(ReadOnlySpan<float> input, Span<float> real, Span<float> imaginary)
    {
        if (input.Length != Size || real.Length != Size || imaginary.Length != Size)
            throw new ArgumentException("FFT buffers must match the configured window size.");
        for (int i = 0; i < Size; i++) real[reversed[i]] = input[i];
        imaginary.Clear();
        for (int width = 2; width <= Size; width <<= 1)
        {
            int half = width / 2;
            int stride = Size / width;
            for (int block = 0; block < Size; block += width)
            {
                for (int j = 0; j < half; j++)
                {
                    int even = block + j, odd = even + half, twiddle = j * stride;
                    float tr = cosine[twiddle] * real[odd] - sine[twiddle] * imaginary[odd];
                    float ti = sine[twiddle] * real[odd] + cosine[twiddle] * imaginary[odd];
                    real[odd] = real[even] - tr;
                    imaginary[odd] = imaginary[even] - ti;
                    real[even] += tr;
                    imaginary[even] += ti;
                }
            }
        }
    }
}
