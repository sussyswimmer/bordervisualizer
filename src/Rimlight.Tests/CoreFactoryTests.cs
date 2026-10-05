using Rimlight.Core;
using Xunit;

namespace Rimlight.Tests;

public sealed class CoreFactoryTests
{
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
