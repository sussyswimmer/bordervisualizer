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
    public void AllFactoryMethodsReturnObjects()
    {
        Assert.NotNull(CoreFactory.CreateAnalyzer());
        Assert.NotNull(CoreFactory.CreatePaletteExtractor());
        Assert.NotNull(CoreFactory.CreatePaletteBlender(Palette.Default));
        Assert.NotNull(CoreFactory.CreateLightEngine());
        Assert.NotNull(CoreFactory.CreateSettingsStore(System.IO.Path.GetTempPath()));
    }

    [Fact]
    public void EveryFactoryMethodReturnsTheRealImplementation()
    {
        // C13: the K0 fakes are gone; each call is a new instance of Core's real type.
        Assert.IsType<AudioAnalyzer>(CoreFactory.CreateAnalyzer());
        Assert.IsType<PaletteExtractor>(CoreFactory.CreatePaletteExtractor());
        Assert.IsType<PaletteBlender>(CoreFactory.CreatePaletteBlender(Palette.Default));
        Assert.IsType<LightEngine>(CoreFactory.CreateLightEngine());
        Assert.IsType<JsonSettingsStore>(CoreFactory.CreateSettingsStore(System.IO.Path.GetTempPath()));
        Assert.NotSame(CoreFactory.CreateLightEngine(), CoreFactory.CreateLightEngine());
        Assert.NotSame(CoreFactory.CreatePaletteBlender(Palette.Default), CoreFactory.CreatePaletteBlender(Palette.Default));
        Assert.NotSame(CoreFactory.CreateAnalyzer(), CoreFactory.CreateAnalyzer());
    }

    [Fact]
    public void NoFakesAreLeftInCore()
    {
        // C2–C7 each deleted their K0 fake from Rimlight.Core/Fakes; nothing named or namespaced as a fake may come back.
        string[] fakes = typeof(CoreFactory).Assembly.GetTypes()
            .Where(t => t.Name.StartsWith("Fake", StringComparison.Ordinal)
                || (t.Namespace?.Split('.').Contains("Fakes", StringComparer.Ordinal) ?? false))
            .Select(t => t.FullName!)
            .ToArray();
        Assert.Empty(fakes);
    }

    [Fact]
    public void ThePublicSurfaceIsTheContractsTheFactoryPresetsAndAppInfo()
    {
        // docs/CORE-API.md documents exactly these; a new public type needs a section there.
        string[] expected =
        [
            "AnalyzerDiagnostics", "AnimationMode", "AppInfo", "AudioFeatures", "AudioTuning", "BatteryBehavior", "ColorMode",
            "CoreFactory", "IAudioAnalyzer", "ILightEngine", "IPaletteBlender", "IPaletteExtractor", "ISettingsStore",
            "LightState", "MonitorSelection", "NowPlaying", "Palette", "Presets", "Rgb", "Settings", "SilentBehavior",
        ];
        string[] exported = typeof(CoreFactory).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, exported);
        Assert.All(typeof(CoreFactory).Assembly.GetExportedTypes(), t => Assert.Equal("Rimlight.Core", t.Namespace));
    }
}
