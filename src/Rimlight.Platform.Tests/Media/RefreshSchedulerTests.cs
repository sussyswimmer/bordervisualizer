using System.Diagnostics;
using Rimlight.Platform.Media;
using Xunit;

namespace Rimlight.Platform.Tests;

// RefreshScheduler (K4, doc 05 §1): when NowPlayingService reads the media session again. Bursts of session events are
// debounced and capped; a retry or reconnect deadline is kept as given; with nothing requested a refresh still runs
// after the idle time, so a session manager that died without events is noticed (K4's review). Real time, so only the
// lower bounds are tight.
[Collection(SerialTests.Name)]
public sealed class RefreshSchedulerTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ARequestRefreshesOnceTheBurstIsOver()
    {
        var scheduler = new RefreshScheduler(TimeSpan.FromMilliseconds(120), TimeSpan.FromSeconds(1), Long);
        var clock = Stopwatch.StartNew();
        scheduler.Request();
        await scheduler.WaitAsync(CancellationToken.None);
        Assert.InRange(clock.ElapsedMilliseconds, 115, 5000);
    }

    [Fact]
    public async Task ADeadlineIsKeptAsGivenNotCutToTheBurstCap()
    {
        // The reconnect retry (5 s in the service) ran every second, the burst cap, before K4's review.
        var scheduler = new RefreshScheduler(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100), Long);
        var clock = Stopwatch.StartNew();
        scheduler.RequestBy(Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2 / 5); // 400 ms from now
        await scheduler.WaitAsync(CancellationToken.None);
        Assert.InRange(clock.ElapsedMilliseconds, 395, 5000);

        // An earlier deadline wins over a later one.
        clock.Restart();
        scheduler.RequestBy(Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3);
        scheduler.RequestBy(Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5); // 200 ms
        await scheduler.WaitAsync(CancellationToken.None);
        Assert.InRange(clock.ElapsedMilliseconds, 195, 2900);
    }

    [Fact]
    public async Task ABurstIsCappedWhileAFarDeadlineWaits()
    {
        var scheduler = new RefreshScheduler(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(150), Long);
        scheduler.RequestBy(Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5); // a reconnect, 5 s away
        using var stop = new CancellationTokenSource();
        var events = new Thread(() =>
        {
            // A session that never stops firing: a request every 10 ms, so the debounce alone would never run out.
            while (!stop.IsCancellationRequested)
            {
                scheduler.Request();
                Thread.Sleep(10);
            }
        });
        var clock = Stopwatch.StartNew();
        events.Start();
        try
        {
            await scheduler.WaitAsync(CancellationToken.None);
            Assert.InRange(clock.ElapsedMilliseconds, 145, 2000); // the burst cap, long before the deadline
        }
        finally
        {
            stop.Cancel();
            events.Join();
        }
    }

    [Fact]
    public async Task WithNothingRequestedARefreshStillRunsAfterTheIdleTime()
    {
        var scheduler = new RefreshScheduler(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        await scheduler.WaitAsync(CancellationToken.None);
        // The idle wait is a plain timeout, which Windows' 15.6 ms timer ticks can end a tick early; the deadlines above
        // are checked against the Stopwatch and never end early.
        Assert.InRange(clock.ElapsedMilliseconds, 260, 5000);
    }

    [Fact]
    public async Task CancellingTheWaitThrows()
    {
        var scheduler = new RefreshScheduler(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), Long);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.WaitAsync(cancel.Token));
    }
}
