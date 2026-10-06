using System.Windows;

namespace Rimlight.App;

/// <summary>Tray application. Everything it runs is owned by <see cref="AppController"/>.</summary>
public partial class App : Application
{
    private AppController? controller;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        controller = new AppController(() => Shutdown());
        controller.Start();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        base.OnExit(e);
    }
}
