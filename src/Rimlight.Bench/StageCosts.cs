using System.Diagnostics;

namespace Rimlight.Bench;

/// <summary>The stages of a frame that are timed separately, in report order.</summary>
internal enum Stage
{
    /// <summary>The whole frame.</summary>
    Frame,
    /// <summary>The audio analyzer.</summary>
    Analyzer,
    /// <summary>The palette blender and gradient.</summary>
    Blender,
    /// <summary>The light engine.</summary>
    Engine,
}

// Per-stage cost distributions of the frames a run measured, in constant memory (one LatencyHistogram per stage).
// Record never allocates.
internal sealed class StageCosts
{
    /// <summary>Every stage, in report order.</summary>
    public static readonly Stage[] All = [Stage.Frame, Stage.Analyzer, Stage.Blender, Stage.Engine];

    private readonly LatencyHistogram[] histograms = [new(), new(), new(), new()];

    /// <summary>The number of frames recorded.</summary>
    public long Count => histograms[0].Count;

    /// <summary>One stage's distribution.</summary>
    /// <param name="stage">The stage.</param>
    public LatencyHistogram this[Stage stage] => histograms[(int)stage];

    /// <summary>Records one frame.</summary>
    /// <param name="ticks">The frame's cost per stage.</param>
    public void Record(in StageTicks ticks)
    {
        histograms[0].RecordTicks(ticks.Frame);
        histograms[1].RecordTicks(ticks.Analyzer);
        histograms[2].RecordTicks(ticks.Blender);
        histograms[3].RecordTicks(ticks.Engine);
    }

    /// <summary>A stage's label in reports.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The label.</returns>
    public static string Label(Stage stage) => stage switch
    {
        Stage.Frame => "whole frame",
        Stage.Analyzer => "analyzer",
        Stage.Blender => "blender + gradient",
        Stage.Engine => "light engine",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    /// <summary>What the timing itself adds to each stage, for the reports.</summary>
    public const string ClockReadsNote = "the analyzer and the light engine include about one clock read each, the blender + gradient about three and the whole frame about six";

    /// <summary>
    /// The cost of one <see cref="Stopwatch.GetTimestamp"/> call on this machine, in nanoseconds: the best of five
    /// averages over 200,000 calls (an average, because the clock's tick can be coarser than one call, e.g. 100 ns on
    /// Windows). The time between two reads includes about one read, so each timed interval carries about this much.
    /// </summary>
    /// <returns>The cost of one clock read.</returns>
    public static double ClockReadNanoseconds()
    {
        const int Calls = 200_000;
        double best = double.PositiveInfinity;
        for (int round = 0; round < 5; round++)
        {
            long start = Stopwatch.GetTimestamp(), sink = 0;
            for (int i = 0; i < Calls; i++) sink ^= Stopwatch.GetTimestamp();
            long ticks = Stopwatch.GetTimestamp() - start;
            if (sink == 42) ticks++; // keeps the loop from being optimized away; changes nothing measurable
            best = Math.Min(best, LatencyHistogram.TicksToNanoseconds(ticks) / Calls);
        }
        return best;
    }
}
