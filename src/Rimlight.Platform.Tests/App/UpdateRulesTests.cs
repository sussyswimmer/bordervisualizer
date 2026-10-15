using System.Net;
using System.Net.Http;
using Rimlight.App.Updates;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// The update service's pure parts (K9, doc 06 §5): when automatic checks are due, and what the user is told.
public sealed class UpdateRulesTests
{
    private const long Second = 1000;
    private const long Minute = 60 * Second;
    private const long Hour = 60 * Minute;

    [Fact]
    public void TheFirstCheckIs30SecondsAfterTheStart()
    {
        var schedule = new UpdateSchedule();
        schedule.Start(5 * Hour);
        Assert.Equal(TimeSpan.FromSeconds(30), schedule.UntilNext(5 * Hour));
        Assert.Equal(TimeSpan.FromSeconds(10), schedule.UntilNext(5 * Hour + 20 * Second));
        Assert.Equal(TimeSpan.Zero, schedule.UntilNext(5 * Hour + 31 * Second)); // never negative
    }

    [Fact]
    public void ThenEvery12HoursOr1HourAfterAFailure()
    {
        var schedule = new UpdateSchedule();
        schedule.Start(0);
        schedule.Checking(30 * Second);
        Assert.Equal(TimeSpan.FromHours(12), schedule.UntilNext(30 * Second));
        schedule.Checking(12 * Hour + 30 * Second);
        schedule.Failed(); // offline at sign-in, GitHub's rate limit, a failed download
        Assert.Equal(TimeSpan.FromHours(1), schedule.UntilNext(12 * Hour + 30 * Second));
        schedule.Checking(13 * Hour + 30 * Second); // the retry works
        Assert.Equal(TimeSpan.FromHours(12), schedule.UntilNext(13 * Hour + 30 * Second));
    }

    [Fact]
    public void TheUsersOwnCheckCountsAndTurningTheSettingOffAndOnDoesNotAskAgain()
    {
        var schedule = new UpdateSchedule();
        schedule.Start(0);
        schedule.Checking(10 * Minute);                // "Check for updates" from the tray
        Assert.Equal(TimeSpan.FromHours(12) - TimeSpan.FromMinutes(10), schedule.UntilNext(20 * Minute));
        schedule.Start(1 * Hour);                     // "Update automatically" off and on again
        Assert.Equal(TimeSpan.FromMinutes(12 * 60 + 10 - 60), schedule.UntilNext(1 * Hour));
        schedule.Start(20 * Hour);                    // long after the last check: 30 s again, not at once
        Assert.Equal(TimeSpan.FromSeconds(30), schedule.UntilNext(20 * Hour));
    }

    [Fact]
    public void FailuresAreToldInAFewPlainWords()
    {
        Assert.Contains("limiting", UpdateMessages.CheckFailed(new HttpRequestException("", null, HttpStatusCode.Forbidden)));
        Assert.Contains("limiting", UpdateMessages.CheckFailed(new HttpRequestException("", null, HttpStatusCode.TooManyRequests)));
        Assert.Contains("error 500", UpdateMessages.CheckFailed(new HttpRequestException("", null, HttpStatusCode.InternalServerError)));
        Assert.Contains("error 404", UpdateMessages.CheckFailed(new HttpRequestException("", null, HttpStatusCode.NotFound)));
        string offline = "Couldn't reach GitHub to check for updates. Check the internet connection and try again.";
        Assert.Equal(offline, UpdateMessages.CheckFailed(new HttpRequestException("No such host is known.")));
        Assert.Equal(offline, UpdateMessages.CheckFailed(new TimeoutException()));
        Assert.Equal(offline, UpdateMessages.CheckFailed(new TaskCanceledException()));
        Assert.Contains("logs folder", UpdateMessages.CheckFailed(new InvalidOperationException()));

        Assert.Equal("Version 1.2.0 couldn't be downloaded. Check the internet connection and try again.",
            UpdateMessages.DownloadFailed("1.2.0", new OperationCanceledException()));
        Assert.Contains("logs folder", UpdateMessages.DownloadFailed("1.2.0", new IOException()));
    }

    [Fact]
    public void TheMessagesNameTheVersionAndTheTrayMenuItem()
    {
        Assert.Equal("You're up to date (version 1.0.0).", UpdateMessages.UpToDate("1.0.0"));
        Assert.Equal("Downloading version 1.1.0…", UpdateMessages.Downloading("1.1.0"));
        Assert.Equal("Updated to version 1.1.0.", UpdateMessages.Updated("1.1.0"));
        // doc 06 §5's first line, then where to click; the tray item is "Restart to update (version X)".
        Assert.StartsWith($"Update ready — restart {AppInfo.Name} to apply.", UpdateMessages.Ready("1.1.0"));
        Assert.Contains("Restart to update", UpdateMessages.Ready("1.1.0"));
        Assert.Contains("1.1.0", UpdateMessages.ReadyQuestion("1.1.0"));
        Assert.Contains("installed copy", UpdateMessages.NotInstalled("0.1.0"));
    }
}
