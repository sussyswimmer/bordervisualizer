using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Rimlight.Core;
using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>
/// The first-run welcome: where the tray icon is and what it does, the glow's keyboard shortcut (or why it has none),
/// starting at sign-in, and the privacy promise. Follows Windows' light or dark app mode, and keeps the system colors
/// under high contrast. Until K7 it also stands in for the settings window.
/// </summary>
internal partial class WelcomeWindow : Window
{
    private readonly Action? openSettings;

    /// <summary>Builds the window; <see cref="Present"/> shows it.</summary>
    /// <param name="hotkey">The glow shortcut and whether it works.</param>
    /// <param name="startsWithWindows">Whether the app is registered to start at sign-in.</param>
    /// <param name="openSettings">Opens Settings; null hides the button.</param>
    public WelcomeWindow(HotkeyStatus hotkey, bool startsWithWindows, Action? openSettings)
    {
        InitializeComponent();
        this.openSettings = openSettings;
        string name = AppInfo.Name;
        Title = $"Welcome to {name}";
        Heading.Text = $"{name} is running";
        Intro.Text = $"{name} lights the edges of your screen in time with whatever your PC is playing. "
            + "You'll find it in the notification area, next to the clock.";
        ClickText.Text = $" the {name} icon to turn the glow on or off.";
        SetHotkey(hotkey);
        SetText(StartupText, startsWithWindows ? $"{name} starts when you sign in to Windows. You can change that in Settings." : "");
        FindIconText.Text = $"Don't see the icon? Click the ^ arrow next to the clock, then drag {name} onto the taskbar "
            + "to keep it in view.";
        PrivacyText.Text = $"{name} only listens to the sound your PC plays, never the microphone. Nothing leaves your PC "
            + "except a check for updates.";
        SettingsButton.Visibility = openSettings is null ? Visibility.Collapsed : Visibility.Visible;
        ApplyTheme();
    }

    /// <summary>Shows the window, or brings it back to the front if it is open.</summary>
    public void Present()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Updates the shortcut line (the shortcut changed while the window is open).</summary>
    /// <param name="hotkey">The glow shortcut and whether it works.</param>
    public void SetHotkey(HotkeyStatus hotkey)
    {
        string gesture = hotkey.Gesture?.ToString() ?? hotkey.Text; // no gesture when the shortcut window is missing
        SetText(HotkeyText, hotkey.State switch
        {
            HotkeyState.Registered => $"Press {gesture} anywhere to turn the glow on or off.",
            HotkeyState.InUse => $"Another app already uses {gesture}, so the glow has no keyboard shortcut yet. "
                + "You can pick a different one in Settings.",
            HotkeyState.Invalid => $"\"{hotkey.Text}\" isn't a shortcut Windows can use. You can pick a different one in Settings.",
            HotkeyState.Failed => $"Windows didn't accept {gesture} as a shortcut. You can pick a different one in Settings.",
            _ => "",
        });
    }

    private static void SetText(System.Windows.Controls.TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        Close();
        openSettings?.Invoke();
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();

    // Windows 11-like colors for light or dark mode. High contrast keeps the system's colors and controls.
    private void ApplyTheme()
    {
        if (SystemParameters.HighContrast) return;
        bool dark = SystemTheme.AppsUseDarkMode();
        Resources["TextBrush"] = Frozen(dark ? 0xFFFFFF : 0x1B1B1B);
        Resources["ButtonBrush"] = Frozen(dark ? 0x2D2D2D : 0xFFFFFF);
        Resources["ButtonBorderBrush"] = Frozen(dark ? 0x454545 : 0xD1D1D1);
        // The brand violet, darker on light and lighter on dark, so the label keeps a contrast above 6:1.
        Resources["AccentBrush"] = Frozen(dark ? 0xA78BFA : 0x5B3FD9);
        Resources["AccentTextBrush"] = Frozen(dark ? 0x1B1B1B : 0xFFFFFF);
        Background = Frozen(dark ? 0x202020 : 0xF9F9F9);
        Foreground = (Brush)Resources["TextBrush"];
        SettingsButton.Style = (Style)Resources["ThemedButton"];
        DoneButton.Style = (Style)Resources["AccentButton"];
        SourceInitialized += (_, _) => SystemTheme.UseDarkTitleBar(new WindowInteropHelper(this).Handle, dark);
    }

    private static SolidColorBrush Frozen(int rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }
}
