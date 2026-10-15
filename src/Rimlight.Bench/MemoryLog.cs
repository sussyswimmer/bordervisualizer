namespace Rimlight.Bench;

// The memory readings of a soak's measured part, in arrays sized up front so recording one never allocates (the
// harness's own garbage would show up in the working set it measures).
internal sealed class MemoryLog
{
    private readonly double[] times;
    private readonly MemoryReading[] readings;

    /// <summary>Creates a log for up to <paramref name="capacity"/> readings; later readings are dropped.</summary>
    /// <param name="capacity">The most readings the run takes.</param>
    public MemoryLog(int capacity)
    {
        times = new double[capacity];
        readings = new MemoryReading[capacity];
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
        if (Count == times.Length) return;
        times[Count] = seconds;
        readings[Count] = reading;
        Count++;
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
