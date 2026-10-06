namespace Rimlight.Platform.Audio;

// Lock-free single-producer / single-consumer ring of mono samples (doc 03 §1, doc 02). The WASAPI capture thread is
// the only writer and the render thread the only reader; neither ever waits for the other.
//
// Each side owns one counter and publishes it with a release store; the other side reads it with an acquire load.
// The producer copies samples in before it publishes `written`, and the consumer copies them out before it
// publishes `read`, so neither side touches a slot the other is still using. When the ring is full (the render
// thread stalled for longer than the ring holds) the newest samples are dropped rather than overwriting unread ones.
internal sealed class AudioRingBuffer
{
    private readonly float[] buffer;
    private readonly int mask;
    private long written; // total samples written; producer-owned
    private long read;    // total samples read; consumer-owned
    private long dropped; // samples the producer had no room for; producer-owned

    public AudioRingBuffer(int capacity)
    {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Expected a power of two.");
        buffer = new float[capacity];
        mask = capacity - 1;
    }

    public int Capacity => buffer.Length;

    // Samples the producer has dropped because the ring was full. Readable from any thread.
    public long Dropped => Volatile.Read(ref dropped);

    // A power-of-two capacity holding at least the given duration at the sample rate.
    public static int CapacityFor(int sampleRate, double seconds)
    {
        long samples = Math.Max(2, (long)Math.Ceiling(sampleRate * seconds));
        return (int)Math.Min(1 << 30, System.Numerics.BitOperations.RoundUpToPowerOf2((ulong)samples));
    }

    // Producer only. Never blocks or allocates. Returns how many samples fit; the rest are dropped.
    public int Write(ReadOnlySpan<float> samples)
    {
        long w = written;
        int free = buffer.Length - (int)(w - Volatile.Read(ref read));
        int count = Math.Min(free, samples.Length);
        if (count > 0)
        {
            int index = (int)(w & mask);
            int first = Math.Min(count, buffer.Length - index);
            samples[..first].CopyTo(buffer.AsSpan(index));
            samples[first..count].CopyTo(buffer);
            Volatile.Write(ref written, w + count);
        }
        if (count < samples.Length) Volatile.Write(ref dropped, dropped + (samples.Length - count));
        return count;
    }

    // Consumer only. Copies every unread sample, oldest first, into destination and returns the count. If more are
    // unread than fit, the oldest are skipped, so the newest samples always arrive. Never blocks or allocates.
    public int Read(Span<float> destination)
    {
        long r = read;
        long w = Volatile.Read(ref written);
        long available = w - r;
        if (available > destination.Length) r = w - destination.Length;
        int count = (int)(w - r);
        if (count > 0)
        {
            int index = (int)(r & mask);
            int first = Math.Min(count, buffer.Length - index);
            buffer.AsSpan(index, first).CopyTo(destination);
            buffer.AsSpan(0, count - first).CopyTo(destination[first..]);
        }
        Volatile.Write(ref read, w);
        return count;
    }
}
