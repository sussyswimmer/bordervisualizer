using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int WmSysColorChange = 0x0015;

    // #7C5CFF, the brand violet (doc 08, Palette.Default's primary). WPF-UI derives lighter shades for dark mode and
    // darker ones for light mode, and picks black or white text on it.
    private static readonly Color BrandAccent = Color.FromRgb(0x7C, 0x5C, 0xFF);

    private static bool loaded;
    private static SystemTheme appliedVariant; // the Windows theme the last Apply read: which high-contrast palette

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
    /// Follows Windows' theme if it changed: the app's windows call it when Windows announces a theme change
    /// (<see cref="Follow"/>), the tray when its menu opens. Cheap when nothing changed (a registry read, a few more in
    /// high contrast).
    /// </summary>
    public static void Sync()
    {
        if (!loaded) return;
        try
        {
            ApplicationTheme wanted = Wanted();
            // WPF-UI has one dictionary per contrast theme, so a switch between two of them re-applies too.
            bool contrastChanged = false;
            if (wanted == ApplicationTheme.HighContrast)
            {
                SystemThemeManager.UpdateSystemThemeCache();
                contrastChanged = SystemThemeManager.GetCachedSystemTheme() != appliedVariant;
            }
            if (wanted != ApplicationThemeManager.GetAppTheme() || contrastChanged) Apply(wanted);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Ui] Following the system theme failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Lets a window follow Windows' theme: a theme, system color or app mode broadcast to it runs <see cref="Sync"/>,
    /// once per burst and outside the window procedure, then <paramref name="followed"/>. Top-level windows get these
    /// broadcasts also while hidden. Call from the window's <c>OnSourceInitialized</c>.
    /// </summary>
    /// <param name="window">The window; its handle exists.</param>
    /// <param name="followed">What the window updates itself after a sync (its title bar), or null.</param>
    public static void Follow(Window window, Action? followed = null)
    {
        if (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is not { } source) return;
        bool queued = false;
        source.AddHook(OnMessage);

        IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            bool changed = message is WmThemeChanged or WmSysColorChange
                // "Choose your app mode"
                || (message == WmSettingChange && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet");
            if (!changed || queued) return IntPtr.Zero;
            queued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                queued = false;
                Sync();
                followed?.Invoke();
            });
            return IntPtr.Zero;
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
        appliedVariant = SystemThemeManager.GetCachedSystemTheme();
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
