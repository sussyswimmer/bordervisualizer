using System.Diagnostics;
using System.Numerics;

namespace Rimlight.Bench;

// Per-frame cost distribution in nanoseconds with constant memory, so an 8-hour soak (C12) costs the same as a
// short run. Buckets are log-linear: exact below 128 ns, then 128 per power of two (≤ 0.8 % relative error).
// Record never allocates.
internal sealed class LatencyHistogram
{
    private const int SubBuckets = 128;
    private const int SubBucketBits = 7;
    private const int MaxExponent = 47; // 2^47 ns ≈ 39 hours
    private readonly long[] counts = new long[SubBuckets + (MaxExponent - SubBucketBits + 1) * SubBuckets];

    public long Count { get; private set; }
    public long Min { get; private set; } = long.MaxValue;
    public long Max { get; private set; }
    public double TotalNanoseconds { get; private set; }
    public double Mean => Count == 0 ? 0 : TotalNanoseconds / Count;

    public static double TicksToNanoseconds(long ticks) => ticks * (1e9 / Stopwatch.Frequency);

    public void RecordTicks(long ticks) => Record((long)TicksToNanoseconds(ticks));

    public void Record(long nanoseconds)
    {
        long v = Math.Clamp(nanoseconds, 0, (1L << (MaxExponent + 1)) - 1);
        counts[Index(v)]++;
        Count++;
        TotalNanoseconds += v;
        if (v < Min) Min = v;
        if (v > Max) Max = v;
    }

    public void Clear()
    {
        Array.Clear(counts);
        Count = 0;
        Min = long.MaxValue;
        Max = 0;
        TotalNanoseconds = 0;
    }

    // The smallest recorded bucket value v such that at least `quantile` of the samples are ≤ v (bucket midpoint,
    // clamped to the exact min and max).
    public double Percentile(double quantile)
    {
        if (Count == 0) return 0;
        long rank = (long)Math.Ceiling(Math.Clamp(quantile, 0, 1) * Count);
        if (rank < 1) rank = 1;
        long seen = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            seen += counts[i];
            if (seen >= rank) return Math.Clamp(Midpoint(i), Min, Max);
        }
        return Max;
    }

    internal static int Index(long v)
    {
        if (v < SubBuckets) return (int)v;
        int exponent = 63 - BitOperations.LeadingZeroCount((ulong)v);
        int shift = exponent - SubBucketBits;
        return SubBuckets + shift * SubBuckets + (int)(v >> shift) - SubBuckets;
    }

    internal static double Midpoint(int index)
    {
        if (index < SubBuckets) return index;
        int shift = (index - SubBuckets) / SubBuckets;
        long low = (long)(SubBuckets + (index - SubBuckets) % SubBuckets) << shift;
        return low + ((1L << shift) - 1) / 2.0;
    }
}
