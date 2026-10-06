using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Rimlight.Platform.Shell;

/// <summary>The light or dark app theme the user picked in Windows, for the app's own small windows.</summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when Windows is set to dark mode for apps. False when light, or when it can't be read.</summary>
    /// <returns>Whether apps should be dark.</returns>
    public static bool AppsUseDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false; // unreadable: assume light
        }
    }

    /// <summary>Gives a window a dark (or light) title bar. Has no effect where Windows doesn't support it.</summary>
    /// <param name="window">The window's handle.</param>
    /// <param name="dark">True for a dark title bar.</param>
    public static unsafe void UseDarkTitleBar(nint window, bool dark)
    {
        if (window == 0) return;
        BOOL value = dark;
        // Windows 10 2004 and later (the app's minimum); the call fails harmlessly anywhere else.
        PInvoke.DwmSetWindowAttribute((HWND)window, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &value, (uint)sizeof(BOOL));
    }
}
