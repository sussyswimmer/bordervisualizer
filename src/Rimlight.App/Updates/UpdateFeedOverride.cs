using System.IO;
using Rimlight.Core;

namespace Rimlight.App.Updates;

/// <summary>
/// The test feed, for testing updates without publishing a release: when the environment variable
/// <c>RIMLIGHT_UPDATE_FEED</c> names a folder of packed releases on this PC (<c>build/pack.ps1</c>'s
/// <c>artifacts/releases</c>), an installed copy reads its releases there instead of GitHub. Only the full path of an
/// existing folder on a local drive counts. Anything else (an http or https URL, a network share, a mapped network drive,
/// a relative path) is ignored, and releases come from GitHub: over plain http, the feed and the SHA-256 that Velopack
/// checks would arrive in the same unauthenticated response, and the GitHub check stays the only network request the app
/// makes. Pure code, compiled into Rimlight.Tests.
/// </summary>
internal static class UpdateFeedOverride
{
    /// <summary>The variable's name, <c>RIMLIGHT_UPDATE_FEED</c>.</summary>
    public static readonly string Variable = AppInfo.Name.ToUpperInvariant() + "_UPDATE_FEED";

    /// <summary>The folder to read releases from, or null to read them from GitHub.</summary>
    /// <param name="value">The variable's value; null when it isn't set.</param>
    /// <param name="isOnNetworkDrive">Whether a full path is on a network drive (<see cref="IsOnNetworkDrive"/>).</param>
    /// <param name="ignored">Why a value that is set isn't used; null when it is used or not set.</param>
    public static string? LocalFolder(string? value, Func<string, bool> isOnNetworkDrive, out string? ignored)
    {
        ignored = null;
        string feed = value?.Trim() ?? "";
        if (feed.Length == 0) return null;
        // A network share (\\server\share, or //server/share) is a full path on Windows; its folder isn't checked, so
        // nothing goes over the network.
        if (feed.StartsWith(@"\\", StringComparison.Ordinal) || feed.StartsWith("//", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(feed))
            ignored = "only the full path of a folder on this PC is used, not a URL or a network share";
        else if (isOnNetworkDrive(feed))
            ignored = "the folder is on a network drive";
        else if (!Directory.Exists(feed))
            ignored = "the folder doesn't exist";
        return ignored is null ? feed : null;
    }

    /// <summary>Whether a full path is on a network drive (a mapped share); true when that can't be told.</summary>
    /// <param name="path">A full path.</param>
    public static bool IsOnNetworkDrive(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
