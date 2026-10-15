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
        // docs/CORE-API.md describes each of these; a new public method needs a section there too.
        string[] methods = [.. typeof(CoreFactory).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["AnalysisSampleRate", "CreateAnalyzer", "CreateLightEngine", "CreatePaletteBlender", "CreatePaletteExtractor", "CreateSettingsStore"], methods);
        string[] publicTypes = [.. typeof(CoreFactory).Assembly.GetExportedTypes().Where(t => !t.IsNested).Select(t => t.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["AnalyzerDiagnostics", "AnimationMode", "AppInfo", "AudioFeatures", "AudioTuning", "BatteryBehavior", "ColorMode", "CoreFactory",
             "IAudioAnalyzer", "ILightEngine", "IPaletteBlender", "IPaletteExtractor", "ISettingsStore", "LightState", "MonitorSelection",
             "NowPlaying", "Palette", "Presets", "Rgb", "Settings", "SilentBehavior"],
            publicTypes);
    }
}
