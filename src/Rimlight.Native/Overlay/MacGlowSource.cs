using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Overlay;

/// <summary>
/// The Mac's frame source: the Windows <c>MusicGlowSource</c> (src/Rimlight.App/Overlay) without the Windows capture,
/// media service and debug visualizer. Each frame takes the samples the render thread drained from the audio ring,
/// runs the analyzer and the light engine, and refills the palette gradient when the colors change; the demo mode, the
/// palette rules and the frame motion are the same code paths. Runs on the render thread only, allocation-free per
/// frame.
/// </summary>
/// <remarks>
/// Keep it in step with MusicGlowSource: a change to the light's rules there belongs here too (M2 adds a replay test
/// that compares the two through Bench's FramePipeline).
/// </remarks>
internal sealed class MacGlowSource
{
    private const int FallbackSampleRate = 48000; // used while nothing is captured; only empty spans are passed then
    // In music sync, sustained silence fades to Idle Glow over 1.5 s (PRD §2); after that only slow breathing is left.
    private const float IdleSettleSeconds = 1.75f;
    // Doc 05 §3: new album colors, or a switch between album and manual colors, crossfade over 800 ms; a manual color
    // edit over 200 ms.
    internal static readonly TimeSpan AlbumFade = TimeSpan.FromMilliseconds(800);
    internal static readonly TimeSpan ManualEditFade = TimeSpan.FromMilliseconds(200);
    private static readonly AudioFeatures Silence = new(0, 0, 0, true);

    private readonly IAudioAnalyzer analyzer;
    private readonly ILightEngine engine;
    private readonly IPaletteBlender blender;
    private readonly DemoBeat demoBeat = new();
    private Palette manual;
    private Palette? album;         // the album palette last published (M4); null: none
    private Palette? publishedAlbum; // written by any thread with Volatile.Write, read once per frame
    private Palette palette;
    private Settings? applied;
    private bool gradientFilled;
    private bool paletteChanged;
    private float filledRatio = float.NaN;
    private float silentSeconds;
    private int demoWanted;
    private bool demo;
    private int demoPalette;
    private Settings? demoSource;
    private Settings? demoSettings;
    private int captureGeneration = -1;
    private int audioWanted;  // written by the main thread (Settings' meter and preview), read once per frame
    private long latestAudio; // AudioReading.Pack of the last frame's features, read by the main thread
    private int audioSequence;

