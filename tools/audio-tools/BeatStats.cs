namespace Rimlight.AudioTools;

/// <summary>How detected beats line up with known kick times.</summary>
/// <param name="Kicks">Kicks at or after the start time that had time to be detected.</param>
/// <param name="Hits">Kicks followed by a detected beat within the allowed delay.</param>
/// <param name="FalseBeats">Detected beats at or after the start time that follow no kick.</param>
/// <param name="MedianLatency">Median delay from kick to detected beat, in seconds (0 without hits).</param>
/// <param name="MaxLatency">Largest delay from kick to detected beat, in seconds (0 without hits).</param>
public readonly record struct BeatMatch(int Kicks, int Hits, int FalseBeats, double MedianLatency, double MaxLatency)
{
    /// <summary>Hits as a fraction of kicks (1 when there were no kicks).</summary>
    public double HitRate => Kicks == 0 ? 1 : Hits / (double)Kicks;
}

/// <summary>Tempo and accuracy statistics over beat times.</summary>
public static class BeatStats
{
    /// <summary>Intervals longer than this (under 30 BPM) are pauses, not tempo, as in the analyzer.</summary>
    public const double MaxIntervalSeconds = 2;

    /// <summary>Intervals further than this fraction from the median are missed or extra beats, not tempo.</summary>
    public const double InlierTolerance = 0.25;

    /// <summary>
    /// The tempo from the intervals between consecutive beats: the median finds the typical interval, and the mean of
    /// the intervals within ±25 % of it gives the tempo. Beats are only seen at frame times, so at a steady 60 fps a
    /// 126 BPM interval is 28 or 29 frames; the median alone then reads 124.1 or 128.6 BPM, the mean reads 126.0.
    /// </summary>
    /// <param name="beats">Beat times in ascending order.</param>
    /// <param name="fromSeconds">Ignore beats before this time (analyzer warm-up).</param>
    /// <returns>Beats per minute, or 0 with fewer than two beats.</returns>
    public static double Tempo(IReadOnlyList<double> beats, double fromSeconds = 0)
    {
        var intervals = new List<double>();
        for (int i = 1; i < beats.Count; i++)
        {
            double interval = beats[i] - beats[i - 1];
            if (beats[i - 1] >= fromSeconds && interval > 0 && interval <= MaxIntervalSeconds) intervals.Add(interval);
        }
        if (intervals.Count == 0) return 0;
        double median = Median(intervals);
        return 60 / intervals.Where(i => Math.Abs(i - median) <= InlierTolerance * median).Average();
    }

    /// <summary>Matches detected beats to the kicks that caused them.</summary>
    /// <param name="beats">Detected beat times in ascending order.</param>
    /// <param name="kicks">Kick onset times in ascending order.</param>
    /// <param name="fromSeconds">Ignore kicks and beats before this time (analyzer warm-up).</param>
    /// <param name="toSeconds">Ignore kicks after this time minus <paramref name="maxDelay"/> (the end of the audio).</param>
    /// <param name="maxDelay">The longest kick-to-beat delay that still counts as a hit, in seconds.</param>
    /// <returns>Hit, miss and false-beat counts with latencies.</returns>
    public static BeatMatch Match(IReadOnlyList<double> beats, IReadOnlyList<double> kicks, double fromSeconds, double toSeconds, double maxDelay = 0.1)
    {
        var used = new bool[beats.Count];
        var latencies = new List<double>();
        int kickCount = 0, next = 0;
        foreach (double kick in kicks)
        {
            // Every kick claims its beat, so a beat caused by a kick just before fromSeconds is not a false beat;
            // only kicks inside the range are counted.
            bool counted = kick >= fromSeconds && kick + maxDelay <= toSeconds;
            if (counted) kickCount++;
            while (next < beats.Count && beats[next] < kick) next++;
            for (int b = next; b < beats.Count && beats[b] <= kick + maxDelay; b++)
            {
                if (used[b]) continue;
                used[b] = true;
                if (counted) latencies.Add(beats[b] - kick);
                break;
            }
        }
        int falseBeats = 0;
        for (int b = 0; b < beats.Count; b++)
            if (!used[b] && beats[b] >= fromSeconds && beats[b] <= toSeconds) falseBeats++;
        return new BeatMatch(kickCount, latencies.Count, falseBeats,
            latencies.Count == 0 ? 0 : Median(latencies), latencies.Count == 0 ? 0 : latencies.Max());
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
