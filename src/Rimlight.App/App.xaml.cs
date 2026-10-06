using System.Diagnostics;
using System.Windows;
using Rimlight.App.Logging;
using Rimlight.Core;
using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>Tray application. Everything it runs is owned by <see cref="AppController"/>.</summary>
public partial class App : Application
{
    private SingleInstance? instance;
    private AppController? controller;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.CatchUnhandled(this);
        bool background = e.Args.Contains(StartupRegistration.BackgroundArgument, StringComparer.OrdinalIgnoreCase);

        // One copy per user session (doc 06 §4), decided before any tray icon exists: a second copy would otherwise
        // replace the first one's icon (H.NotifyIcon derives the icon's ID from the exe path). A start by hand asks the
        // running copy to show Settings; a start at sign-in just leaves.
        instance = SingleInstance.Claim(AppInfo.Name, background ? null : SingleInstance.ShowSettingsCommand);
        if (instance is null)
        {
            Shutdown();
            return;
        }

        AppLog.Start(e.Args); // only the running copy writes the log
        controller = new AppController(() => Shutdown(), instance, background);
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
        controller?.Flush();
        AppLog.Flush();
        base.OnSessionEnding(e);
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        controller = null;
        if (instance is not null) Trace.WriteLine("[App] Exited.");
        AppLog.Stop();
        instance?.Dispose(); // last: a new start can take over only once everything is released
        instance = null;
        base.OnExit(e);
    }
}
