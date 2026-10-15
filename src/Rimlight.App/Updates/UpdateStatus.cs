using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Rimlight.App.Updates;

/// <summary>Where updates stand (<see cref="UpdateService.Status"/>).</summary>
internal enum UpdatePhase
{
    /// <summary>This copy isn't installed (a build run from source): it never checks.</summary>
    NotInstalled,

    /// <summary>Velopack didn't start, so this session can't update.</summary>
    Unavailable,

    /// <summary>Nothing checked yet in this session.</summary>
    Idle,

    /// <summary>Asking GitHub for the newest release.</summary>
    Checking,

    /// <summary>The newest release is the one running.</summary>
    UpToDate,

    /// <summary>A newer release is downloading (<see cref="UpdateStatus.Version"/>, <see cref="UpdateStatus.Percent"/>).</summary>
    Downloading,

    /// <summary>A newer release is downloaded and installs at the next start, or now with Restart now.</summary>
    Ready,

    /// <summary>The last check or download failed (<see cref="UpdateStatus.Failure"/>).</summary>
    Failed,
}

/// <summary>Why a check or a download failed, for what the user is told.</summary>
internal enum UpdateFailure
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>GitHub couldn't be reached: offline, DNS, a timeout, a dropped or stalled connection, a server error.</summary>
    Offline,

    /// <summary>GitHub refused for now (HTTP 403 or 429: 60 API requests an hour per IP address without a token).</summary>
    RateLimited,

    /// <summary>Anything else (a damaged download, a full disk, a missing release file); the log has the details.</summary>
    Other,
}

/// <summary>A snapshot of where updates stand. Immutable; made and read on the UI thread.</summary>
/// <param name="Phase">The phase.</param>
/// <param name="Version">The newer version (downloading, ready), else null.</param>
/// <param name="Percent">The download's progress, 0..100.</param>
/// <param name="Failure">Why the last attempt failed (<see cref="UpdatePhase.Failed"/>).</param>
/// <param name="WhileDownloading">Whether that failure ended a download rather than a check.</param>
internal sealed record UpdateStatus(
    UpdatePhase Phase, string? Version = null, int Percent = 0, UpdateFailure Failure = UpdateFailure.None, bool WhileDownloading = false)
{
    /// <summary>Whether a check or a download is running.</summary>
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading;

    /// <summary>Whether this session can check at all.</summary>
    public bool CanCheck => Phase is not (UpdatePhase.NotInstalled or UpdatePhase.Unavailable);

    /// <summary>The line Settings' About page shows under its buttons; empty for nothing to say.</summary>
    /// <param name="appName">The app's name.</param>
    /// <returns>The text.</returns>
    public string Describe(string appName) => Phase switch
    {
        UpdatePhase.NotInstalled => "Updates work in the installed app. This copy runs from a build folder, so it doesn't check.",
        UpdatePhase.Unavailable => "Updates aren't available in this session. Open logs folder shows why.",
        UpdatePhase.Checking => "Checking for updates…",
        UpdatePhase.UpToDate => $"You're up to date. This is the newest version of {appName}.",
        UpdatePhase.Downloading => $"Downloading version {Version}… {Math.Clamp(Percent, 0, 100)}%",
        UpdatePhase.Ready => $"Version {Version} is ready. Restart {appName} to install it.",
        UpdatePhase.Failed => UpdatePolicy.FailureMessage(Failure, WhileDownloading),
        _ => "",
    };
}

/// <summary>
/// The rules behind the update checks (doc 06 §5), kept free of Windows, WPF and Velopack calls: when the next background
/// check is due, and how a failure is told.
/// </summary>
internal static class UpdatePolicy
{
    /// <summary>The first background check waits this long after the start (doc 06 §5), so it never slows sign-in.</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);

