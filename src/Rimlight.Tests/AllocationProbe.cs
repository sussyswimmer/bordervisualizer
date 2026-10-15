namespace Rimlight.Tests;

// Allocation checks measure their whole loop up to three times and assert on the smallest count. In full parallel
// runs, checks that pass alone now and then saw one lump of a few KB on the test's thread (7,896 B in
// LightEngineTests, about 8 KB in the analyzer benchmark, close to one 8 KB allocation quantum), and the cause is still
// open (docs/PROGRESS.md, C12 and C13 notes). A real per-frame allocation shows in every run of the loop; a one-off
// like that lands in one. Each run is the same full loop, so a rare path is never left out.
internal static class AllocationProbe
{
    /// <summary>The fewest bytes this thread allocated in one of up to <paramref name="runs"/> runs of
    /// <paramref name="loop"/>. It stops at a run that allocated nothing, which none can beat. The delegate is built by
    /// the caller, before the first count, so it isn't counted.</summary>
    public static long MinBytes(Action loop, int runs = 3)
    {
        long fewest = long.MaxValue;
        for (int run = 0; run < runs && fewest > 0; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            loop();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return fewest;
    }
}
