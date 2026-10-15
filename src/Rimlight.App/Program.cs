using Rimlight.App.Updates;
using Velopack;

namespace Rimlight.App;

/// <summary>The entry point: Velopack first, then the WPF app.</summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Velopack must run first, here in Main (vpk refuses to pack an exe without this call in its entry point). When
        // the installer, an update or the uninstaller starts Rimlight.exe for one of its steps, the hook runs and the
        // process exits inside Run. A downloaded update is not applied here but by App, once this process holds the
        // single-instance claim, so a second start never stops the copy that runs (UpdateBootstrap.ApplyPending).
        var updates = new UpdateBootstrap();
        try
        {
            VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .SetLogger(new VelopackTraceLog())
                .OnBeforeUninstallFastCallback(UpdateBootstrap.OnUninstall)
                .OnRestarted(updates.OnRestarted)
                .Run();
        }
        catch (Exception exception)
        {
            // A process started only for an install, update or uninstall step must not become a normal start: during an
            // uninstall it would register "Launch at startup" again. Velopack ends a failed hook with -1 too.
            string[] args = Environment.GetCommandLineArgs(); // [0] is the exe; Velopack skips it too
            if (args.Length > 1 && (args[1].StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase)
                || args[1].StartsWith("--squirrel-", StringComparison.OrdinalIgnoreCase)))
            {
                return -1;
            }
            // The app still runs, without updates; logged once the log has started.
            updates.OnStartFailed(exception);
        }

        var app = new App(updates);
        app.InitializeComponent();
        return app.Run();
    }
}
