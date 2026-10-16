namespace Rimlight.Bench;

// The memory readings of a soak's measured part, in arrays sized and touched up front so recording one never allocates
// or faults in a page (the harness's own memory would show up in the working set it measures).
internal sealed class MemoryLog
{
    private readonly double[] times;
    private readonly MemoryReading[] readings;

    /// <summary>Creates a log for up to <paramref name="capacity"/> readings. Once it is full, each new reading replaces
    /// the last one, so the last reading is always the latest (the end of a run, which the flatness check needs).</summary>
    /// <param name="capacity">The most readings the run takes; at least 2.</param>
    public MemoryLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        times = new double[capacity];
        readings = new MemoryReading[capacity];
        times.AsSpan().Clear();
        readings.AsSpan().Clear();
    }

    /// <summary>The number of readings.</summary>
    public int Count { get; private set; }

    /// <summary>The largest working set and managed heap recorded.</summary>
    public MemoryReading Peak { get; private set; }

    /// <summary>Records a reading.</summary>
    /// <param name="seconds">Simulated time of the reading.</param>
    /// <param name="reading">The reading.</param>
    public void Add(double seconds, MemoryReading reading)
    {
        Peak = new MemoryReading(Math.Max(Peak.WorkingSetBytes, reading.WorkingSetBytes), Math.Max(Peak.ManagedHeapBytes, reading.ManagedHeapBytes),
            Math.Max(Peak.CompiledMethods, reading.CompiledMethods));
        int index = Count == times.Length ? Count - 1 : Count++;
        times[index] = seconds;
        readings[index] = reading;
    }

    /// <summary>The time of reading <paramref name="index"/>.</summary>
    /// <param name="index">From 0 to <see cref="Count"/> − 1.</param>
    /// <returns>Simulated seconds.</returns>
    public double TimeAt(int index) => times[index];

    /// <summary>Reading <paramref name="index"/>.</summary>
    /// <param name="index">From 0 to <see cref="Count"/> − 1.</param>
    /// <returns>The reading.</returns>
    public MemoryReading this[int index] => readings[index];

    /// <summary>The first reading at or after a time (the last one if none is).</summary>
    /// <param name="seconds">Simulated seconds.</param>
    /// <returns>Its index, or −1 for an empty log.</returns>
    public int IndexAt(double seconds)
    {
        for (int i = 0; i < Count; i++)
        {
            if (times[i] >= seconds) return i;
        }
        return Count - 1;
    }

    /// <summary>The least-squares slope of the working set over the readings from <paramref name="from"/> on.</summary>
    /// <param name="from">The first reading's index.</param>
    /// <returns>Bytes per hour; 0 with fewer than two readings.</returns>
    public double WorkingSetTrendPerHour(int from)
    {
        from = Math.Max(0, from);
        int n = Count - from;
        if (n < 2) return 0;
        double meanT = 0, meanW = 0;
        for (int i = from; i < Count; i++)
        {
            meanT += times[i] / n;
            meanW += readings[i].WorkingSetBytes / (double)n;
        }
        double covariance = 0, variance = 0;
        for (int i = from; i < Count; i++)
        {
            covariance += (times[i] - meanT) * (readings[i].WorkingSetBytes - meanW);
            variance += (times[i] - meanT) * (times[i] - meanT);
        }
        return variance > 0 ? covariance / variance * 3600 : 0;
    }
}