    /// <summary>Background checks run this often (doc 06 §5).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    /// <summary>
    /// A check or download that failed for a passing reason (<see cref="UpdateFailure.Offline"/>,
    /// <see cref="UpdateFailure.RateLimited"/>) is tried again after this long instead of 12 h: right after sign-in or a
    /// wake the network is often not up yet, and GitHub's rate limit resets hourly. Other failures (a damaged release file,
    /// a full disk) wait the usual 12 h, since they would repeat, and each download starts the whole package over.
    /// </summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    /// <summary>A check gives up after this long (Velopack's own HTTP timeout is 30 minutes).</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A download gives up after this long without progress. HttpClient's timeout stops at the response headers, so a
    /// connection left half-open by a sleep or a network change would otherwise wait forever. Velopack reports progress
    /// only every 2-3 %, and applying deltas takes up to 5 min without any.
    /// </summary>
    public static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromMinutes(10);

    /// <summary>When the next background check is due.</summary>
    /// <param name="enabled">Whether background checks run: installed, and the AutoUpdate setting on.</param>
    /// <param name="phase">The current phase. Nothing is scheduled while busy or while an update waits for a restart.</param>
    /// <param name="lastAttempt">When the last check started (milliseconds of <see cref="Environment.TickCount64"/>), or null.</param>
    /// <param name="lastFailure">Why that check, or the download after it, failed; <see cref="UpdateFailure.None"/> if it didn't.</param>
    /// <param name="now">Now, on the same clock.</param>
    /// <returns>The time from now, or null for no background check.</returns>
    public static TimeSpan? NextCheckIn(bool enabled, UpdatePhase phase, long? lastAttempt, UpdateFailure lastFailure, long now)
    {
        if (!enabled || phase is UpdatePhase.NotInstalled or UpdatePhase.Unavailable or UpdatePhase.Checking
            or UpdatePhase.Downloading or UpdatePhase.Ready)
        {
            return null;
        }
        if (lastAttempt is not { } last) return FirstCheckDelay;
        bool passing = lastFailure is UpdateFailure.Offline or UpdateFailure.RateLimited;
        double due = last + (passing ? RetryInterval : CheckInterval).TotalMilliseconds - now;
        // At least FirstCheckDelay: turning AutoUpdate on long after the last check checks soon, never in a burst.
        return TimeSpan.FromMilliseconds(Math.Max(due, FirstCheckDelay.TotalMilliseconds));
    }

    /// <summary>Sorts a failure from Velopack or the network into what the user is told.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The kind.</returns>
    public static UpdateFailure Classify(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate) exception = aggregate.InnerExceptions[0];
        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => UpdateFailure.RateLimited,
            // No answer at all (StatusCode null: DNS, refused, reset, TLS) or a server error: GitHub can't be reached now.
            HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError } => UpdateFailure.Offline,
            TimeoutException or TaskCanceledException or SocketException => UpdateFailure.Offline,
            // The connection lost in the middle of a download: the body ended early, or the socket was reset. A disk
            // error has no SocketException inside, so it stays Other.
            HttpIOException or IOException { InnerException: SocketException } => UpdateFailure.Offline,
            _ => UpdateFailure.Other,
        };
    }

    /// <summary>What the user is told about a failure (a notification for the tray's check, the About page's line).</summary>
    /// <param name="failure">The kind.</param>
    /// <param name="whileDownloading">Whether it ended a download.</param>
    /// <returns>The text.</returns>
    public static string FailureMessage(UpdateFailure failure, bool whileDownloading) => failure switch
    {
        UpdateFailure.Offline when whileDownloading => "The update download stopped because GitHub couldn't be reached. Check your internet connection and try again.",
        UpdateFailure.Offline => "Couldn't reach GitHub to check for updates. Check your internet connection and try again.",
        UpdateFailure.RateLimited => "GitHub is limiting update checks from your network right now. Try again in an hour.",
        _ when whileDownloading => "Downloading the update failed. Open logs folder shows why.",
        _ => "Checking for updates failed. Open logs folder shows why.",
    };
}
