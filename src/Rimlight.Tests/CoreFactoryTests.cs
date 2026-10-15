using System.Reflection;
using Rimlight.Core;
using Rimlight.Core.Audio;
using Rimlight.Core.Color;
using Rimlight.Core.Lighting;
using Rimlight.Core.SettingsStorage;
using Xunit;

namespace Rimlight.Tests;

public sealed class CoreFactoryTests
{
    [Fact]
    public void PresetCatalogIsAvailableDuringShellStartup()
    {
        Assert.Equal(5, Presets.All.Count);
    }

    [Fact]
    public void SettingsStoreIsTheJsonFileStore()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rimlight-tests", Guid.NewGuid().ToString("N"));
        try
        {
            ISettingsStore store = CoreFactory.CreateSettingsStore(directory);
            Assert.IsType<JsonSettingsStore>(store);
            Assert.Equal(System.IO.Path.Combine(directory, "settings.json"), store.Path);
            Assert.False(Directory.Exists(directory)); // nothing touched before the first call

            store.Save(new Settings { Glow = 0.7f });
            Assert.True(File.Exists(store.Path));
            Assert.Equal(0.7f, CoreFactory.CreateSettingsStore(directory).Load().Glow);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EveryFactoryMethodReturnsTheRealImplementation()
    {
        // C13: the Step 0 fakes are gone and the factory is the only seam Lane B uses.
        Assert.IsType<AudioAnalyzer>(CoreFactory.CreateAnalyzer());
        Assert.IsType<AudioAnalyzer>(CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = 1.5f }));
        Assert.IsType<PaletteExtractor>(CoreFactory.CreatePaletteExtractor());
        Assert.IsType<PaletteBlender>(CoreFactory.CreatePaletteBlender(Palette.Default));
        Assert.IsType<LightEngine>(CoreFactory.CreateLightEngine());
        Assert.IsType<JsonSettingsStore>(CoreFactory.CreateSettingsStore(System.IO.Path.GetTempPath()));
    }

    [Fact]
    public void CoreContainsNoFakes()
    {
        Type[] types = typeof(CoreFactory).Assembly.GetTypes();
        Assert.DoesNotContain(types, t => t.Namespace?.Contains("Fakes", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(types, t => t.Name.StartsWith("Fake", StringComparison.Ordinal));
    }

    [Fact]
    public void FactorySurfaceIsTheOneCoreApiDocuments()
    {
        // The surface is pinned, so changing it fails here as a reminder to update docs/CORE-API.md.
        string[] methods = [.. typeof(CoreFactory).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["AnalysisSampleRate", "CreateAnalyzer", "CreateLightEngine", "CreatePaletteBlender", "CreatePaletteExtractor", "CreateSettingsStore"], methods);
        string[] publicTypes = [.. typeof(CoreFactory).Assembly.GetExportedTypes().Where(t => !t.IsNested).Select(t => t.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["AnalyzerDiagnostics", "AnimationMode", "AppInfo", "AudioFeatures", "AudioTuning", "BatteryBehavior", "ColorMode", "CoreFactory",
             "IAudioAnalyzer", "ILightEngine", "IPaletteBlender", "IPaletteExtractor", "ISettingsStore", "LightState", "MonitorSelection",
             "NowPlaying", "Palette", "Presets", "Rgb", "Settings", "SilentBehavior"],
            publicTypes);

        // And the page itself names every factory method and exported type in code, as `Name`, `Name(…)` or `Name.Member`,
        // so updating only the lists above doesn't pass.
        string page = File.ReadAllText(CoreApiPagePath());
        string[] undocumented = [.. methods.Concat(publicTypes).Where(name => !page.Contains('`' + name + '`', StringComparison.Ordinal)
            && !page.Contains('`' + name + '(', StringComparison.Ordinal) && !page.Contains('`' + name + '.', StringComparison.Ordinal))];
        Assert.True(undocumented.Length == 0, "docs/CORE-API.md doesn't name: " + string.Join(", ", undocumented));
    }

    // docs/CORE-API.md in the source tree, found from the test output folder (bin/<configuration>/<framework>/).
    private static string CoreApiPagePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rimlight.sln")))
                return Path.Combine(directory.FullName, "docs", "CORE-API.md");
        }
        throw new DirectoryNotFoundException("Rimlight.sln not found above " + AppContext.BaseDirectory);
    }
}
