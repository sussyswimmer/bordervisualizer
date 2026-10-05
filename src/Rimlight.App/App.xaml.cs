using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using Rimlight.Core;

namespace Rimlight.App;

/// <summary>Minimal tray-only application for the K0 scaffold.</summary>
public partial class App : Application
{
    private TaskbarIcon? trayIcon;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var quit = new MenuItem { Header = "Quit " + AppInfo.Name };
        quit.Click += (_, _) => Shutdown();
        var menu = new ContextMenu();
        menu.Items.Add(quit);

        var icon = new GeneratedIconSource
        {
            Text = "R",
            Background = Brushes.MediumPurple,
            Foreground = Brushes.White
        };
        trayIcon = new TaskbarIcon
        {
            IconSource = icon,
            ToolTipText = AppInfo.Name + " — Waiting for music",
            ContextMenu = menu
        };
        trayIcon.ForceCreate();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        trayIcon?.Dispose();
        base.OnExit(e);
    }
}
