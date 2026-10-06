using Rimlight.Core.Audio;
using Rimlight.Core.Color;
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
    /// <summary>Creates the real palette extractor: k-means in Oklab, a scored Primary and Secondary, glow-ify (doc 05 §2).</summary>
    /// <returns>A new extractor.</returns>
    /// <remarks>
    /// <para>Input: tightly packed BGRA8 with straight alpha, any size. The buffer must be exactly width × height × 4 bytes
    /// and both sizes positive, otherwise <see cref="ArgumentException"/>; <c>trackId</c> must not be null. Pixels with alpha
    /// below 128 are skipped, and images over 512 × 512 are sampled on a grid.</para>
    /// <para>Output: <see cref="Palette.Primary"/> and <see cref="Palette.Secondary"/> are linear RGB with every channel in
    /// 0..1 (H-008), glow-ified in OkLCh: lightness 0.55..0.85 and chroma at least 0.12, reduced only where sRGB can't
    /// hold it. A grayscale artwork color (chroma below 0.03) stays neutral and glows soft white.
    /// <see cref="Palette.SourceTrackId"/> is <c>trackId</c>.</para>
    /// <para>Returns null for "no art" (doc 05 §1): when no pixel has alpha 128 or more, or when the palette is nearly
    /// grayscale (Primary and Secondary below chroma 0.03 before glow-ify) and the image is mostly one flat color (at
    /// least 60 % of the pixels within Oklab distance 0.03 of the largest cluster), as with a player's generic app
    /// icon. Keep the previous palette, or the manual one, in that case.</para>
    /// <para>Deterministic, and the extractor holds no state, so calls may run on any thread, even concurrently. One
    /// call took about 1.5 ms for 64 × 64 and under 0.1 s for 512 × 512 on the Linux test machine: run it on the
    /// thread pool, never on the UI or render thread. Scratch comes from the shared array pool, which keeps it per
    /// thread: the first call on a thread allocates it (about 85 KB at 64 × 64, about 5 MB at 512 × 512) and that
    /// thread holds it until the pool trims it; a repeat call on the same thread allocates about 500 bytes. Pass the
    /// thumbnail at 64 × 64 to keep both small.</para>
    /// </remarks>
    public static IPaletteExtractor CreatePaletteExtractor() => new PaletteExtractor();
    /// <summary>Creates the real palette blender: Oklab crossfades with ease-in-out, and the 64-texel perimeter gradient
    /// (doc 05 §3, doc 04 §3 steps 4–5).</summary>
    /// <param name="initial">The palette to show first, with no fade running.</param>
    /// <returns>A new blender.</returns>
    /// <remarks>
    /// <para><b>Threading.</b> Not thread-safe: call every member, <see cref="IPaletteBlender.Current"/> included, from
    /// the render thread that owns the instance. <see cref="IPaletteBlender.Update"/> and
    /// <see cref="IPaletteBlender.FillGradient"/> never allocate, read no clock and are deterministic;
    /// <see cref="IPaletteBlender.SetTarget"/> allocates nothing for a palette with channels in 0..1. A null palette
    /// throws.</para>
    /// <para><b>Crossfade.</b> <see cref="IPaletteBlender.SetTarget"/> fades from the colors displayed at that moment
    /// (a new target in the middle of a fade carries on from there, without a jump) to the target over the duration:
    /// Primary to Primary and Secondary to Secondary along straight lines in Oklab, smoothstep-eased (3t² − 2t³ of the
    /// elapsed share). Each call restarts the clock. A zero or negative duration, or a target whose colors are already
    /// displayed, switches at once. <see cref="IPaletteBlender.IsAnimating"/> is true from SetTarget until the
    /// <see cref="IPaletteBlender.Update"/> that reaches the duration; that Update settles exactly on the target's
    /// colors, so refill the gradient after it too (H-006). Update ignores NaN, negative and zero steps; an infinite
    /// step finishes the fade. Channels outside 0..1 are clamped and NaN counts as 0.</para>
    /// <para><b>Current.</b> While idle it is the palette last passed to SetTarget (or <paramref name="initial"/>), the
    /// same instance unless a channel had to be clamped, and reading it allocates nothing. During a fade it is the
    /// blend, with the target's <see cref="Palette.SourceTrackId"/>: the first read after each Update or SetTarget
    /// allocates one <see cref="Palette"/>, later reads return it. Keep it off the per-frame path (H-004).</para>
    /// <para><b>Gradient (H-008 item 1, binding).</b> FillGradient writes 64 RGBA texels into the first 256 floats
    /// (fewer throws <see cref="ArgumentException"/>; anything after them is left alone). Texel i is the color at
    /// perimeter coordinate u = (i + 0.5) / 64 before the shader's <c>frac(t + Phase)</c>, sampled with WRAP addressing
    /// and linear filtering. Primary covers u = 0..ratio, Secondary the rest; both boundaries, u = ratio and the seam
    /// u = 0 ≡ 1 between texels 63 and 0, are smoothstep blends 0.08 wide centered on them and mixed in Oklab, so the
    /// loop is seamless and Primary's weight over the whole loop averages exactly ratio. Away from the blends the
    /// texels are the displayed colors exactly. Linear RGB in 0..1 (Oklab mixes that leave sRGB are clamped per
    /// channel, as the shader's <c>saturate</c> would), alpha 1, not premultiplied. The ratio is clamped to 0.1..0.9,
    /// where each color still has a pure middle, and NaN uses the <see cref="Settings.PrimaryRatio"/> default (0.6).
    /// One fill took about 0.6 µs, idle or during a fade, on the Linux test machine.</para>
    /// </remarks>
    public static IPaletteBlender CreatePaletteBlender(Palette initial) => new PaletteBlender(initial);
    /// <summary>Creates a simple linear light engine until C6 lands.</summary>
    /// <returns>A new light engine.</returns>
    public static ILightEngine CreateLightEngine() => new FakeLightEngine();
    /// <summary>Creates an in-memory settings stub until C7 lands; no file is written.</summary>
    /// <param name="directory">Directory for the eventual settings file.</param>
    /// <returns>A new settings store.</returns>
    public static ISettingsStore CreateSettingsStore(string directory) => new FakeSettingsStore(directory);
}
