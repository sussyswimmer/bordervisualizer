using System.Diagnostics;
using System.Windows;
using Rimlight.App.Logging;
using Rimlight.Core;
using Rimlight.Platform.Shell;
using Velopack;
using Velopack.Sources;

namespace Rimlight.App.Updates;

/// <summary>
/// Updates from GitHub Releases with Velopack (doc 06 §5). While Settings > Behavior > "Update automatically" is on it
/// checks 30 s after start and then every 12 h (<see cref="UpdateSchedule"/>), downloads a newer release in the
/// background and says so with a tray notification whose click offers to restart into it ("Restart now"), as the tray
/// menu's "Restart to update" does while it waits; otherwise the update is installed at the next start
/// (<see cref="InstallDownloadedUpdate"/>). "Check for updates" (the tray, Settings > About) works with the setting off
/// too and reports every step. Only an installed copy checks, and the check is the app's only network call.
/// </summary>
/// <remarks>
/// Created, called and disposed on the UI thread. Velopack's disk and network work runs on the thread pool and its
/// results come back to the UI thread (the awaits resume on the dispatcher). Failures, offline and GitHub's rate limit
/// included, are logged and never thrown; an automatic check fails silently and is retried an hour later.
/// </remarks>
internal sealed class UpdateService : IUpdateCheck, IDisposable
{
    /// <summary>The repository whose releases installed copies read; each install reads its own channel's feed (HANDOFF H-014).</summary>
    public const string RepositoryUrl = "https://github.com/sussyswimmer/bordervisualizer";

    // Velopack's own HTTP timeout is 30 minutes; a check that takes longer than this is reported as failed (and left to
    // end on the pool). The download has no such limit: 75 MB can take a while.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(1);

