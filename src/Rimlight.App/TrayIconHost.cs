using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Rimlight.Core;
using Rimlight.Platform;

namespace Rimlight.App;

/// <summary>
/// Owns the notification-area icon. K0/K1: placeholder icon, tooltip, Quit and (Debug builds) a device-loss test item.
/// The full menu (doc 06 §2) arrives in K6.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon icon;

    public TrayIconHost(Action onQuit, Action onSimulateDeviceLoss)
    {
        var quit = new MenuItem { Header = "Quit " + AppInfo.Name };
        quit.Click += (_, _) => onQuit();
        var menu = new ContextMenu();
#if DEBUG
        // Debug builds only: exercises the GPU device-loss recovery path (doc 07 Phase 1).
        var deviceLoss = new MenuItem { Header = "Simulate GPU device loss (debug)" };
        deviceLoss.Click += (_, _) => onSimulateDeviceLoss();
        menu.Items.Add(deviceLoss);
        menu.Items.Add(new Separator());
#else
        _ = onSimulateDeviceLoss;
#endif
        menu.Items.Add(quit);

        icon = new TaskbarIcon
        {
            // Built here rather than in a static field: the pack: scheme only parses once the WPF Application exists.
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Rimlight.ico", UriKind.Absolute)),
            ToolTipText = BuildToolTip(),
            ContextMenu = menu,
        };

        // The icon lives outside any visual tree, so it must be created explicitly. Keep Efficiency Mode off:
        // H.NotifyIcon's default puts the whole process on EcoQoS and Idle priority class, which would starve
        // the render thread and audio capture.
        icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Dispose() => icon.Dispose();

    private static string BuildToolTip()
    {
        string text = AppInfo.Name + " — Waiting for music";

        // Visible check that the manifest took effect; without Per-Monitor V2 the overlay is misplaced on mixed-DPI setups.
        return DpiAwareness.IsPerMonitorV2() ? text : text + " (warning: not Per-Monitor V2 DPI aware)";
    }
}
