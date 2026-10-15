using Rimlight.Platform.Audio;
using Xunit;

namespace Rimlight.Platform.Tests;

// AudioRingBuffer (K2): the lock-free single-producer, single-consumer ring between the WASAPI capture thread and the
// overlay thread (doc 02, doc 03 §1).
public sealed class AudioRingBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void TheCapacityMustBeAPowerOfTwo(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioRingBuffer(capacity));

    [Theory]
    [InlineData(48000, 2.0, 131072)]
    [InlineData(44100, 2.0, 131072)]
    [InlineData(96000, 2.0, 262144)]
    [InlineData(192000, 2.0, 524288)]
    [InlineData(8000, 2.0, 16384)]
    [InlineData(1, 0.0, 2)]
    public void CapacityForHoldsTheDurationInAPowerOfTwo(int sampleRate, double seconds, int expected)
    {
        int capacity = AudioRingBuffer.CapacityFor(sampleRate, seconds);
        Assert.Equal(expected, capacity);
        Assert.True(capacity >= sampleRate * seconds);
    }

    [Fact]
    public void SamplesComeOutOnceInOrderAcrossTheWrap()
    {
        var ring = new AudioRingBuffer(8);
        float[] read = new float[8];
        Assert.Equal(0, ring.Read(read)); // nothing written yet
        float next = 0, expected = 0;
        for (int round = 0; round < 50; round++)
        {
            int count = round % 8 + 1; // 1..8 samples, so writes and reads straddle the end of the buffer
            float[] write = new float[count];
            for (int i = 0; i < count; i++) write[i] = next++;
            Assert.Equal(count, ring.Write(write));
            Assert.Equal(count, ring.Read(read));
            for (int i = 0; i < count; i++) Assert.Equal(expected++, read[i]);
            Assert.Equal(0, ring.Read(read)); // each sample once
        }
        Assert.Equal(0, ring.Dropped);
    }

    [Fact]
    public void AFullRingDropsTheNewestAndCountsThem()
    {
        // The reader fell more than the capacity behind (a stalled render thread): unread samples are never overwritten.
        var ring = new AudioRingBuffer(8);
        Assert.Equal(6, ring.Write([0, 1, 2, 3, 4, 5]));
        Assert.Equal(2, ring.Write([6, 7, 8, 9, 10]));
        Assert.Equal(3, ring.Dropped);
        Assert.Equal(0, ring.Write([11]));
        Assert.Equal(4, ring.Dropped);

        float[] read = new float[8];
        Assert.Equal(8, ring.Read(read));
        Assert.Equal(new float[] { 0, 1, 2, 3, 4, 5, 6, 7 }, read);
        Assert.Equal(1, ring.Write([12])); // room again
        Assert.Equal(1, ring.Read(read));
        Assert.Equal(12, read[0]);
    }

    [Fact]
    public void ASmallerDestinationSkipsTheOldest()
    {
        // The newest audio always arrives; what doesn't fit is the oldest, and it is gone afterwards.
        var ring = new AudioRingBuffer(16);
        ring.Write([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        float[] read = new float[4];
        Assert.Equal(4, ring.Read(read));
        Assert.Equal(new float[] { 6, 7, 8, 9 }, read);
        Assert.Equal(0, ring.Read(read));
        Assert.Equal(0, ring.Read(Span<float>.Empty));
        ring.Write([10, 11]);
        Assert.Equal(0, ring.Read(Span<float>.Empty)); // an empty read skips everything waiting
        Assert.Equal(0, ring.Read(read));
    }

    [Fact]
    public void WritingAndReadingAllocateNothing()
    {
        var ring = new AudioRingBuffer(AudioRingBuffer.CapacityFor(48000, 2));
        float[] packet = new float[480];
        float[] frame = new float[ring.Capacity];
        ring.Write(packet);
        ring.Read(frame);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            ring.Write(packet);
            if (i % 2 == 1) ring.Read(frame);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TwoThreadsSeeEverySampleInOrderOrACountedDrop()
    {
        // The capture thread writes a counting sequence in packets while the render thread reads in frames of varying
        // size, sometimes pausing so the ring fills up. Every value read must be larger than the one before: samples the
        // writer dropped (the ring was full) or the reader skipped (its buffer was smaller) are gaps, but never repeats,
        // reorderings or torn values. Values stay below 2^24, so each is exact in a float.
        const int Capacity = 4096;
        const int Total = 4_000_000;
        var ring = new AudioRingBuffer(Capacity);
        long written = 0, dropped = 0;
        var writer = new Thread(() =>
        {
            float[] packet = new float[333];
            int value = 0;
            while (value < Total)
            {
                for (int i = 0; i < packet.Length; i++) packet[i] = value + i;
                int accepted = ring.Write(packet);
                dropped += packet.Length - accepted;
                written += accepted;
                value += packet.Length;
            }
        });
        writer.Start();

        float[] frame = new float[Capacity];
        long seen = 0, notIncreasing = 0;
        float last = -1;
        var random = new Random(3);
        void Drain(int size)
        {
            int count = ring.Read(frame.AsSpan(0, size));
            for (int i = 0; i < count; i++)
            {
                if (!(frame[i] > last)) notIncreasing++;
                last = frame[i];
            }
            seen += count;
        }
        while (writer.IsAlive)
        {
            Drain(random.Next(1, Capacity + 1));
            if (random.Next(64) == 0) Thread.Sleep(1);
        }
        writer.Join();
        Drain(Capacity);

        Assert.Equal(0, notIncreasing);
        Assert.Equal(dropped, ring.Dropped);
        Assert.Equal(Total + 333 - Total % 333, written + dropped);
        Assert.True(seen > 0 && seen <= written, $"read {seen} of {written} written");
    }
}
