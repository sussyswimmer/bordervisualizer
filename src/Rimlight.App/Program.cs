using Rimlight.App.Updates;
using Rimlight.Core;
using Rimlight.Platform.Shell;
using Velopack;

namespace Rimlight.App;

/// <summary>
/// The entry point. Velopack's startup code runs first, before WPF, the single-instance claim, the log or the tray icon
/// exist: when Velopack's installer or updater starts the exe to run a hook (install, update, uninstall), <c>Run()</c>
/// handles it and ends the process right there (HANDOFF H-014). Every other start goes on to the WPF app.
/// </summary>
internal static class Program
{
    /// <summary>Runs Velopack's startup code, then the app.</summary>
    [STAThread]
    public static void Main()
    {
        try
        {
            // vpk's pack check looks for this call in Main itself, so it stays here rather than in a helper.
            VelopackApp.Build()
                // A downloaded update is installed by App.OnStartup once this process holds the single-instance claim,
                // not here: Velopack's updater ends every process running from the install folder, so a second start
                // (the Start menu while the app runs) would kill the running copy before it saves or removes its icon.
                .SetAutoApplyOnStartup(false)
                // Uninstall removes Velopack's own shortcuts, uninstall entry and folder; the "Launch at startup" Run
                // value is ours. A fast callback: Velopack ends the process right after it, within 30 s.
                .OnBeforeUninstallFastCallback(_ => StartupRegistration.Remove(AppInfo.Name))
                // Started again by Velopack's updater after it installed an update, or failed to.
                .OnRestarted(UpdateService.OnRestarted)
                .Run();
        }
        catch (Exception exception)
        {
            // Velopack handles its own failures, so this is a bug. Better the glow without updates than no app; the
            // log isn't open yet, so the update service logs it later.
            UpdateService.OnStartupFailed(exception);
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
