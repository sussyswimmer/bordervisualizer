using Rimlight.Core.Audio;
using Rimlight.Core.Fakes;
using Rimlight.Core.Lighting;

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
    /// <para><see cref="AudioTuning.MinFlux"/> is relative to the analysis window's RMS, so beats don't depend on playback
    /// volume; below the near-silence level (<see cref="AudioTuning.SilenceThresholdDb"/> − 20 dB) no beat fires.</para>
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
    /// <summary>Creates the real light engine: doc 07 Phase 3 intensity, beat pulse and phase drift, Idle Glow
    /// breathing, and every fade (doc 01 §2, doc 04 §4).</summary>
    /// <returns>A new light engine.</returns>
    /// <remarks>
    /// <para>Call each instance from one thread only: the render thread, which may own one per monitor (see below); a
    /// Settings preview may own another. <see cref="ILightEngine.Update"/> never allocates, reads no clock and is
    /// deterministic. A negative or non-finite dtSeconds counts as 0, a step over 0.1 s counts as 0.1 s, and the first
    /// step that moves time, on a new engine or after a static frame, counts at most 1/60 s, so neither a stall, a slow
    /// start nor a renderer waking from idle makes the light jump. Non-finite or out-of-range audio, colors and settings
    /// are clamped, or replaced by 0 or the <see cref="Settings"/> default; every output is finite and in range. A null
    /// palette or settings throws.</para>
    /// <para><b>Intensity and Visibility (H-008 item 2, binding).</b> <see cref="LightState.Intensity"/> excludes
    /// <see cref="LightState.Visibility"/>; the renderer multiplies alpha by both. Intensity is
    /// Brightness × (0.35 + 0.65 × Level) in Music Sync and Brightness × 0.9 × (1 + 0.1 × sin(2π t / 6 s)) in
    /// Idle Glow (breathing between 81 % and 99 % of Brightness). Level already carries Sensitivity, which the engine
    /// never applies again (H-007). Visibility carries every fade: pause, Enabled = false and Animation = Off fade out
    /// and back in over 300 ms; in Music Sync with WhenSilent = Hide, <see cref="AudioFeatures.IsSilent"/> fades out
    /// over 1.5 s, keeping the look it starts with (Idle Glow too), and sound brings it back within 150 ms. With
    /// WhenSilent = IdleGlow, IsSilent instead crossfades Intensity, Pulse and the drift to Idle Glow over 1.5 s and
    /// back within 150 ms, and Visibility stays 1. Switching between Music Sync and Idle Glow uses the same crossfade.
    /// A new engine starts at Visibility 0 and fades in over 300 ms. All fades are smoothstep-eased.</para>
    /// <para><b>Other fields.</b> ColorA and ColorB are the palette's Primary and Secondary (pass the palette last given to
    /// <see cref="IPaletteBlender.SetTarget"/>; the gradient carries the crossfade). Ratio is PrimaryRatio clamped to
    /// 0.1..0.9, Spread is Glow (0..1, mapped to pixels by the renderer) and CoreThicknessDip is clamped to 0..40.
    /// Pulse is Beat, unsmoothed, times the Music Sync weight. Phase is in [0, 1): it drifts 0.015 cycles/s, and each
    /// beat (Beat rising by more than 0.1; it re-arms once Beat falls) adds 0.01 cycles as a push that decays with
    /// τ = 120 ms. Idle Glow has no drift and no pulse.</para>
    /// <para><b>IsStatic.</b> True only while the light is hidden and stays hidden for the same inputs: the 300 ms gate
    /// fade has finished (paused, Enabled = false, Animation = Off), or, in Music Sync with WhenSilent = Hide, the 1.5 s
    /// silence fade has. It turns true on the second such Update in a row, so the frame that reaches Visibility 0 is
    /// still presented. While it is true nothing moves with time: Update returns the same state for the same inputs
    /// whatever dtSeconds is, and how many calls it gets changes nothing later, so the renderer may stop presenting and
    /// stop calling. It turns false on the very Update whose inputs can show the light again (even with
    /// dtSeconds = 0): unpaused, Enabled, Animation not Off, IsSilent cleared, or WhenSilent or Animation no longer
    /// hiding silence. Changes that cannot show the light (palette, Glow, Brightness and other appearance fields, Level,
    /// Beat) leave it true. Every visible state moves (Music Sync drifts, Idle Glow breathes), so it is never true while
    /// anything is visible.</para>
    /// <para><b>Renderer guidance (doc 02).</b> While Music Sync hides silence, keep feeding the analyzer and calling
    /// Update at a low rate, or the return of sound goes unnoticed. In Off, disabled or paused, only a settings or pause
    /// change can wake it. Idle Glow (Animation = IdleGlow, or Music Sync with WhenSilent = IdleGlow from 1.5 s after
    /// IsSilent) changes only through the 6 s breathing, so 10 fps is enough there.</para>
    /// <para><b>Per-monitor pause (doc 04 §4).</b> <c>paused</c> is one flag per engine, so give each overlay its own
    /// engine, all updated on the render thread with the same audio, palette and settings and that monitor's flag. An
    /// Update costs well under a microsecond and allocates nothing. Stop rendering only while every engine reports
    /// IsStatic. A new engine fades in over 300 ms, which also suits a monitor plugged in later.</para>
    /// </remarks>
    public static ILightEngine CreateLightEngine() => new LightEngine();
    /// <summary>Creates an in-memory settings stub until C7 lands; no file is written.</summary>
    /// <param name="directory">Directory for the eventual settings file.</param>
    /// <returns>A new settings store.</returns>
    public static ISettingsStore CreateSettingsStore(string directory) => new FakeSettingsStore(directory);
}
