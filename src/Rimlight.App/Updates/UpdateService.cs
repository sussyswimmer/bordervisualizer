using System.Diagnostics;
using System.Windows.Threading;
using Rimlight.App.Logging;
using Rimlight.Core;
using Velopack;

namespace Rimlight.App.Updates;

/// <summary>
/// Updates from GitHub Releases (doc 06 §5): a background check 30 s after the start and every 12 h while AutoUpdate
/// is on, the manual "Check for updates" (tray, About) at any time, a download in the background as soon as a newer
/// release is found, and the "Update ready" notification whose click restarts into the new version. Otherwise the
/// update installs at the next start (<see cref="UpdateBootstrap.ApplyPending"/>). The update check is the only
/// network request the app makes.
/// </summary>
/// <remarks>
/// Used on the UI thread. Velopack's calls run on the thread pool, so the UI never waits for the network or the disk;
/// their outcomes come back through the dispatcher. One check or download runs at a time. Failures of background
/// checks are logged once and shown to nobody.
/// </remarks>
internal sealed class UpdateService : IDisposable
{
    private readonly AppController app;
    private readonly UpdateBootstrap bootstrap;
    private readonly UpdateManager? manager; // null: not installed, or Velopack didn't start
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource stop = new();
    private bool autoUpdate;
    private long? lastAttempt; // Environment.TickCount64 when the last check started
    private bool lastFailed;
    private string? lastLoggedFailure; // a failure is logged once until something else happens
    private bool userWaiting; // the tray's check: its outcome is told in a notification
    private VelopackAsset? ready; // the downloaded release that Restart now installs
    private int progress; // the download's percent, written on the pool
    private int progressQueued;
    private bool restarting;
    private bool disposed;

    /// <summary>Reads what Velopack found at start; <see cref="Start"/> starts the checks.</summary>
    /// <param name="app">The app, for its tray notifications and Quit.</param>
    /// <param name="bootstrap">Velopack's start in Main and the update manager.</param>
    /// <param name="settings">The settings at start (AutoUpdate).</param>
    public UpdateService(AppController app, UpdateBootstrap bootstrap, Settings settings)
    {
        this.app = app;
        this.bootstrap = bootstrap;
        manager = bootstrap.Manager;
        autoUpdate = settings.AutoUpdate;
        timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += OnTimer;
        if (manager is null)
        {
            Status = new UpdateStatus(bootstrap.NotInstalled ? UpdatePhase.NotInstalled : UpdatePhase.Unavailable);
        }
        else if (bootstrap.PendingAtStart is { } pending)
        {
            // Downloaded earlier, and installing it at this start failed: offer it again, without a notification.
            ready = pending;
            Status = new UpdateStatus(UpdatePhase.Ready, pending.Version.ToString());
        }
        else
        {
            Status = new UpdateStatus(UpdatePhase.Idle);
        }
    }

