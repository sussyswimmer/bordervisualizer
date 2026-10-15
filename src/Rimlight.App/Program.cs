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
            // The app still runs, without updates; logged once the log has started.
            updates.OnStartFailed(exception);
        }

        var app = new App(updates);
        app.InitializeComponent();
        return app.Run();
    }
}
