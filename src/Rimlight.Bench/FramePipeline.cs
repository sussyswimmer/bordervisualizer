using System.Diagnostics;
using Rimlight.Core;

namespace Rimlight.Bench;

/// <summary>How the glow will change after a frame, as the app's frame source reports it to the overlay (Platform's
/// <c>FrameMotion</c>, doc 02 "Frame pacing").</summary>
internal enum FrameMotion
{
    /// <summary>Music or a transition: the full frame rate.</summary>
    Full,
    /// <summary>Slow change only (Idle Glow breathing): 10 frames a second.</summary>
    Slow,
    /// <summary>Hidden until the audio changes (music sync hides silence): keep calling about 10 times a second.</summary>
    Listening,
    /// <summary>Hidden until an input changes (Off, disabled, paused): no frames at all.</summary>
    Still,
}

/// <summary>One frame's cost per stage, in <see cref="Stopwatch"/> ticks.</summary>
/// <param name="Frame">The whole frame, glue included.</param>
/// <param name="Analyzer"><see cref="IAudioAnalyzer.Process"/>, plus <see cref="IAudioAnalyzer.Reset"/> on a device change.</param>
/// <param name="Blender">The settings and palette checks with any <see cref="IPaletteBlender.SetTarget"/> (a new
/// settings snapshot's analyzer retune included), <see cref="IPaletteBlender.Update"/>, and
/// <see cref="IPaletteBlender.FillGradient"/> when the gradient is refilled.</param>
/// <param name="Engine"><see cref="ILightEngine.Update"/>.</param>
internal readonly record struct StageTicks(long Frame, long Analyzer, long Blender, long Engine);

// One render-thread frame as the app runs it: MusicGlowSource.NextFrame (src/Rimlight.App/Overlay/MusicGlowSource.cs)
// step for step, with a clock read between the stages. Rimlight.App is Windows-only, so its glue is mirrored here:
// keep the two in step (Rimlight.Platform.Tests' BenchMirrorTests runs both and fails when they differ). What is left out is Windows work, not Core work: the copy out of the loopback ring (one
// memcpy, done by the caller before the frame), the volatile write that publishes the features to the UI, and the
// overlay's own drawing (doc 04). The caller plays LoopbackCapture: it says when the captured device changes and
// passes only the samples that arrived. Settings never shows the audio meter here ("audio wanted" is false), and the
// manual colors stay fixed (the app parses them again when an edit changes them; no schedule edits them).
// Render thread only. Applying a new settings snapshot does what the app's ApplySettings does, allocation included: a
// Sensitivity change rebuilds the analyzer's AudioTuning record on the render thread (`with`, one object per edit).
// SettingsBytes reports that per-edit cost apart; the rest of NextFrame allocates nothing.
internal sealed class FramePipeline
{
    /// <summary>The rate passed while no device is captured (MusicGlowSource.FallbackSampleRate).</summary>
    public const int FallbackSampleRate = 48000;
    /// <summary>Sustained silence counts as Idle Glow this long after IsSilent (MusicGlowSource.IdleSettleSeconds).</summary>
    public const float IdleSettleSeconds = 1.75f;
    /// <summary>New album colors, and album/manual switches, crossfade this long (doc 05 §3).</summary>
    public static readonly TimeSpan AlbumFade = TimeSpan.FromMilliseconds(800);
    /// <summary>A manual color edit crossfades this long.</summary>
    public static readonly TimeSpan ManualEditFade = TimeSpan.FromMilliseconds(200);
    private static readonly AudioFeatures Silence = new(0, 0, 0, true);

    private readonly IAudioAnalyzer analyzer;
    private readonly IPaletteBlender blender;
    private readonly ILightEngine engine;
    private readonly Palette manual;
    private readonly float[] gradient = new float[256];
    private Settings applied;
    private Palette? album;
    private Palette palette;
    private bool gradientFilled, paletteChanged;
    private float filledRatio = float.NaN, silentSeconds;