    /// <summary>Where updates stand now.</summary>
    public UpdateStatus Status { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Status"/> changes.</summary>
    public event Action? StatusChanged;

    /// <summary>Schedules the first background check and says so if this start just installed an update.</summary>
    public void Start()
    {
        if (disposed) return;
        if (manager is not null && bootstrap.RestartedVersion is { } version && ready is null)
        {
            // Velopack restarted the app into the new version (Restart now, or the install at a start): the restart
            // opens no window, so say that it worked.
            Trace.WriteLine($"[Updates] Now running version {version}.");
            app.Tray?.Notify($"{AppInfo.Name} updated", $"You're now running version {version}.");
        }
        Schedule();
    }

    /// <summary>
    /// "Check for updates": checks now, downloads a newer release, and tells the outcome. Works with AutoUpdate off too.
    /// If an update is ready, shows its notification again instead.
    /// </summary>
    /// <param name="notify">
    /// True for the tray, which tells the outcome in a notification; false for the About page, which shows
    /// <see cref="Status"/> itself. "Update ready" is always notified.
    /// </param>
    public void CheckNow(bool notify)
    {
        if (disposed) return;
        if (manager is null)
        {
            if (notify) app.Tell(Status.Describe(AppInfo.Name));
            return;
        }
        switch (Status.Phase)
        {
            case UpdatePhase.Ready:
                ShowReady(userInitiated: true);
                return;
            case UpdatePhase.Checking:
                userWaiting |= notify; // told when it ends
                return;
            case UpdatePhase.Downloading:
                userWaiting |= notify;
                if (notify) app.Tell($"Version {Status.Version} is downloading. A notification says when it's ready.");
                return;
        }
        userWaiting = notify;
        StartCheck();
    }

    /// <summary>
    /// "Restart now": starts Velopack's updater, which waits for this process to end, installs the downloaded update
    /// and starts the new version in the background (<c>--background</c>, no window); then quits the app the usual
    /// way. The single-instance claim is released before the new version starts, since the updater waits for this
    /// process to exit.
    /// </summary>
    public void RestartToUpdate()
    {
        if (disposed || restarting || manager is null || ready is not { } update) return;
        try
        {
            // ApplyUpdatesAndRestart without its Environment.Exit: quitting below saves the settings, removes the tray
            // icon and stops the overlay first. The updater waits up to 60 s for this process.
            manager.WaitExitThenApplyUpdates(update, silent: false, restart: true, [AppController.BackgroundArgument]);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] Starting the updater failed: {exception}");
            app.Tell($"The update couldn't be installed now. It installs the next time {AppInfo.Name} starts.");
            return;
        }
        restarting = true;
        Trace.WriteLine($"[Updates] Restarting to install version {update.Version}.");
        app.Quit();
    }

    /// <summary>Follows the AutoUpdate setting: off stops background checks, on schedules the next one.</summary>
    /// <param name="settings">The new settings.</param>
    public void ApplySettings(Settings settings)
    {
        if (disposed || settings.AutoUpdate == autoUpdate) return;
        autoUpdate = settings.AutoUpdate;
        if (manager is not null) Trace.WriteLine($"[Updates] Background checks {(autoUpdate ? "on" : "off")}.");
        Schedule();
    }

