using System.Diagnostics;

namespace Rimlight.Platform.Media;

// When the media session is read again (doc 05 §1). Event bursts are debounced: a refresh runs `debounce` after the
// latest Request, but no later than `maxWait` after the loop first saw the burst, so a session that never stops firing
// is still read. RequestBy deadlines (a retry, a reconnect) are kept as given; the cap applies to bursts only. With
// nothing requested, a refresh still runs every `idle`. Requests come from any thread (WinRT event handlers); one loop
// waits in WaitAsync. No locks.
internal sealed class RefreshScheduler(TimeSpan debounce, TimeSpan maxWait, TimeSpan idle)
{
    private const long Never = long.MaxValue;

    private readonly long debounceTicks = ToTicks(debounce);
    private readonly long maxWaitTicks = ToTicks(maxWait);
    private readonly SemaphoreSlim signal = new(0); // never disposed: it holds no wait handle, and late events may release it
    private long burstDue = Never;                  // Stopwatch timestamp: the end of the current burst of Requests
    private long deadline = Never;                  // Stopwatch timestamp: the earliest RequestBy

    // Something changed: refresh once the burst is over.
    public void Request()
    {
        Volatile.Write(ref burstDue, Stopwatch.GetTimestamp() + debounceTicks);
        Signal();
    }

    // Refresh no later than this Stopwatch timestamp (a retry, a reconnect, the first read). An earlier deadline wins.
    public void RequestBy(long timestamp)
    {
        long seen = Volatile.Read(ref deadline);
        while (timestamp < seen)
        {
            long previous = Interlocked.CompareExchange(ref deadline, timestamp, seen);
            if (previous == seen) break;
            seen = previous;
        }
        Signal();
    }

    // Completes when a refresh is due, or after `idle` with nothing requested, and claims it: requests made from here
    // on schedule the next one. Throws OperationCanceledException when `token` is cancelled.
    public async Task WaitAsync(CancellationToken token)
    {
        long burstSince = 0; // when this wait first saw the current burst
        while (true)
        {
            long now = Stopwatch.GetTimestamp();
            long at = Volatile.Read(ref deadline);
            long burst = Volatile.Read(ref burstDue);
            if (burst == Never)
            {
                burstSince = 0;
            }
            else
            {
                if (burstSince == 0) burstSince = now;
                at = Math.Min(at, Math.Min(burst, burstSince + maxWaitTicks));
            }
            if (at == Never)
            {
                // Nothing requested. A request wakes the wait and is read in the next pass; a timeout is a refresh too.
                if (!await signal.WaitAsync(idle, token).ConfigureAwait(false)) return;
                continue;
            }
            if (at > now)
            {
                // A new request wakes the wait early; the due time is then read again.
                double milliseconds = (at - now) * 1000.0 / Stopwatch.Frequency;
                await signal.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(milliseconds))), token).ConfigureAwait(false);
                continue;
            }
            // Both claimed unconditionally: a request that arrived since they were read is covered too, because the
            // refresh that follows reads the session after it, and whoever needs a later deadline sets it again. (A
            // compare-and-swap here could fail forever under a stream of requests.)
            Interlocked.Exchange(ref burstDue, Never);
            Interlocked.Exchange(ref deadline, Never);
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
