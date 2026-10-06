using Rimlight.Core;
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
}