    // Task.Delay doesn't count time the computer sleeps, so a long wait is cut into pieces of at most this, after each
    // of which the schedule's clock (which does count it) is read again.
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);

    private static SemanticVersion? restartedAs; // set by Velopack's restarted hook in Program.Main, before WPF exists
    private static Exception? startupFailure;

    private readonly AppController app;
    private readonly UpdateManager? manager; // null when this copy isn't installed (or Velopack failed)
    private readonly UpdateSchedule schedule = new();
    private readonly CancellationTokenSource stop = new(); // on Dispose: the automatic checks and a download
    private SettingsService? settings;
    private CancellationTokenSource? automatic; // the automatic checks, while "Update automatically" is on
    private bool busy; // a check (and its download) is running
    private bool reportBusy; // the user asked for the running check's outcome
    private SemanticVersion? downloading;
    private VelopackAsset? ready; // downloaded: installed at the next start, or now by "Restart now"
    private SemanticVersion? announced; // the ready version the user has been told about
    private bool asking; // the "Restart now?" question is open
    private bool disposed;

    /// <summary>Creates the service; <see cref="Start"/> starts the automatic checks.</summary>
    /// <param name="app">The app, for its tray notifications, settings and Quit.</param>
    public UpdateService(AppController app)
    {
        this.app = app;
        manager = CreateManager();
    }

    /// <summary>
    /// Velopack's restarted hook (<see cref="VelopackApp.OnRestarted"/>): Velopack's updater started this process after
    /// installing an update, or after failing to. Runs inside <c>VelopackApp.Run()</c>, before WPF exists, so it only
    /// takes note.
    /// </summary>
    /// <param name="version">The installed version now running.</param>
    public static void OnRestarted(SemanticVersion version) => restartedAs = version;

    /// <summary>Notes that Velopack's startup code threw; the update service logs it once the log is open.</summary>
    /// <param name="exception">What it threw.</param>
    public static void OnStartupFailed(Exception exception) => startupFailure = exception;

    /// <inheritdoc />
    public string? ReadyVersion => disposed ? null : ready?.Version.ToString();

    /// <summary>
    /// Installs an update that an earlier run downloaded ("apply on the next restart", doc 06 §5): starts Velopack's
    /// updater, which waits for this process to end, installs the update and starts the new version with the same
    /// arguments. Call once at startup, after the single-instance claim (a second start never gets here) and before
    /// anything else starts; when it returns true, end the app at once. A start with <c>--background</c> (sign-in)
    /// shows none of the updater's windows. Velopack's own startup code doesn't do this (Program.Main).
    /// </summary>
    /// <param name="args">This start's command line, passed on to the new version.</param>
    /// <returns>True when the updater was started and the app should exit; false to start normally.</returns>
    public static bool InstallDownloadedUpdate(string[] args)
    {
        // Started by the updater: the update was just installed, or installing it failed. Trying again here could loop.
        if (restartedAs is not null) return false;
        try
        {
            if (CreateManager() is not { } manager || manager.UpdatePendingRestart is not { } update) return false;
            bool background = args.Contains(StartupRegistration.BackgroundArgument, StringComparer.OrdinalIgnoreCase);
            Trace.WriteLine($"[Updates] Installing version {update.Version}, downloaded earlier, before starting.");
            manager.WaitExitThenApplyUpdates(update, silent: background, restart: true, restartArgs: args);
            return true;
        }
        catch (Exception exception)
        {
            // Start this version instead; the update service offers the downloaded one again.
            Trace.WriteLine($"[Updates] Installing the downloaded update failed: {exception}");
            return false;
        }
    }

    /// <summary>Starts the automatic checks if the setting is on and follows the setting from now on. UI thread, once.</summary>
    /// <param name="settings">The app's settings.</param>
    public void Start(SettingsService settings)
    {
        if (startupFailure is not null) Trace.WriteLine($"[Updates] Velopack's startup code failed: {startupFailure}");
        if (manager is null)
        {
            Trace.WriteLine("[Updates] This copy isn't installed: no update checks.");
            return;
        }
        Trace.WriteLine($"[Updates] Installed version {manager.CurrentVersion}, updates from {RepositoryUrl}.");
        this.settings = settings;
        settings.Changed += OnSettingsChanged;
        SetAutomatic(settings.Current.AutoUpdate);
        _ = ReadDownloadedAsync(manager);
    }

    /// <summary>
    /// "Check for updates" (doc 06 §5), also with "Update automatically" off: checks now, or reports the check or
    /// download already running, or offers the downloaded update again. Each step is reported with a tray notification
    /// (a message box if the tray can't show one).
    /// </summary>
    public void CheckNow()
    {
        if (disposed) return;
        if (manager is null)
        {
            app.Tell(UpdateMessages.NotInstalled(AppLog.Version));
            return;
        }
        if (busy)
        {
            // The running check reports its outcome; a download in progress says so now.
            reportBusy = true;
            if (downloading is { } version) app.Tell(UpdateMessages.Downloading(version.ToString()));
            return;
        }
        if (ready is { } update)
        {
            // Downloaded already: no need to ask GitHub, offer the restart again.
            AnnounceReady(update, userAsked: true);
            return;
        }
        _ = CheckAsync(manager, report: true);
    }

    /// <summary>Stops the automatic checks and a download. Doesn't wait for a request already sent; its outcome is dropped.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (settings is not null) settings.Changed -= OnSettingsChanged;
        automatic?.Cancel();
        automatic = null;
        // Not disposed: a download on the pool may still look at its token.
        stop.Cancel();
    }

    // The installed copy's update manager, or null when this copy isn't installed (a build run from source) or Velopack
    // isn't working. Reads only local files; no network.
    private static UpdateManager? CreateManager()
    {
        try
        {
            // No explicit channel: each install reads its own channel's feed (win for x64, win-arm64 for ARM64), and
            // prereleases are skipped (doc 06 §5).
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            return manager.IsInstalled ? manager : null;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] Velopack isn't available: {exception.Message}");
            return null;
        }
    }

    // UI thread, inside SettingsService.Update.
    private void OnSettingsChanged(Settings snapshot) => SetAutomatic(snapshot.AutoUpdate);

    // "Update automatically" on: the checks (re)start, the first one 30 s from now unless one ran within 12 h. Off: they
    // stop (a check already running finishes).
    private void SetAutomatic(bool enabled)
    {
        if (disposed || manager is null || enabled == (automatic is not null)) return;
        if (!enabled)
        {
            automatic!.Cancel();
            automatic = null;
            Trace.WriteLine("[Updates] Automatic update checks off.");
            return;
        }
        automatic = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        schedule.Start(Environment.TickCount64);
        _ = RunAutomaticAsync(manager, automatic);
    }

    // The automatic checks, on the UI thread between waits, until "Update automatically" is turned off or the app exits.
    private async Task RunAutomaticAsync(UpdateManager manager, CancellationTokenSource cancel)
    {
        try
        {
            while (true)
            {
                TimeSpan wait = schedule.UntilNext(Environment.TickCount64);
                if (wait == TimeSpan.Zero && !busy)
                {
                    await CheckAsync(manager, report: false);
                    continue;
                }
                // Due while the user's check (or a long download) still runs: that one counts; look again later.
                if (wait == TimeSpan.Zero) wait = MaxWait;
                await Task.Delay(wait < MaxWait ? wait : MaxWait, cancel.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Turned off, or the app is exiting.
        }
        finally
        {
            cancel.Dispose();
        }
    }

    // One check, and the download of a newer version. UI thread between the awaits; never throws.
    private async Task CheckAsync(UpdateManager manager, bool report)
    {
        busy = true;
        reportBusy = report;
        schedule.Checking(Environment.TickCount64);
        try
        {
            UpdateInfo? update;
            try
            {
                update = await Task.Run(() => manager.CheckForUpdatesAsync()).WaitAsync(CheckTimeout);
            }
            catch (Exception exception)
            {
                if (disposed) return;
                schedule.Failed();
                Trace.WriteLine($"[Updates] Checking for updates failed: {exception.GetType().Name}: {exception.Message}");
                if (reportBusy) app.Tell(UpdateMessages.CheckFailed(exception));
                return;
            }
            if (disposed) return;
            string current = manager.CurrentVersion?.ToString() ?? AppLog.Version;
            if (update is null)
            {
                Trace.WriteLine($"[Updates] Up to date ({current}).");
                if (reportBusy) app.Tell(UpdateMessages.UpToDate(current));
                return;
            }

            VelopackAsset target = update.TargetFullRelease;
            string version = target.Version.ToString();
            downloading = target.Version;
            Trace.WriteLine($"[Updates] Version {version} is available (running {current}); downloading it.");
            if (reportBusy) app.Tell(UpdateMessages.Downloading(version));
            try
            {
                // A version downloaded earlier is found on disk and not downloaded again.
                await Task.Run(() => manager.DownloadUpdatesAsync(update, null, stop.Token));
            }
            catch (Exception exception)
            {
                if (disposed) return;
                Trace.WriteLine($"[Updates] Downloading version {version} failed: {exception.GetType().Name}: {exception.Message}");
                if (reportBusy) app.Tell(UpdateMessages.DownloadFailed(version, exception));
                return;
            }
            if (disposed) return;
            ready = target;
            Trace.WriteLine($"[Updates] Version {version} is downloaded; it installs at the next start or with Restart now.");
            // An automatic check tells about each version once; the user's own check always answers.
            if (reportBusy || announced != target.Version) AnnounceReady(target, userAsked: reportBusy);
        }
        finally
        {
            busy = false;
            reportBusy = false;
            downloading = null;
        }
    }

    // "Update ready — restart Rimlight to apply." A click on the notification asks whether to restart into the update
    // now. When the tray can't show it, the user's own check asks with a message box at once; an automatic one only
    // logs. The tray menu offers "Restart to update" either way.
    private void AnnounceReady(VelopackAsset update, bool userAsked)
    {
        string version = update.Version.ToString();
        if (app.Tray?.Notify(AppInfo.Name, UpdateMessages.Ready(version), userInitiated: userAsked, clicked: AskToRestart) == true)
        {
            announced = update.Version;
            return;
        }
        if (!userAsked)
        {
            Trace.WriteLine("[Updates] The tray couldn't show the update notification.");
            return;
        }
        announced = update.Version;
        AskToRestart();
    }

    // "Restart now?"; Yes restarts into the update. A click on a notification only asks: the shell doesn't say which
    // notification was clicked, so it may be an older one still in Windows' list (K7's "still running in the tray").
    private void AskToRestart()
    {
        if (disposed || asking || ready is not { } update) return;
        asking = true; // the message box runs a message loop, where another click can arrive
        try
        {
            MessageBoxResult answer = MessageBox.Show(UpdateMessages.ReadyQuestion(update.Version.ToString()), AppInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) RestartToUpdate();
        }
        finally
        {
            asking = false;
        }
    }

    /// <summary>
    /// "Restart now" (Yes to the notification's question, the tray menu's "Restart to update"): saves the settings,
    /// starts Velopack's updater, which waits for this process to end (up to 60 s), installs the update and starts the
    /// new version in the tray, then quits normally. The quit disposes everything and releases the single-instance claim
    /// before the new version starts. Does nothing without a downloaded update.
    /// </summary>
    public void RestartToUpdate()
    {
        if (disposed || manager is null || ready is not { } update) return;
        Trace.WriteLine($"[Updates] Restarting to install version {update.Version}.");
        settings?.Flush();
        try
        {
            manager.WaitExitThenApplyUpdates(update, silent: false, restart: true,
                restartArgs: [StartupRegistration.BackgroundArgument]);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] Starting Velopack's updater failed: {exception}");
            app.Tell(UpdateMessages.RestartFailed);
            return;
        }
        // The updater has it now. The quit is posted to the dispatcher, and the question and the tray menu can both be
        // open: a second Yes or click before it runs starts no second updater.
        ready = null;
        app.Quit();
    }

    // At start: an update downloaded by an earlier run that isn't installed (it was offered again: the updater failed),
    // or the update that the updater just installed ("Updated to version X").
    private async Task ReadDownloadedAsync(UpdateManager manager)
    {
        VelopackAsset? pending;
        try
        {
            pending = await Task.Run(() => manager.UpdatePendingRestart); // reads the local packages
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] Reading the downloaded updates failed: {exception.Message}");
            return;
        }
        if (disposed) return;
        if (pending is not null)
        {
            ready ??= pending;
            Trace.WriteLine(restartedAs is not null
                ? $"[Updates] Velopack's updater restarted the app, but version {pending.Version} isn't installed."
                : $"[Updates] Version {pending.Version} is downloaded but not installed.");
        }
        else if (restartedAs is { } version)
        {
            Trace.WriteLine($"[Updates] Updated to version {version}.");
            app.Tray?.Notify(AppInfo.Name, UpdateMessages.Updated(version.ToString()));
        }
    }
}
