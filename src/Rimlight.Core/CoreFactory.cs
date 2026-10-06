using Rimlight.Core.Audio;
using Rimlight.Core.Fakes;

namespace Rimlight.Core;

/// <summary>The sole construction seam for Platform and App; fakes remain until their Lane A tasks land.</summary>
public static class CoreFactory
{
    /// <summary>Creates the real audio analyzer: FFT, auto-gain, spectral-flux beats and silence detection (doc 03).</summary>
    /// <param name="tuning">Optional initial tuning parameters.</param>
    /// <returns>A new analyzer.</returns>
    /// <remarks>
    /// <para>Call every member from the render thread. <see cref="IAudioAnalyzer.Process"/> takes any span length,
    /// including empty spans while no packets arrive, and never allocates. After
    /// <see cref="AudioTuning.NoPacketTimeoutSeconds"/> without packets the gap counts as digital silence.</para>
    /// <para>Sensitivity is applied here once: it scales <see cref="AudioFeatures.Level"/> and lowers the beat threshold.
    /// The light engine must not apply it again. Setting <see cref="IAudioAnalyzer.Tuning"/> validates the value
    /// (invalid tuning throws and keeps the previous one); a <see cref="AudioTuning.WindowSize"/> change allocates,
    /// resets the analysis and replaces <see cref="IAudioAnalyzer.Diagnostics"/>, so read that property every frame.</para>
    /// <para>Diagnostics: <see cref="AnalyzerDiagnostics.Spectrum"/> has WindowSize / 2 + 1 bins, DC through Nyquist,
    /// in amplitude units: |X[k]| × 4 / WindowSize, so a full-scale sine centered on a bin reads about 1. Bin k is at
    /// k × <see cref="AnalysisSampleRate"/> / WindowSize Hz.
    /// <see cref="AnalyzerDiagnostics.FluxHistory"/> and <see cref="AnalyzerDiagnostics.ThresholdHistory"/> hold 240
    /// entries, exactly one per <c>Process</c> call (empty spans included), oldest first and zero until filled; a time
    /// axis must use the actual call cadence, not an assumed 60 Hz. Each flux entry is the largest flux of that call,
    /// and each threshold entry is the effective beat threshold, in the same units. <see cref="AnalyzerDiagnostics.EstimatedBpm"/> is the median of
    /// recent beat intervals and returns to 0 after 4 s without a beat. <see cref="IAudioAnalyzer.Reset"/> clears
    /// everything except the current <see cref="AudioFeatures.IsSilent"/> state.</para>
    /// </remarks>
    public static IAudioAnalyzer CreateAnalyzer(AudioTuning? tuning = null) => new AudioAnalyzer(tuning);
    /// <summary>The rate the analyzer's spectrum is computed at for a capture rate: capture rates of 75 kHz and above
    /// are decimated (by 2 from 75 kHz, 4 from 150 kHz, 8 from 300 kHz) so the bass band keeps its resolution.</summary>
    /// <param name="captureSampleRate">The capture sample rate passed to <see cref="IAudioAnalyzer.Process"/>, in Hz.</param>
    /// <returns>The analysis sample rate in Hz, for mapping diagnostics bins to frequencies.</returns>
    public static int AnalysisSampleRate(int captureSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureSampleRate);
        return AudioAnalyzer.AnalysisRate(captureSampleRate);
    }
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
