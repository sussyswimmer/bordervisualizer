using System.Net;
using System.Net.Http;
using Rimlight.Core;

namespace Rimlight.App.Updates;

/// <summary>What the update service tells the user (doc 06 §5), in one place. Versions are shown as Velopack formats them.</summary>
internal static class UpdateMessages
{
    /// <summary>"Check for updates" in a copy that isn't installed (a build run from source).</summary>
    /// <param name="version">The running version.</param>
    public static string NotInstalled(string version) =>
        $"Updates work only in an installed copy of {AppInfo.Name}. This one is version {version}, run from a build.";

    /// <summary>The check found nothing newer.</summary>
    /// <param name="version">The installed version.</param>
    public static string UpToDate(string version) => $"You're up to date (version {version}).";

    /// <summary>A newer version was found and is being downloaded.</summary>
    /// <param name="version">The new version.</param>
    public static string Downloading(string version) => $"Downloading version {version}…";

    /// <summary>The notification for a downloaded update; clicking it restarts the app into it ("Restart now").</summary>
    /// <param name="version">The new version.</param>
    public static string Ready(string version) =>
        $"Update ready — restart {AppInfo.Name} to apply.\nClick here to restart now (version {version}).";

    /// <summary>The same question in a message box, when the tray can't show a notification.</summary>
    /// <param name="version">The new version.</param>
    public static string ReadyQuestion(string version) =>
        $"Update ready — restart {AppInfo.Name} to apply.\n\nRestart now to install version {version}?";

    /// <summary>After Velopack restarted the app into a new version.</summary>
    /// <param name="version">The version now running.</param>
    public static string Updated(string version) => $"Updated to version {version}.";

    /// <summary>"Restart now" couldn't start Velopack's updater.</summary>
    public static string RestartFailed { get; } =
        $"The update couldn't be started. It will install the next time {AppInfo.Name} starts.";

    /// <summary>A short reason for a check that failed; the log has the details.</summary>
    /// <param name="exception">What the check threw.</param>
    /// <returns>One or two sentences.</returns>
    public static string CheckFailed(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } =>
            "GitHub is limiting update checks from this network right now. Try again in an hour.",
        HttpRequestException { StatusCode: { } status } =>
            $"Couldn't check for updates: GitHub answered with error {(int)status}. Try again later.",
        HttpRequestException or TimeoutException or TaskCanceledException =>
            "Couldn't reach GitHub to check for updates. Check the internet connection and try again.",
        _ => "Couldn't check for updates. Open the logs folder from the tray menu for details.",
    };

    /// <summary>A short reason for a download that failed.</summary>
    /// <param name="version">The version that was being downloaded.</param>
    /// <param name="exception">What the download threw.</param>
    /// <returns>One or two sentences.</returns>
    public static string DownloadFailed(string version, Exception exception) => exception switch
    {
        HttpRequestException or TimeoutException or TaskCanceledException =>
            $"Version {version} couldn't be downloaded. Check the internet connection and try again.",
        _ => $"Version {version} couldn't be downloaded. Try again later, or open the logs folder from the tray menu for details.",
    };
}
