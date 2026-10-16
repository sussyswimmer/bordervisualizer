namespace Rimlight.App.Updates;

/// <summary>
/// When the next automatic update check is due (doc 06 §5): 30 s after the checks start (the app starts, or
/// "Update automatically" is turned on), then 12 h after the previous check, or 1 h after one that failed. Any check
/// counts, also one the user asked for, so turning the setting off and on again doesn't ask GitHub again within the
/// 12 h. Times are milliseconds of a clock that keeps counting while the computer sleeps
/// (<see cref="Environment.TickCount64"/>). Not thread-safe; the update service uses it on the UI thread.
/// </summary>
internal sealed class UpdateSchedule
{
    /// <summary>The first check after the checks start: the app's start isn't slowed by it.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);

    /// <summary>Between two checks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    /// <summary>After a check that failed, such as one at sign-in before the network is up (GitHub's limit is 60 an hour).</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    private long startedMs;
    private long? lastCheckMs;
    private bool lastCheckFailed;

    /// <summary>The checks (re)start: the next one is due <see cref="FirstDelay"/> from now at the earliest.</summary>
    /// <param name="nowMs">The clock now.</param>
    public void Start(long nowMs) => startedMs = nowMs;

    /// <summary>A check starts now (automatic or asked for); it counts as successful until <see cref="Failed"/>.</summary>
    /// <param name="nowMs">The clock now.</param>
    public void Checking(long nowMs)
    {
        lastCheckMs = nowMs;
        lastCheckFailed = false;
    }

    /// <summary>The check started last failed: the next one is due <see cref="RetryInterval"/> after it.</summary>
    public void Failed() => lastCheckFailed = true;

    /// <summary>How long until the next automatic check is due; zero when it is due now.</summary>
    /// <param name="nowMs">The clock now.</param>
    /// <returns>The time to wait, never negative.</returns>
    public TimeSpan UntilNext(long nowMs)
    {
        long due = startedMs + (long)FirstDelay.TotalMilliseconds;
        if (lastCheckMs is { } last) due = Math.Max(due, last + (long)(lastCheckFailed ? RetryInterval : Interval).TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(0, due - nowMs));
    }
}
