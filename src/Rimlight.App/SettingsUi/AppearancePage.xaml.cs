using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Appearance (doc 06 §3.1): the live preview, the shape sliders (ranges from doc 06 §1), Cover taskbar, and the
/// presets as color chips (<see cref="Presets.All"/>; the section is hidden while the catalog is empty).
/// </summary>
internal partial class AppearancePage : UserControl, ISettingsPage
{
    private readonly AppController app;
    private readonly PageEdits edits;
    private readonly GlowPreview preview;
    private readonly List<(Button Chip, Func<Settings, Settings> Apply, Shape Primary, Shape Secondary, string Name)> chips = [];

    public AppearancePage(AppController app)
    {
        this.app = app;
        edits = new PageEdits(app);
        InitializeComponent();
        preview = new GlowPreview(app, PreviewImage);
        BuildPresets();
    }

    public void Refresh(Settings settings) => edits.Refresh(() =>
    {
        PageEdits.Set(ThicknessSlider, settings.CoreThicknessDip);
        PageEdits.Set(GlowSlider, settings.Glow * 100);
        PageEdits.Set(BrightnessSlider, settings.Brightness * 100);
        PageEdits.Set(CornerSlider, settings.CornerRadiusDip);
        CoverTaskbarSwitch.IsChecked = settings.CoverTaskbar;
        ShowValues(settings);
        PreviewCaption.Text = Caption(settings);
        RefreshPresets(settings);
    });

    public void SetActive(bool active)
    {
        if (active) preview.Start();
        else preview.Stop();
    }

    private void ShowValues(Settings settings)
    {
        ThicknessValue.Text = Pixels(settings.CoreThicknessDip);
        GlowValue.Text = Percent(settings.Glow);
        BrightnessValue.Text = Percent(settings.Brightness);
        CornerValue.Text = Pixels(settings.CornerRadiusDip);
    }

    // The sliders work in the units they show (pixels, percent); the settings store doc 06 §1's ranges.
    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        edits.Update(s => s with { CoreThicknessDip = (float)Math.Round(e.NewValue) });

    private void OnGlowChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        edits.Update(s => s with { Glow = (float)(Math.Round(e.NewValue) / 100) });

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        edits.Update(s => s with { Brightness = (float)(Math.Round(e.NewValue) / 100) });

    private void OnCornerChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        edits.Update(s => s with { CornerRadiusDip = (float)Math.Round(e.NewValue) });

    private void OnCoverTaskbarChanged(object sender, RoutedEventArgs e) =>
        edits.Update(s => s with { CoverTaskbar = CoverTaskbarSwitch.IsChecked == true });

    // What the preview shows when the glow itself isn't on screen.
    private static string Caption(Settings settings)
    {
        if (!settings.Enabled) return "The glow is turned off. This is how it looks when it's on.";
        if (settings.Animation == AnimationMode.Off) return "Motion is set to Off. This is how the glow looks in Idle Glow.";
        return settings.Monitors == MonitorSelection.Custom && (settings.CustomMonitorIds?.Count ?? 0) == 0
            ? "No display is chosen on the Displays page, so the glow isn't shown anywhere."
            : "";
    }

    // One chip per built-in look: its two colors and its name. Presets change appearance fields only (doc 06 §1).
    private void BuildPresets()
    {
        IReadOnlyList<(string Name, Func<Settings, Settings> Apply)> presets;
        try
        {
            presets = Presets.All;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Reading the presets failed: {exception.Message}");
            presets = [];
        }
        PresetsSection.Visibility = presets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach ((string name, Func<Settings, Settings> apply) in presets)
        {
            var primary = new Ellipse { Width = 18, Height = 18 };
            var secondary = new Ellipse { Width = 18, Height = 18, Margin = new Thickness(-6, 0, 0, 0) };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(primary);
            content.Children.Add(secondary);
            content.Children.Add(new TextBlock { Text = name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var chip = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 6, 12, 6) };
            AutomationProperties.SetName(chip, $"Preset {name}");
            chip.Click += (_, _) => ApplyPreset(name, apply);
            PresetChips.Children.Add(chip);
            chips.Add((chip, apply, primary, secondary, name));
        }
    }

    private void ApplyPreset(string name, Func<Settings, Settings> apply)
    {
        try
        {
            edits.Update(apply);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Applying the preset {name} failed: {exception}");
        }
    }

    // A chip shows the preset's colors; the chosen one (applying it again would change nothing) has the accent border.
    private void RefreshPresets(Settings settings)
    {
        foreach ((Button chip, Func<Settings, Settings> apply, Shape primary, Shape secondary, string name) in chips)
        {
            Settings? look = null;
            try
            {
                look = apply(settings);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[Settings] The preset {name} failed: {exception.Message}");
            }
            primary.Fill = Brush(look?.PrimaryHex);
            secondary.Fill = Brush(look?.SecondaryHex);
            bool chosen = look is not null && look == settings;
            chip.SetResourceReference(BorderBrushProperty, chosen ? "AccentFillColorDefaultBrush" : "ControlElevationBorderBrush");
            chip.BorderThickness = new Thickness(chosen ? 2 : 1);
            AutomationProperties.SetItemStatus(chip, chosen ? "Chosen" : "");
        }
    }

    private static Brush Brush(string? hex)
    {
        if (!SrgbColor.TryParse(hex, out SrgbColor color)) return Brushes.Transparent;
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static string Pixels(float dip) => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(dip):0} px");

    private static string Percent(float fraction) => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(fraction * 100):0}%");
}
