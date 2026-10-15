using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Displays (doc 06 §3.4): all, the main display, or a toggle per monitor named as Windows names it (e.g. "DELL
/// U2720Q", else "Display N"), and the frame rate limit. Custom choices store monitor IDs (H-009). The list is read
/// when the page opens and again after a display change while it is open.
/// </summary>
internal partial class DisplaysPage : UserControl, ISettingsPage
{
    private static readonly TimeSpan DisplayChangeDelay = TimeSpan.FromMilliseconds(500); // Windows sends several

    private readonly PageEdits edits;
    private readonly DispatcherTimer displayChange;
    private readonly List<(MonitorInfo Monitor, ToggleSwitch Toggle)> rows = [];
    private IReadOnlyList<MonitorInfo> monitors = [];
    private bool active;

    public DisplaysPage(AppController app)
    {
        edits = new PageEdits(app);
        InitializeComponent();
        displayChange = new DispatcherTimer(DispatcherPriority.Background) { Interval = DisplayChangeDelay };
        displayChange.Tick += (_, _) =>
        {
            displayChange.Stop();
            if (!active) return;
            ReadMonitors();
            Refresh(edits.Current);
        };
    }

    public void Refresh(Settings settings) => edits.Refresh(() =>
    {
        AllChoice.IsChecked = settings.Monitors == MonitorSelection.All;
        PrimaryChoice.IsChecked = settings.Monitors == MonitorSelection.PrimaryOnly;
        CustomChoice.IsChecked = settings.Monitors == MonitorSelection.Custom;
        int lit = 0;
        foreach ((MonitorInfo monitor, ToggleSwitch toggle) in rows)
        {
            bool on = MonitorChoice.IsLit(settings, monitor);
            toggle.IsChecked = on;
            if (on) lit++;
        }
        int remembered = settings.Monitors == MonitorSelection.Custom
            ? (settings.CustomMonitorIds ?? []).Count(id => !string.IsNullOrEmpty(id) && !monitors.Any(m => m.StableId == id))
            : 0;
        PageEdits.SetMessage(SelectionNote, Note(lit, remembered));

        FpsBox.SelectedIndex = -1;
        for (int i = 0; i < FpsBox.Items.Count; i++)
        {
            if (FpsBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == settings.FpsCap.ToString(CultureInfo.InvariantCulture))
                FpsBox.SelectedIndex = i;
        }
    });

    public void SetActive(bool active)
    {
        this.active = active;
        if (!active)
        {
            displayChange.Stop();
            return;
        }
        ReadMonitors(); // the window shows the settings right after this
    }

    /// <summary>Windows reported a display change (WM_DISPLAYCHANGE): re-read the monitors shortly, if on screen.</summary>
    public void OnDisplaysChanged()
    {
        if (!active) return;
        displayChange.Stop();
        displayChange.Start();
    }

    private static string Note(int lit, int remembered)
    {
        if (lit == 0) return "No display is chosen, so the glow isn't shown anywhere.";
        if (remembered == 1) return "One chosen display isn't connected right now. It lights up again when you plug it back in.";
        if (remembered > 1) return $"{remembered} chosen displays aren't connected right now. They light up again when you plug them back in.";
        return "";
    }

    // The attached monitors, named and in order, each with its toggle.
    private void ReadMonitors()
    {
        IReadOnlyList<MonitorInfo> next = DisplayMonitors.Describe();
        if (next.SequenceEqual(monitors)) return;
        monitors = next;
        rows.Clear();
        MonitorList.Children.Clear();
        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorInfo monitor = monitors[i];
            string name = monitor.FriendlyName ?? (monitor.IsBuiltIn ? "Built-in display" : $"Display {i + 1}");
            var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            AutomationProperties.SetName(toggle, $"Show the glow on {name}");
            AutomationProperties.SetHelpText(toggle, Details(monitor, i));
            toggle.Checked += (_, _) => edits.Update(s => MonitorChoice.Toggle(s, monitors, monitor, lit: true));
            toggle.Unchecked += (_, _) => edits.Update(s => MonitorChoice.Toggle(s, monitors, monitor, lit: false));
            DockPanel.SetDock(toggle, Dock.Right);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = name, Style = (Style)FindResource("RowLabel") });
            text.Children.Add(new TextBlock { Text = Details(monitor, i), Style = (Style)FindResource("RowDescription") });
            var panel = new DockPanel();
            panel.Children.Add(toggle);
            panel.Children.Add(text);
            MonitorList.Children.Add(new Border { Child = panel, Style = (Style)FindResource("Row") });
            rows.Add((monitor, toggle));
        }
    }

    // "Display 2 · 2560 × 1440 · 144 Hz · Main display": the number tells two monitors of the same model apart.
    private static string Details(MonitorInfo monitor, int index)
    {
        var parts = new List<string> { $"Display {index + 1}", $"{monitor.Width} × {monitor.Height}" };
        if (monitor.RefreshHz > 0) parts.Add($"{monitor.RefreshHz} Hz");
        if (monitor.IsPrimary) parts.Add("Main display");
        return string.Join(" · ", parts);
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, AllChoice)) edits.Update(s => s with { Monitors = MonitorSelection.All });
        else if (ReferenceEquals(sender, PrimaryChoice)) edits.Update(s => s with { Monitors = MonitorSelection.PrimaryOnly });
        else edits.Update(s => MonitorChoice.ChooseDisplays(s, monitors));
    }

    private void OnFpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FpsBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap)) return;
        edits.Update(s => s with { FpsCap = cap });
    }
}
