using System.Windows;
using Rimlight.App.SettingsUi;
using Wpf.Ui.Appearance;
using Xunit;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace Rimlight.App.Tests;

// The settings window as Maxwell opens it: from the tray (AppController.ShowSettings, the launcher, Present), then
// every page through the navigation view, in every theme. The window, its pages and WPF-UI's navigation catch and log
// what goes wrong, so these tests also fail on what the app logged (UiTest.AssertNoFailures): a window that opened
// with nothing in it, or a page that never showed, fails here even though nothing was thrown.
public sealed class SettingsWindowTests : UiTest
{
    private static readonly Type[] PageTypes =
    [
        typeof(AppearancePage), typeof(ColorPage), typeof(MotionPage), typeof(DisplaysPage), typeof(BehaviorPage),
        typeof(AboutPage),
    ];

    [WindowsFact]
    public void TheTrayOpensSettingsOnTheAppearancePageAndClosingOnlyHidesIt()
    {
        // The tray's double-click and "Settings…" (and a second start, and the welcome's button) all come here.
        UiThread.Run(Controller.ShowSettings);
        SettingsWindow window = UiThread.Run(() =>
            Track(Application.Current.Windows.OfType<SettingsWindow>().Single()));
        WaitForPage(window, typeof(AppearancePage));
        UiThread.Run(() =>
        {
            Assert.True(window.IsVisible);
            Assert.Same(window, Application.Current.MainWindow); // WPF-UI restyles the main window on a theme change
            Assert.Contains("Settings", window.Title, StringComparison.Ordinal);
            window.Close(); // the title bar's close button: the app keeps running in the tray
        });
        UiThread.WaitUntil(() => !window.IsVisible, "the window to hide");
        UiThread.Run(Controller.ShowSettings);
        WaitForPage(window, typeof(AppearancePage)); // the same window, back on its page
        Assert.Single(UiThread.Run(() => Application.Current.Windows.OfType<SettingsWindow>().ToArray()));
        AssertNoFailures();
    }

    [WindowsTheory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    [InlineData(ApplicationTheme.HighContrast)]
    public void EveryPageOpensInEveryTheme(ApplicationTheme theme)
    {
        try
        {
            SettingsWindow window = UiThread.Run(() =>
            {
                // As UiTheme applies it. Show, not Present: Present follows Windows' theme, which would undo this one.
                ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
                SettingsWindow created = Track(new SettingsWindow(Controller));
                MoveOffScreen(created);
                created.Show();
                return created;
            });
            WaitForPage(window, typeof(AppearancePage)); // the window opens on Appearance by itself
            foreach (Type page in PageTypes.Skip(1).Append(typeof(AppearancePage)))
            {
                UiThread.Run(() => window.Navigation.Navigate(page)); // what a click on its item in the pane does
                WaitForPage(window, page);
            }
        }
        finally
        {
            UiThread.Run(UiTheme.Sync); // back to the theme Windows asks for
        }
        AssertNoFailures();
    }

    // Waits until the page is on screen in the window: loaded, visible, and laid out with a size.
    private static void WaitForPage(Window window, Type page) =>
        UiThread.WaitUntil(
            () => Descendants(window).OfType<FrameworkElement>()
                .Any(e => e.GetType() == page && e.IsLoaded && e.IsVisible && e.ActualWidth > 0 && e.ActualHeight > 0),
            $"the {page.Name} to show in the settings window");
}