    public MacGlowSource(Settings settings)
    {
        manual = ManualPalette(settings);
        palette = manual;
        blender = CoreFactory.CreatePaletteBlender(palette);
        analyzer = CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = ClampSensitivity(settings.Sensitivity) });
        engine = CoreFactory.CreateLightEngine();
        applied = settings;
    }

    /// <summary>
    /// Whether this frame wants the audio: the glow listens, or Settings shows the meter or the preview. The capture
    /// runs only then.
    /// </summary>
    public bool Listening { get; private set; }

    /// <summary>The analyzer's features from the latest frame. Any thread.</summary>
    public AudioReading LatestAudio => AudioReading.Unpack(Volatile.Read(ref latestAudio));

    /// <summary>
    /// Keeps the capture open and the analyzer running even when the glow doesn't need audio, while Settings shows the
    /// level meter or the preview (MusicGlowSource.SetAudioWanted). Any thread.
    /// </summary>
    public void SetAudioWanted(bool wanted) => Volatile.Write(ref audioWanted, wanted ? 1 : 0);

    /// <summary>The album palette last published (M4), or null. Any thread.</summary>
    public Palette? PublishedAlbum => Volatile.Read(ref publishedAlbum);

    /// <summary>Demo mode (doc 04 §5), as MusicGlowSource.SetDemo. Any thread.</summary>
    public void SetDemo(bool on) => Volatile.Write(ref demoWanted, on ? 1 : 0);

    /// <summary>Whether demo mode is asked for. Any thread.</summary>
    public bool DemoWanted => Volatile.Read(ref demoWanted) != 0;

    /// <summary>A new album palette, or null for none (M4). Any thread; the frame picks it up by reference.</summary>
    public void PublishAlbum(Palette? palette) => Volatile.Write(ref publishedAlbum, palette);

    /// <summary>
    /// One frame. <paramref name="samples"/>: mono samples drained from the audio ring since the last frame, at
    /// <paramref name="sampleRate"/> (0 while nothing is captured). <paramref name="captureGeneration"/> changes
    /// whenever the capture opens, closes or restarts on another device or format: the analysis starts over, as the
    /// Windows app's does on a new <c>LoopbackCapture.Current</c>.
    /// </summary>
    public OverlayFrame NextFrame(float dtSeconds, Settings settings, bool paused, Span<float> gradient,
        ReadOnlySpan<float> samples, int sampleRate, int captureGeneration)
    {
        if (!ReferenceEquals(settings, applied)) ApplySettings(settings);

        Palette? latestAlbum = Volatile.Read(ref publishedAlbum);
        if (!ReferenceEquals(latestAlbum, album))
        {
            album = latestAlbum;
            ShowPalette(settings, AlbumFade);
        }

        bool demoOn = Volatile.Read(ref demoWanted) != 0;
        if (demoOn != demo) SwitchDemo(demoOn, settings);

        bool listen = !demo && settings.Enabled && settings.Animation == AnimationMode.MusicSync && !paused;
        bool wanted = Volatile.Read(ref audioWanted) != 0;
        Listening = listen || wanted;

        if (captureGeneration != this.captureGeneration)
        {
            this.captureGeneration = captureGeneration;
            analyzer.Reset();
        }
        int rate = sampleRate > 0 ? sampleRate : FallbackSampleRate;
        AudioFeatures features = analyzer.Process(samples, rate, dtSeconds);
        silentSeconds = features.IsSilent ? MathF.Min(silentSeconds + dtSeconds, 3600) : 0;
        audioSequence = AudioReading.Next(audioSequence);
        Volatile.Write(ref latestAudio, AudioReading.Pack(in features, audioSequence));

        AudioFeatures heard = listen ? features : Silence;
        Settings look = settings;
        if (demo)
        {
            heard = demoBeat.Advance(dtSeconds);
            ShowDemoPalette(demoBeat.PaletteIndex);
            look = DemoSettings(settings);
        }

        bool crossfading = blender.IsAnimating;
        blender.Update(dtSeconds);
        LightState state = engine.Update(dtSeconds, heard, palette, look, paused);

        bool refill = !gradientFilled || paletteChanged || crossfading || state.Ratio != filledRatio;
        if (refill)
        {
            blender.FillGradient(gradient, state.Ratio);
            gradientFilled = true;
            paletteChanged = false;
            filledRatio = state.Ratio;
        }

        return new OverlayFrame(state, refill, Motion(listen, wanted, in features, settings));
    }

    // As MusicGlowSource.Motion, without the debug visualizer.
    private FrameMotion Motion(bool listen, bool wanted, in AudioFeatures features, Settings settings)
    {
        if (blender.IsAnimating) return FrameMotion.Full;
        if (engine.IsStatic) return listen || wanted ? FrameMotion.Listening : FrameMotion.Still;
        if (demo) return FrameMotion.Full;
        if (!listen) return FrameMotion.Slow;
        bool idle = features.IsSilent && silentSeconds >= IdleSettleSeconds && settings.WhenSilent == SilentBehavior.IdleGlow;
        return idle ? FrameMotion.Slow : FrameMotion.Full;
    }

    private void ApplySettings(Settings settings)
    {
        Settings? previous = applied;
        applied = settings;
        if (previous is null || previous.Sensitivity != settings.Sensitivity)
            analyzer.Tuning = analyzer.Tuning with { Sensitivity = ClampSensitivity(settings.Sensitivity) };
        if (previous is null || previous.PrimaryHex != settings.PrimaryHex || previous.SecondaryHex != settings.SecondaryHex)
            manual = ManualPalette(settings);
        bool albumBefore = album is not null && ReferenceEquals(palette, album);
        bool albumAfter = album is not null && UsesAlbumColors(settings);
        ShowPalette(settings, albumBefore || albumAfter ? AlbumFade : ManualEditFade);
    }

    private void ShowPalette(Settings settings, TimeSpan fade)
    {
        if (demo) return;
        Palette next = album is not null && UsesAlbumColors(settings) ? album : manual;
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        blender.SetTarget(next, fade);
        paletteChanged = true;
    }

    private void SwitchDemo(bool on, Settings settings)
    {
        demo = on;
        if (on)
        {
            demoBeat.Restart();
            demoPalette = -1;
        }
        else
        {
            ShowPalette(settings, AlbumFade);
        }
    }

    private void ShowDemoPalette(int index)
    {
        if (index == demoPalette) return;
        demoPalette = index;
        palette = DemoBeat.Palettes[index];
        blender.SetTarget(palette, DemoBeat.PaletteFade);
        paletteChanged = true;
    }

    private Settings DemoSettings(Settings settings)
    {
        if (!ReferenceEquals(settings, demoSource))
        {
            demoSource = settings;
            demoSettings = settings.Enabled && settings.Animation == AnimationMode.MusicSync
                ? settings
                : settings with { Enabled = true, Animation = AnimationMode.MusicSync };
        }
        return demoSettings!;
    }

    internal static bool UsesAlbumColors(Settings settings) =>
        settings.ColorMode == ColorMode.AlbumArt && !settings.OverrideAlbumColor;

    internal static Palette ManualPalette(Settings settings)
    {
        Rgb primary = SrgbHex.TryParse(settings.PrimaryHex, out Rgb a) ? a : Palette.Default.Primary;
        Rgb secondary = SrgbHex.TryParse(settings.SecondaryHex, out Rgb b) ? b : Palette.Default.Secondary;
        return new Palette(primary, secondary, null);
    }

    private static float ClampSensitivity(float sensitivity) =>
        float.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0.25f, 2f) : 1f;
}
