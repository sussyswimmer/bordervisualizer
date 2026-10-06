using Rimlight.Core.Audio;
using Rimlight.Core.Color;
using Rimlight.Core.Lighting;
using Rimlight.Core.SettingsStorage;

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
    /// (a new target in the middle of a fade carries on from there, without a jump) to the target over the whole
    /// duration: Primary to Primary and Secondary to Secondary along straight lines in Oklab. A fade from rest is
    /// smoothstep-eased (3t² − 2t³ of the elapsed share); one that takes over while the colors are moving eases out only
    /// (t(2 − t)), so they keep moving: calling SetTarget with 200 ms on every edit of a color drag is fine, and the glow
    /// trails the picker by about 0.1 s. A target with the colors the running fade is heading to (the next track of the
    /// same album) leaves that fade as it is and only changes Current's track. A zero or negative duration, or a target
    /// whose colors are already displayed, switches at once. <see cref="IPaletteBlender.IsAnimating"/> is true from
    /// SetTarget until the <see cref="IPaletteBlender.Update"/> that reaches the duration; that Update settles exactly
    /// on the target's colors, so refill the gradient after it too (H-006). Update ignores NaN, negative and zero steps; an infinite
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
    /// <summary>Creates the JSON settings store for <c>settings.json</c> in <paramref name="directory"/> (doc 02 "Settings
    /// storage", doc 06 §1). Nothing is read or written until the first call.</summary>
    /// <param name="directory">The settings directory, normally <c>%APPDATA%\Rimlight</c>. It is created by the first
    /// <see cref="ISettingsStore.Save"/>; a relative path is resolved against the current directory now.</param>
    /// <returns>A new settings store.</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty or whitespace.</exception>
    /// <remarks>
    /// <para><b>Load</b> never throws and always returns valid settings. A missing file gives defaults. A file that
    /// can't be read or parsed (after a brief retry if another program has it locked) is copied over
    /// <c>settings.bad.json</c> in the same directory and defaults are returned. Otherwise each field is read on its
    /// own: a missing, mistyped or invalid field takes its default and the rest are kept. Numbers are clamped to their
    /// doc 06 range (a NaN or ∞ passed to Save gives the default), <see cref="Settings.FpsCap"/> outside
    /// {0, 30, 60, 120} gives 60, colors are stored as upper-case <c>#RRGGBB</c> (<c>#RGB</c> is expanded), and unknown
    /// fields are ignored. <see cref="Settings.ToggleHotkey"/> is opaque (only null is replaced; an empty string means no
    /// hotkey), and <see cref="Settings.CustomMonitorIds"/> keeps every non-empty ID, including unplugged monitors (H-009).
    /// A file from an older version is migrated; one from a newer version is read as far as this version understands it.</para>
    /// <para><b>Save</b> validates the same way, creates the directory, and replaces the file atomically (temporary file,
    /// flush to disk, then a swap), so a crash or failure leaves the previous file, or the complete new one if Windows'
    /// replace failed half-way. It always writes the current schema version, which drops fields only a newer version
    /// knows. It throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when
    /// the file can't be written; catch those and retry on the next change.</para>
    /// <para>The format is camelCase JSON with enums as names, e.g. <c>"animation": "MusicSync"</c>. Calls on one store
    /// are serialized internally and may come from any thread; never call them per frame.</para>
    /// </remarks>
    public static ISettingsStore CreateSettingsStore(string directory) => new JsonSettingsStore(directory);
}
