using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// One of the two manual colors (doc 06 §3.2): a hex box and hue and saturation sliders at the color's own brightness
/// (value). Edits apply live: the overlay crossfades manual edits over 200 ms (doc 05 §3). A six-digit hex applies as
/// it is typed; a three-digit one, or a correction, when the box loses focus or Enter is pressed.
/// </summary>
internal partial class GlowColorEditor : UserControl
{
    private SrgbColor color;
    private double hue;        // kept for grays and black, where the color itself has no hue
    private double saturation; // kept for black
    private bool showing;      // the editor is setting its own controls
    private bool shown;        // a color from the settings is shown; edits count only from then on

    public GlowColorEditor()
    {
        InitializeComponent();
        HexBox.KeyDown += OnHexKeyDown;
    }

    /// <summary>Raised when the user picks a color, with its "#RRGGBB" form.</summary>
    public event Action<string>? ColorEdited;

    /// <summary>Sets the label, the description and the names screen readers announce.</summary>
    public void Configure(string name, string description)
    {
        Label.Text = name;
        Description.Text = description;
        AutomationProperties.SetName(HexBox, $"{name}, hex code");
        AutomationProperties.SetHelpText(HexBox, "A color like #7C5CFF");
        AutomationProperties.SetName(HueSlider, $"{name}, hue in degrees");
        AutomationProperties.SetName(SaturationSlider, $"{name}, saturation in percent");
        AutomationProperties.SetHelpText(SaturationSlider, "0 is gray or white, 100 is the most vivid");
    }

    /// <summary>Shows a color from the settings (an invalid one as the default it falls back to).</summary>
    public void Show(string? hex, string fallback)
    {
        if (!SrgbColor.TryParse(hex, out SrgbColor next)) SrgbColor.TryParse(fallback, out next);
        if (shown && next == color) return;
        shown = true;
        color = next;
        ShowSliders(next);
        // Not while the user types: the text would jump under the cursor.
        if (!HexBox.IsKeyboardFocusWithin || !SrgbColor.TryParse(HexBox.Text, out SrgbColor typed) || typed != next)
            SetText(next.ToString());
        Swatch.Background = Brush(next);
    }

    // Moves the sliders to a color, unless they already give it (their rounding would make them jump).
    private void ShowSliders(SrgbColor next)
    {
        (double h, double s, double v) = next.ToHsv();
        if (SrgbColor.FromHsv(HueSlider.Value, SaturationSlider.Value / 100, v) == next)
        {
            hue = HueSlider.Value;
            saturation = SaturationSlider.Value / 100;
        }
        else
        {
            if (v > 0 && s > 0) hue = h; // grays and black keep the hue (and black the saturation) they had
            if (v > 0) saturation = s;
            showing = true;
            try
            {
                PageEdits.Set(HueSlider, Math.Round(hue) % 360);
                PageEdits.Set(SaturationSlider, Math.Round(saturation * 100));
            }
            finally
            {
                showing = false;
            }
        }
        ShowSaturationStrip();
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (showing || !shown) return;
        string text = HexBox.Text.Trim();
        // Live only for six digits: "#7C5" on the way to "#7C5CFF" is a valid color of its own.
        if (text.TrimStart('#').Length == 6 && SrgbColor.TryParse(text, out SrgbColor typed))
        {
            PageEdits.SetMessage(HexError, "");
            Pick(typed, fromSliders: false);
        }
    }

    private void OnHexLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitHex();

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitHex();
        e.Handled = true;
    }

    // The text as a color, or back to the current color with a hint.
    private void CommitHex()
    {
        if (!shown) return;
        string text = HexBox.Text.Trim();
        if (SrgbColor.TryParse(text, out SrgbColor typed))
        {
            PageEdits.SetMessage(HexError, "");
            Pick(typed, fromSliders: false);
            SetText(typed.ToString());
            return;
        }
        if (text.Length > 0)
            PageEdits.SetMessage(HexError, $"\"{text}\" isn't a color. Use a hex code like #7C5CFF.");
        SetText(color.ToString());
    }

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (showing || !shown) return;
        hue = e.NewValue;
        PickFromSliders();
        ShowSaturationStrip();
    }

    private void OnSaturationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (showing || !shown) return;
        saturation = e.NewValue / 100;
        PickFromSliders();
    }

    // At the color's own brightness; from black, at full brightness, or the sliders would do nothing visible.
    private void PickFromSliders()
    {
        double value = color.ToHsv().Value;
        Pick(SrgbColor.FromHsv(hue, saturation, value > 0 ? value : 1), fromSliders: true);
    }

    // The user's color: shown at once, then applied (the settings change comes back here as the same color).
    private void Pick(SrgbColor next, bool fromSliders)
    {
        if (next == color) return;
        color = next;
        Swatch.Background = Brush(next);
        if (!fromSliders) ShowSliders(next);
        if (!HexBox.IsKeyboardFocusWithin) SetText(next.ToString());
        ColorEdited?.Invoke(next.ToString());
    }

    private void SetText(string text)
    {
        showing = true;
        try
        {
            if (HexBox.Text != text) HexBox.Text = text;
        }
        finally
        {
            showing = false;
        }
    }

    // From gray to the full color at this hue and brightness.
    private void ShowSaturationStrip()
    {
        double value = color.ToHsv().Value;
        if (value <= 0) value = 1;
        var strip = new LinearGradientBrush(ToColor(SrgbColor.FromHsv(hue, 0, value)), ToColor(SrgbColor.FromHsv(hue, 1, value)), 0);
        strip.Freeze();
        SaturationStrip.Background = strip;
    }

    private static Color ToColor(SrgbColor c) => Color.FromRgb(c.R, c.G, c.B);

    private static SolidColorBrush Brush(SrgbColor c)
    {
        var brush = new SolidColorBrush(ToColor(c));
        brush.Freeze();
        return brush;
    }
}
