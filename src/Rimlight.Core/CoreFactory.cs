using Rimlight.Core.Fakes;

namespace Rimlight.Core;

/// <summary>The sole construction seam for Platform and App; K0 returns development fakes.</summary>
public static class CoreFactory
{
    /// <summary>Creates a synthetic 120 BPM analyzer until C2 lands.</summary>
    /// <param name="tuning">Optional initial tuning parameters.</param>
    /// <returns>A new analyzer.</returns>
    public static IAudioAnalyzer CreateAnalyzer(AudioTuning? tuning = null) => new FakeAnalyzer(tuning);
    /// <summary>Creates an average-color extractor until C4 lands.</summary>
    /// <returns>A new extractor.</returns>
    public static IPaletteExtractor CreatePaletteExtractor() => new FakePaletteExtractor();
    /// <summary>Creates an immediate-transition palette stub until C5 lands.</summary>
    /// <param name="initial">Initial palette.</param>
    /// <returns>A new blender.</returns>
    public static IPaletteBlender CreatePaletteBlender(Palette initial) => new FakePaletteBlender(initial);
    /// <summary>Creates a simple linear light engine until C6 lands.</summary>
    /// <returns>A new light engine.</returns>
    public static ILightEngine CreateLightEngine() => new FakeLightEngine();
    /// <summary>Creates an in-memory settings stub until C7 lands; no file is written.</summary>
    /// <param name="directory">Directory for the eventual settings file.</param>
    /// <returns>A new settings store.</returns>
    public static ISettingsStore CreateSettingsStore(string directory) => new FakeSettingsStore(directory);
}
