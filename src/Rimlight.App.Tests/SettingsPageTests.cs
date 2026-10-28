using System.Windows;
using System.Windows.Controls;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Xunit;

namespace Rimlight.App.Tests;

// Each settings page and the color editor on its own, as the settings window drives them: built (their XAML, its
// styles from PageStyles.xaml and WPF-UI's templates), shown the settings, started and stopped, and laid out at the
// window's size and at its narrowest, without a window.
public sealed class SettingsPageTests : UiTest
{
    public static TheoryData<string> Pages => ["Appearance", "Color", "Motion", "Displays", "Behavior", "About"];

    [WindowsTheory]
    [MemberData(nameof(Pages))]
    public void EveryPageBuildsShowsTheSettingsAndLaysOut(string name)
    {
        UiThread.Run(() =>
        {
            UserControl page = Create(name);
            var settingsPage = (ISettingsPage)page;
            Settings settings = Controller.SettingsService.Current;
            settingsPage.SetActive(true); // the window's order: a page reads what it shows (monitors, media) first
            settingsPage.Refresh(settings);
            Size wide = Layout(page, 572, 520); // the default window's content area beside the navigation pane
            Size narrow = Layout(page, 372, 360); // the minimum window
            settingsPage.Refresh(settings with
            {
                Animation = AnimationMode.IdleGlow, ColorMode = ColorMode.Manual, EdgeStyle = EdgeStyle.Line,
            });
            settingsPage.SetActive(false);
            Assert.True(wide.Height > 100, $"{name} measured {wide}");
            Assert.True(narrow.Height > 100, $"{name} measured {narrow} at the minimum width");
        });
        AssertNoFailures();
    }

    [WindowsFact]
    public void TheColorEditorShowsAColorAndSurvivesABadOne()
    {
        UiThread.Run(() =>
        {
            var editor = new GlowColorEditor();
            editor.Configure("First color", "Covers most of the edge.");
            editor.Show("#7C5CFF", "#7C5CFF");
            Layout(editor, 520, 300);
            editor.Show("not a color", "#22D3EE"); // an invalid setting shows its fallback
            Assert.Equal("#22D3EE", editor.HexBox.Text);
            Assert.True(editor.DesiredSize.Height > 50);
        });
        AssertNoFailures();
    }

    [WindowsFact]
    public void EditsApplyOnlyOnceThePageShowsTheSettings()
    {
        // PageEdits: control events while the XAML loads (Brightness's minimum of 10 moves its slider) or while a page
        // shows the settings are not edits; after the first refresh, moving a slider is one.
        UiThread.Run(() =>
        {
            Settings before = Controller.SettingsService.Current;
            var page = new AppearancePage(Controller);
            Assert.Same(before, Controller.SettingsService.Current);
            ((ISettingsPage)page).Refresh(before);
            Assert.Same(before, Controller.SettingsService.Current);
            page.ThicknessSlider.Value = 0;
            Assert.Equal(0f, Controller.SettingsService.Current.CoreThicknessDip);
        });
        AssertNoFailures();
    }

    [WindowsFact]
    public void TheEdgeChoiceSwitchesBetweenTheSoftGlowAndTheBrightLine()
    {
        // Maxwell's request: the bright line is an option on the Appearance page; the Thickness row then sets the
        // line's width, and says so.
        UiThread.Run(() =>
        {
            var page = new AppearancePage(Controller);
            Settings before = Controller.SettingsService.Current;
            ((ISettingsPage)page).Refresh(before);
            Assert.Equal(before.EdgeStyle == EdgeStyle.Line, page.BrightLineChoice.IsChecked);
            page.BrightLineChoice.IsChecked = true;
            Assert.Equal(EdgeStyle.Line, Controller.SettingsService.Current.EdgeStyle);
            ((ISettingsPage)page).Refresh(Controller.SettingsService.Current);
            Assert.Contains("line", page.ThicknessDescription.Text, StringComparison.Ordinal);
            page.SoftGlowChoice.IsChecked = true;
            Assert.Equal(EdgeStyle.Glow, Controller.SettingsService.Current.EdgeStyle);
            ((ISettingsPage)page).Refresh(Controller.SettingsService.Current);
            Assert.DoesNotContain("line", page.ThicknessDescription.Text, StringComparison.Ordinal);
        });
        AssertNoFailures();
    }

    private UserControl Create(string name) => name switch
    {
        "Appearance" => new AppearancePage(Controller),
        "Color" => new ColorPage(Controller),
        "Motion" => new MotionPage(Controller),
        "Displays" => new DisplaysPage(Controller),
        "Behavior" => new BehaviorPage(Controller),
        "About" => new AboutPage(Controller),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a settings page."),
    };
}
