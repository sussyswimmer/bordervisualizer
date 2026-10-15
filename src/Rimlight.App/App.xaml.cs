using System.Diagnostics;
using System.Windows;
using Rimlight.App.Logging;
using Rimlight.App.Updates;
using Rimlight.Core;
using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>Tray application, started by <see cref="Program"/>. Everything it runs is owned by <see cref="AppController"/>.</summary>
public partial class App : Application
{
    private readonly UpdateBootstrap updates;
    private SingleInstance? instance;
    private AppController? controller;

    /// <summary>Creates the app; <see cref="Program"/> then loads App.xaml and runs it.</summary>
    /// <param name="updates">What Velopack found at start, and the update manager.</param>
    internal App(UpdateBootstrap updates)
    {
        this.updates = updates;
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.CatchUnhandled(this);
        AppController.LaunchOptions options = AppController.ParseArguments(e.Args);

        // One copy per user session (doc 06 §4), decided before any tray icon exists: a second copy would otherwise
        // replace the first one's icon (H.NotifyIcon derives the icon's ID from the exe path). A start by hand asks the
        // running copy to show Settings (or the demo, or the visualizer); a start at sign-in just leaves.
        instance = SingleInstance.Claim(AppInfo.Name, options.Command);
        if (instance is null)
        {
            Shutdown();
            return;
        }

        AppLog.Start(e.Args); // only the running copy writes the log

        // An update downloaded in an earlier session installs now (doc 06 §5 "apply on next restart"): Velopack's updater
        // waits for this process to exit, so the claim is free again when it starts the new version.
        if (updates.ApplyPending(e.Args, options.Background))
        {
            Shutdown();
            return;
        }

        controller = new AppController(() => Shutdown(), instance, options, updates);
        try
        {
            controller.Start();
        }
        catch (Exception exception)
        {
            // Each part handles its own failures, so this is a bug. Keep running while the tray icon offers Quit;
            // without it the app could not be closed, so end it.
            Trace.WriteLine($"[App] Starting failed: {exception}");
            if (controller.Tray is null) Shutdown();
        }
    }

    /// <inheritdoc />
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows may end the process soon after; keep the last change and the last lines.
        Trace.WriteLine($"[App] Windows session ending ({e.ReasonSessionEnding}).");
        controller?.EndSession();
        AppLog.Flush();
        base.OnSessionEnding(e);
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        // First: a start during the teardown below then waits to take over instead of handing its request to this
        // process, which would drop it.
        instance?.StopListening();
        controller?.Dispose();
        controller = null;
        if (instance is not null) Trace.WriteLine("[App] Exited.");
        AppLog.Stop();
        instance?.Dispose(); // last: a new start can take over only once everything is released
        instance = null;
        base.OnExit(e);
    }
}
