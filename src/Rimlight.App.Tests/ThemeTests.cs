using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Rimlight.App.SettingsUi;
using Wpf.Ui.Appearance;
using Xunit;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace Rimlight.App.Tests;

// WPF-UI's theme as UiTheme loads it, and the resources the app's windows take from it. A DynamicResource whose key no
// dictionary has throws nothing: the control just gets no brush (text in the window's own color, an invisible card),
// so the keys are checked here, in every theme the app can show.
public sealed partial class ThemeTests : UiTest
{
    [WindowsFact]
    public void TheFluentStylesLoadAsTheAppLoadsThem()
    {
        UiThread.Run(() =>
        {
            Assert.Null(UiTheme.LoadFailure);
            Assert.True(UiTheme.IsLoaded);
            // In the application's resources, where WPF-UI's theme manager looks for the theme dictionary to swap.
            string[] sources =
                [.. Application.Current.Resources.MergedDictionaries.Select(d => d.Source?.ToString() ?? "")];
            Assert.Contains(sources, source =>
                source.Contains("Wpf.Ui;component/Resources/Theme/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(sources, source =>
                source.EndsWith("Wpf.Ui;component/Resources/Wpf.Ui.xaml", StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual(ApplicationTheme.Unknown, UiTheme.Current);
        });
        AssertNoFailures();
    }

    [WindowsTheory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    [InlineData(ApplicationTheme.HighContrast)]
    public void EveryThemeHasEveryResourceTheWindowsUse(ApplicationTheme theme)
    {
        string[] keys = ThemeKeysTheAppUses();
        Assert.True(keys.Length >= 10, $"Found only {keys.Length} theme keys in the app's XAML and code.");
        try
        {
            string[] missing = UiThread.Run(() =>
            {
                // As UiTheme applies a theme (the accent follows through ApplicationThemeManager.Changed).
                ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
                return keys.Where(key => Application.Current.TryFindResource(key) is null).ToArray();
            });
            Assert.True(missing.Length == 0, $"Missing in the {theme} theme: {string.Join(", ", missing)}");
        }
        finally
        {
            UiThread.Run(UiTheme.Sync); // back to the theme Windows asks for
        }
        AssertNoFailures();
    }

    [WindowsFact]
    public void TheAppIconLoads()
    {
        // The settings and visualizer windows' title bars and the About page; a missing icon is logged, not thrown.
        Assert.True(UiThread.Run(() => AboutPage.LoadIcon(32) is not null));
        AssertNoFailures();
    }

    // Every key the app's XAML takes with DynamicResource or its code with SetResourceReference, minus the keys the
    // app defines itself: what must come from WPF-UI's dictionaries. Read from the source, so a new key is checked too.
    private static string[] ThemeKeysTheAppUses()
    {
        string app = Path.Combine(RepositoryRoot(), "src", "Rimlight.App");
        string[] xaml = ReadSources(app, "*.xaml");
        string[] code = ReadSources(app, "*.cs");

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (string text in xaml)
        {
            foreach (Match match in DynamicResource().Matches(text)) used.Add(match.Groups[1].Value);
        }
        foreach (string text in code)
        {
            foreach (Match call in SetResourceReference().Matches(text))
            {
                foreach (Match key in StringLiteral().Matches(call.Value)) used.Add(key.Groups[1].Value);
            }
        }

        // The app's own: x:Key in its XAML, and brushes its code puts in a window's resources (the welcome's).
        var own = new HashSet<string>(StringComparer.Ordinal);
        foreach (string text in xaml)
        {
            foreach (Match match in XamlKey().Matches(text)) own.Add(match.Groups[1].Value);
        }
        foreach (string text in code)
        {
            foreach (Match match in ResourceSetInCode().Matches(text)) own.Add(match.Groups[1].Value);
        }
        return [.. used.Except(own).Order(StringComparer.Ordinal)];
    }

    // The source files' text, without the copies and generated files under bin and obj.
    private static string[] ReadSources(string folder, string pattern)
    {
        string bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        string obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        return
        [
            .. Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                .Where(path => !path.Contains(bin, StringComparison.Ordinal) && !path.Contains(obj, StringComparison.Ordinal))
                .Select(File.ReadAllText),
        ];
    }

    // The tests run from the build output inside the repository (src/Rimlight.App.Tests/bin/…), in CI too.
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Rimlight.sln"))) return folder.FullName;
        }
        throw new DirectoryNotFoundException($"No Rimlight.sln above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"\{DynamicResource\s+([^\s}]+)\s*\}")]
    private static partial Regex DynamicResource();

    [GeneratedRegex(@"SetResourceReference\([^;]*;")]
    private static partial Regex SetResourceReference();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex StringLiteral();

    [GeneratedRegex("x:Key=\"([^\"]+)\"")]
    private static partial Regex XamlKey();

    [GeneratedRegex("Resources\\[\"([^\"]+)\"\\]\\s*=")]
    private static partial Regex ResourceSetInCode();
}
