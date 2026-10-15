using System.Diagnostics;
using System.IO;
using Rimlight.Core;
using Rimlight.Platform.Shell;
using Velopack;
using Velopack.Sources;

namespace Rimlight.App.Updates;

/// <summary>
/// What Velopack reported in <see cref="Program"/>'s Main, the update manager the app uses afterwards (doc 06 §5), and
/// the start-up half of "apply on next restart": an update downloaded in an earlier session installs when the app next
/// starts. Used on the UI thread; the hooks run in Main before WPF starts.
/// </summary>
internal sealed class UpdateBootstrap
{
    /// <summary>Where releases come from: the project's GitHub Releases, stable ones only.</summary>
    public const string RepositoryUrl = "https://github.com/sussyswimmer/bordervisualizer";

    private UpdateManager? manager;
    private bool managerCreated;

    /// <summary>
    /// The version running when Velopack restarted the app after installing an update (or after failing to); null for
    /// any other start.
    /// </summary>
    public SemanticVersion? RestartedVersion { get; private set; }

    /// <summary>Why Velopack failed to start, if it did; the app then runs without updates.</summary>
    public Exception? StartError { get; private set; }

    /// <summary>
    /// The update that was already downloaded when this session started and was not installed then (see
    /// <see cref="ApplyPending"/>), or null.
    /// </summary>
    public VelopackAsset? PendingAtStart { get; private set; }

    /// <summary>True when this copy is not an installed one (a build run from source): it never checks.</summary>
    public bool NotInstalled { get; private set; }

    /// <summary>
    /// The update manager for <see cref="RepositoryUrl"/>, or null when this copy isn't installed or Velopack failed to
    /// start. Created on first use, without network or file access.
    /// </summary>
    public UpdateManager? Manager
    {
        get
        {
            if (!managerCreated)
            {
                managerCreated = true;
                manager = CreateManager();
            }
            return manager;
        }
    }

    /// <summary>Velopack's "restarted" hook (Main): Velopack started this process after applying an update.</summary>
    /// <param name="version">The version now running.</param>
    public void OnRestarted(SemanticVersion version) => RestartedVersion = version;

    /// <summary>Velopack's start threw (Main); updates are off for this session.</summary>
    /// <param name="exception">The failure.</param>
    public void OnStartFailed(Exception exception) => StartError = exception;

    /// <summary>
    /// Velopack's uninstall hook, run in a process of its own before the files go: removes the "Launch at startup" entry,
    /// which would otherwise start a launcher that no longer exists. Settings (<c>%APPDATA%\Rimlight</c>) and logs stay
    /// (doc 07 Phase 6). Velopack ends the process after 30 s at the latest.
    /// </summary>
    /// <param name="version">The version being removed.</param>
    public static void OnUninstall(SemanticVersion version) => StartupRegistration.Remove(AppInfo.Name);

    /// <summary>
    /// "Apply on next restart" (doc 06 §5): if an update was downloaded earlier, starts Velopack's updater, which waits for
    /// this process to exit, installs the update and starts the new version with the same arguments. Call once this
    /// process holds the single-instance claim, before anything else starts, so a second start never stops the copy that
    /// runs. Never throws.
    /// </summary>
    /// <param name="arguments">This start's arguments, passed to the new version.</param>
    /// <param name="background">Whether this is a start at sign-in: the updater then shows no window.</param>
    /// <returns>True when the updater was started and this process should exit now.</returns>
    public bool ApplyPending(string[] arguments, bool background)
    {
        if (Manager is not { } updates) return false;
        try
        {
            PendingAtStart = updates.UpdatePendingRestart;
            if (PendingAtStart is not { } pending) return false;
            if (RestartedVersion is not null)
            {
                // Velopack just restarted the app, and the update is still waiting: installing it failed. Not again now,
                // which could loop; it stays ready for Restart now or the next start.
                Trace.WriteLine($"[Updates] Version {pending.Version} was downloaded but did not install; it stays ready.");
                return false;
            }
            Trace.WriteLine($"[Updates] Installing version {pending.Version}, downloaded earlier, before starting.");
            updates.WaitExitThenApplyUpdates(pending, silent: background, restart: true, arguments);
            return true;
        }
        catch (Exception exception)
        {
            // The app starts as it is; the update stays downloaded.
            Trace.WriteLine($"[Updates] Installing the downloaded update failed, starting without it: {exception.Message}");
            return false;
        }
    }

    private UpdateManager? CreateManager()
    {
        if (StartError is not null)
        {
            Trace.WriteLine($"[Updates] Velopack failed to start, so this session doesn't update: {StartError}");
            return null;
        }
        try
        {
            // Stable releases only, without a token: GitHub allows 60 API requests an hour per IP address, and a check
            // makes one. The installed copy reads its own channel's feed (win or win-arm64, C9). The test feed is a local
            // folder only, never a URL (UpdateFeedOverride).
            string variable = UpdateFeedOverride.Variable;
            string? value = Environment.GetEnvironmentVariable(variable);
            string? folder = UpdateFeedOverride.LocalFolder(value, UpdateFeedOverride.IsOnNetworkDrive, out string? ignored);
            if (ignored is not null) Trace.WriteLine($"[Updates] {variable} ('{value}') is ignored: {ignored}. Releases come from GitHub.");
            var updates = folder is null
                ? new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false))
                : new UpdateManager(new SimpleFileSource(new DirectoryInfo(folder)));
            if (folder is not null) Trace.WriteLine($"[Updates] Releases come from the folder in {variable}: {folder}");
            if (updates.IsInstalled) return updates;
            NotInstalled = true;
            Trace.WriteLine("[Updates] This copy isn't installed (it runs from a build folder), so it doesn't check for updates.");
            return null;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] The update manager could not be created, so this session doesn't update: {exception}");
            return null;
        }
    }
}
