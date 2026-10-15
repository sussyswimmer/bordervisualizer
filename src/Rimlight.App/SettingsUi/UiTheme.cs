using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using ShellTheme = Rimlight.Platform.Shell.SystemTheme;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// WPF-UI's Fluent styles for the app's windows and the tray menu, in Windows' light, dark or high-contrast app theme,
/// with the brand violet (doc 08) as the accent. Use on the UI thread only.
/// </summary>
internal static class UiTheme
{
    // #7C5CFF, the brand violet (doc 08, Palette.Default's primary). WPF-UI derives lighter shades for dark mode and
    // darker ones for light mode, and picks black or white text on it.
    private static readonly Color BrandAccent = Color.FromRgb(0x7C, 0x5C, 0xFF);

    private static bool loaded;

    /// <summary>Whether WPF-UI's resources are in place; the settings window needs them.</summary>
    public static bool IsLoaded => loaded;

    /// <summary>The theme the app shows now.</summary>
    public static ApplicationTheme Current => loaded ? ApplicationThemeManager.GetAppTheme() : ApplicationTheme.Light;

    /// <summary>
    /// Adds WPF-UI's theme and control dictionaries to the application's resources and applies Windows' theme. Call
    /// once, before anything uses WPF-UI: its theme manager binds to the application's resources on first use.
    /// </summary>
    public static void Load()
    {
        if (loaded || Application.Current is not { } application) return;
        try
        {
            // The theme dictionary first: the theme manager swaps the first WPF-UI dictionary whose source names a theme.
            application.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            application.Resources.MergedDictionaries.Add(new ControlsDictionary());
            loaded = true;
            ApplicationThemeManager.Changed += (theme, _) => ApplyAccent(theme);
            Apply(Wanted()); // raises Changed, which sets the accent
        }
        catch (Exception exception)
        {
            // The tray works with WPF's own styles; the settings window can't be shown without these.
            Trace.WriteLine($"[Ui] Loading the Fluent styles failed: {exception}");
        }
    }

    /// <summary>
    /// Follows Windows' theme if it changed: the settings window calls it when Windows announces a theme change, the
    /// tray when its menu opens. Cheap when nothing changed (a registry read).
    /// </summary>
    public static void Sync()
    {
        if (!loaded) return;
        try
        {
            ApplicationTheme wanted = Wanted();
            if (wanted != ApplicationThemeManager.GetAppTheme()) Apply(wanted);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Ui] Following the system theme failed: {exception.Message}");
        }
    }

    // Windows' app mode, read the same way as the welcome window (K6): high contrast, else "Choose your app mode".
    private static ApplicationTheme Wanted() =>
        SystemParameters.HighContrast ? ApplicationTheme.HighContrast
        : ShellTheme.AppsUseDarkMode() ? ApplicationTheme.Dark
        : ApplicationTheme.Light;

    // Swaps the theme dictionary; WPF-UI also updates the main window's backdrop and dark title bar (the settings
    // window claims that role).
    private static void Apply(ApplicationTheme theme)
    {
        SystemThemeManager.UpdateSystemThemeCache(); // which high-contrast palette, for HighContrast
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
    }

    // High contrast keeps the system's own colors.
    private static void ApplyAccent(ApplicationTheme theme)
    {
        if (theme == ApplicationTheme.HighContrast) return;
        try
        {
            ApplicationAccentColorManager.Apply(BrandAccent, theme);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Ui] Applying the accent color failed: {exception.Message}");
        }
    }
}