    /// <summary>Creates the Core objects the way MusicGlowSource does.</summary>
    /// <param name="settings">The initial settings.</param>
    /// <param name="manual">The manual colors, shown while there is no album palette.</param>
    public FramePipeline(Settings settings, Palette manual)
    {
        this.manual = manual;
        palette = manual;
        blender = CoreFactory.CreatePaletteBlender(palette);
        analyzer = CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = ClampSensitivity(settings.Sensitivity) });
        engine = CoreFactory.CreateLightEngine();
        applied = settings;
    }

    /// <summary>The analyzer, for its diagnostics.</summary>
    public IAudioAnalyzer Analyzer => analyzer;
    /// <summary>The light engine, for <see cref="ILightEngine.IsStatic"/>.</summary>
    public ILightEngine Engine => engine;
    /// <summary>The gradient the last refill wrote (64 RGBA texels).</summary>
    public ReadOnlySpan<float> Gradient => gradient;
    /// <summary>The features of the last frame.</summary>
    public AudioFeatures Features { get; private set; }
    /// <summary>The light state of the last frame.</summary>
    public LightState State { get; private set; }
    /// <summary>Whether the last frame refilled the gradient (the app uploads it to the GPU then).</summary>
    public bool Refilled { get; private set; }
    /// <summary>Whether the last frame started a crossfade (<see cref="IPaletteBlender.SetTarget"/>).</summary>
    public bool PaletteSet { get; private set; }
    /// <summary>Whether the last frame applied a new settings snapshot.</summary>
    public bool SettingsApplied { get; private set; }
    /// <summary>The bytes the last frame allocated while applying a new settings snapshot (a Sensitivity change
    /// rebuilds the analyzer tuning, as in the app); 0 on frames without one. Part of the frame's own allocations.</summary>
    public long SettingsBytes { get; private set; }
    /// <summary>The last frame's cost per stage.</summary>
    public StageTicks Ticks { get; private set; }

    /// <summary>Whether the glow listens to the audio: music sync, enabled and not paused (MusicGlowSource).</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="paused">The overlay's pause flag.</param>
    /// <returns>True while the loopback capture is open.</returns>
    public static bool Listens(Settings settings, bool paused) =>
        settings.Enabled && settings.Animation == AnimationMode.MusicSync && !paused;

    /// <summary>Runs one frame.</summary>
    /// <param name="samples">The samples that arrived since the last frame (none while nothing is captured).</param>
    /// <param name="sampleRate">The capture rate, or <see cref="FallbackSampleRate"/> while nothing is captured.</param>
    /// <param name="dtSeconds">Time since the last frame, as the overlay passes it.</param>
    /// <param name="settings">This frame's settings snapshot (a new reference is a new snapshot, as in the app).</param>
    /// <param name="latestAlbum">The album palette the media service publishes; null for none (no art, no session).</param>
    /// <param name="paused">The overlay's pause flag.</param>
    /// <param name="captureChanged">True when the captured device changed since the last frame (opened, closed or
    /// restarted): the analyzer starts over, as the app does on a new <c>LoopbackCapture.Current</c>.</param>
    /// <returns>How the glow will change, for the frame pacer.</returns>
    public FrameMotion NextFrame(ReadOnlySpan<float> samples, int sampleRate, float dtSeconds, Settings settings,
        Palette? latestAlbum, bool paused, bool captureChanged)
    {
        long t0 = Stopwatch.GetTimestamp();
        PaletteSet = false;
        SettingsApplied = !ReferenceEquals(settings, applied);
        SettingsBytes = 0;
        if (SettingsApplied)
        {
            // Counted apart so a caller can tell the once-per-edit cost from per-frame allocations; two counter reads
            // on edit frames only.
            long before = GC.GetAllocatedBytesForCurrentThread();
            ApplySettings(settings);
            SettingsBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        if (!ReferenceEquals(latestAlbum, album))
        {
            album = latestAlbum;
            ShowPalette(settings, AlbumFade);
        }
        bool listen = Listens(settings, paused);

        long t1 = Stopwatch.GetTimestamp();
        if (captureChanged) analyzer.Reset();
        AudioFeatures features = analyzer.Process(samples, sampleRate, dtSeconds);
        long t2 = Stopwatch.GetTimestamp();
        silentSeconds = features.IsSilent ? MathF.Min(silentSeconds + dtSeconds, 3600) : 0;

        bool crossfading = blender.IsAnimating;
        blender.Update(dtSeconds);
        long t3 = Stopwatch.GetTimestamp();
        LightState state = engine.Update(dtSeconds, listen ? features : Silence, palette, settings, paused);
        long t4 = Stopwatch.GetTimestamp();

        // Refill after every SetTarget, after every update that was animating before it (so the crossfade's last frame
        // is uploaded too, H-006), and when the ratio changes.
        bool refill = !gradientFilled || paletteChanged || crossfading || state.Ratio != filledRatio;
        if (refill)
        {
            blender.FillGradient(gradient, state.Ratio);
            gradientFilled = true;
            paletteChanged = false;
            filledRatio = state.Ratio;
        }
        long t5 = Stopwatch.GetTimestamp();
        FrameMotion motion = Motion(listen, in features, settings);
        long t6 = Stopwatch.GetTimestamp();

        Features = features;
        State = state;
        Refilled = refill;
        Ticks = new StageTicks(t6 - t0, t2 - t1, (t1 - t0) + (t3 - t2) + (t5 - t4), t4 - t3);
        return motion;
    }

    // MusicGlowSource.Motion: the overlay draws music at the full rate, breathing at 10 fps, and stops drawing a static
    // glow (listening on at 10 Hz while music could bring it back).
    private FrameMotion Motion(bool listen, in AudioFeatures features, Settings settings)
    {
        if (blender.IsAnimating) return FrameMotion.Full;
        if (engine.IsStatic) return listen ? FrameMotion.Listening : FrameMotion.Still;
        if (!listen) return FrameMotion.Slow;
        bool idle = features.IsSilent && silentSeconds >= IdleSettleSeconds && settings.WhenSilent == SilentBehavior.IdleGlow;
        return idle ? FrameMotion.Slow : FrameMotion.Full;
    }

    // MusicGlowSource.ApplySettings, the analyzer retune included: AudioTuning is a record class, so `with` allocates
    // one object per Sensitivity edit on the render thread, in the app too. The schedules never edit the manual colors,
    // so the manual palette is fixed here (the app parses the hex colors again only when they change).
    private void ApplySettings(Settings next)
    {
        Settings previous = applied;
        applied = next;
        if (previous.Sensitivity != next.Sensitivity)
            analyzer.Tuning = analyzer.Tuning with { Sensitivity = ClampSensitivity(next.Sensitivity) };
        bool albumBefore = album is not null && ReferenceEquals(palette, album);
        bool albumAfter = album is not null && UsesAlbumColors(next);
        ShowPalette(next, albumBefore || albumAfter ? AlbumFade : ManualEditFade);
    }

    private void ShowPalette(Settings settings, TimeSpan fade)
    {
        Palette next = album is not null && UsesAlbumColors(settings) ? album : manual;
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        blender.SetTarget(next, fade);
        paletteChanged = true;
        PaletteSet = true;
    }

    private static bool UsesAlbumColors(Settings settings) =>
        settings.ColorMode == ColorMode.AlbumArt && !settings.OverrideAlbumColor;

    // MusicGlowSource.ClampSensitivity.
    private static float ClampSensitivity(float sensitivity) =>
        float.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0.25f, 2f) : 1f;
}
