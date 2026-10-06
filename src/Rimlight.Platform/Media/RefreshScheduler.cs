using System.Diagnostics;

namespace Rimlight.Platform.Media;

// When the media session is read again (doc 05 §1). Event bursts are debounced: a refresh runs `debounce` after the
// latest request, but no later than `maxWait` after the loop first saw one pending, so a session that never stops
// firing is still read. Requests come from any thread (WinRT event handlers); one loop waits in WaitAsync. No locks.
internal sealed class RefreshScheduler(TimeSpan debounce, TimeSpan maxWait)
{
    private const long Never = long.MaxValue;

    private readonly long debounceTicks = ToTicks(debounce);
    private readonly long maxWaitTicks = ToTicks(maxWait);
    private readonly SemaphoreSlim signal = new(0); // never disposed: it holds no wait handle, and late events may release it
    private long due = Never;                       // Stopwatch timestamp of the next refresh

    // Something changed: refresh once the burst is over.
    public void Request()
    {
        Volatile.Write(ref due, Stopwatch.GetTimestamp() + debounceTicks);
        Signal();
    }

    // Refresh no later than this Stopwatch timestamp (a retry, or the first read). An earlier refresh already due wins.
    public void RequestBy(long timestamp)
    {
        long seen = Volatile.Read(ref due);
        while (timestamp < seen)
        {
            long previous = Interlocked.CompareExchange(ref due, timestamp, seen);
            if (previous == seen) break;
            seen = previous;
        }
        Signal();
    }

    // Completes when a refresh is due and claims it: requests made from here on schedule the next one.
    // Throws OperationCanceledException when `token` is cancelled.
    public async Task WaitAsync(CancellationToken token)
    {
        long pendingSince = 0;
        while (true)
        {
            long next = Volatile.Read(ref due);
            if (next == Never)
            {
                pendingSince = 0;
                await signal.WaitAsync(token).ConfigureAwait(false);
                continue;
            }
            long now = Stopwatch.GetTimestamp();
            if (pendingSince == 0) pendingSince = now;
            long at = Math.Min(next, pendingSince + maxWaitTicks);
            if (at > now)
            {
                // A new request wakes the wait early; the due time is then read again.
                double milliseconds = (at - now) * 1000.0 / Stopwatch.Frequency;
                await signal.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(milliseconds))), token).ConfigureAwait(false);
                continue;
            }
            // Claimed unconditionally: a request that arrived since `next` was read is covered too, because the
            // refresh that follows reads the session after it. (A compare-and-swap here could fail forever under a
            // stream of requests.)
            Interlocked.Exchange(ref due, Never);
            return;
        }
    }

    // One pending release is enough to wake the loop; the count stays small however many events arrive.
    private void Signal()
    {
        if (signal.CurrentCount == 0) signal.Release();
    }

    private static long ToTicks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);
}