    /// <summary>Stops the timer and any check or download; their late outcomes are dropped.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StatusChanged = null;
        timer.Stop();
        try
        {
            stop.Cancel(); // not disposed: a pool task may still read the token
        }
        catch (AggregateException exception)
        {
            // A cancellation callback inside Velopack or HttpClient threw; the app is exiting anyway.
            Trace.WriteLine($"[Updates] Cancelling the update work failed: {exception.Message}");
        }
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        timer.Stop();
        if (disposed || !autoUpdate || manager is null || Status.IsBusy || Status.Phase == UpdatePhase.Ready) return;
        userWaiting = false;
        StartCheck();
    }

    // Arms the timer for the next background check, or stops it.
    private void Schedule()
    {
        timer.Stop();
        TimeSpan? next = UpdatePolicy.NextCheckIn(autoUpdate && manager is not null, Status.Phase, lastAttempt, lastFailed,
            Environment.TickCount64);
        if (next is not { } delay) return;
        timer.Interval = delay;
        timer.Start();
    }

    private void StartCheck()
    {
        UpdateManager updates = manager!;
        lastAttempt = Environment.TickCount64;
        timer.Stop();
        SetStatus(new UpdateStatus(UpdatePhase.Checking));
        CancellationToken token = stop.Token;
        _ = Task.Run(() => CheckAndDownloadAsync(updates, token));
    }

    // Thread pool. Each outcome goes back to the UI thread.
    private async Task CheckAndDownloadAsync(UpdateManager updates, CancellationToken token)
    {
        UpdateInfo? found;
        try
        {
            Task<UpdateInfo?> check = updates.CheckForUpdatesAsync();
            // A check given up on (timeout, quit) may still fail later; observe that so it isn't reported as unobserved.
            _ = check.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            found = await check.WaitAsync(UpdatePolicy.CheckTimeout, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Post(() => OnFailed(exception, whileDownloading: false));
            return;
        }
        if (found is null)
        {
            Post(OnUpToDate);
            return;
        }

        VelopackAsset target = found.TargetFullRelease;
        Post(() => OnDownloading(target));
        try
        {
            // Velopack downloads deltas when it can, else the full package, and checks its SHA-256. A package already on
            // disk is not downloaded again.
            await updates.DownloadUpdatesAsync(found, OnProgress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Post(() => OnFailed(exception, whileDownloading: true));
            return;
        }
        Post(() => OnReady(target));
    }

    // Thread pool, at most every 2 %: the latest value is shown, one queued update at a time.
    private void OnProgress(int percent)
    {
        Volatile.Write(ref progress, percent);
        if (Interlocked.Exchange(ref progressQueued, 1) == 0) Post(ShowProgress);
    }

    private void ShowProgress()
    {
        Volatile.Write(ref progressQueued, 0);
        if (Status.Phase == UpdatePhase.Downloading) SetStatus(Status with { Percent = Volatile.Read(ref progress) });
    }

    private void OnUpToDate()
    {
        Succeeded();
        Trace.WriteLine($"[Updates] Up to date (version {AppLog.Version}).");
        SetStatus(new UpdateStatus(UpdatePhase.UpToDate));
        if (TakeUserWaiting()) app.Tell($"You're up to date. {AppInfo.Name} {AppLog.Version} is the newest version.");
        Schedule();
    }

    private void OnDownloading(VelopackAsset target)
    {
        Trace.WriteLine($"[Updates] Version {target.Version} is available; downloading it.");
        SetStatus(new UpdateStatus(UpdatePhase.Downloading, target.Version.ToString()));
        // The tray's check is answered now; "Update ready" follows when the download is done (or the failure, below).
        if (userWaiting) app.Tell($"Version {target.Version} is available and downloading. A notification says when it's ready.");
    }

    private void OnReady(VelopackAsset target)
    {
        Succeeded();
        ready = target;
        Trace.WriteLine($"[Updates] Version {target.Version} is downloaded; it installs at the next start, or now with Restart now.");
        SetStatus(new UpdateStatus(UpdatePhase.Ready, target.Version.ToString()));
        ShowReady(userInitiated: TakeUserWaiting());
        Schedule(); // no background checks while an update waits
    }

    private void OnFailed(Exception exception, bool whileDownloading)
    {
        lastFailed = true;
        UpdateFailure failure = UpdatePolicy.Classify(exception);
        string what = whileDownloading ? "Downloading the update" : "Checking for updates";
        // Offline and rate limits are expected and need no stack; anything else is logged in full, once.
        string detail = failure == UpdateFailure.Other ? exception.ToString() : $"{exception.GetType().Name}: {exception.Message}";
        string key = $"{whileDownloading}|{failure}|{exception.GetType().FullName}|{exception.Message}";
        if (key != lastLoggedFailure)
        {
            lastLoggedFailure = key;
            Trace.WriteLine($"[Updates] {what} failed ({failure}): {detail}. Until something changes, the same failure isn't logged again.");
        }
        SetStatus(new UpdateStatus(UpdatePhase.Failed, Failure: failure, WhileDownloading: whileDownloading));
        if (TakeUserWaiting()) app.Tell(UpdatePolicy.FailureMessage(failure, whileDownloading));
        Schedule(); // a background retry in 1 h
    }

    private void Succeeded()
    {
        lastFailed = false;
        lastLoggedFailure = null;
    }

    private bool TakeUserWaiting()
    {
        bool waiting = userWaiting;
        userWaiting = false;
        return waiting;
    }

    // The notification of doc 06 §5. A click restarts into the update; the tray menu and the About page offer it too,
    // since a click on it in the notification center does nothing.
    private void ShowReady(bool userInitiated)
    {
        if (ready is not { } update) return;
        app.Tray?.Notify($"Update ready — restart {AppInfo.Name} to apply.",
            $"Version {update.Version} is downloaded. Click here to restart now, or it installs the next time {AppInfo.Name} starts.",
            userInitiated, onClick: RestartToUpdate);
    }

    private void SetStatus(UpdateStatus status)
    {
        if (status == Status) return;
        Status = status;
        try
        {
            StatusChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] Showing the update status failed: {exception}");
        }
    }

    // To the UI thread; dropped once disposed (the dispatcher drops it too once it has shut down).
    private void Post(Action action) => dispatcher.InvokeAsync(() =>
    {
        if (disposed) return;
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Updates] Handling an update result failed: {exception}");
        }
    });
}
