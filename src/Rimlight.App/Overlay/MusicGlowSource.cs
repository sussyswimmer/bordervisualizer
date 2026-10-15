using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;

namespace Rimlight.App.Overlay;

/// <summary>
/// The glow driven by what's playing: each frame drains the loopback capture into the analyzer and maps the features
/// to a light state (doc 02 render-thread steps 1–2), then tells the overlay how the glow will change so it can pace
/// itself (doc 02 "Frame pacing"). The colors are the album palette the media service publishes, or the manual colors
/// (PRD §3), crossfaded by the palette blender. Runs on the overlay thread only.
/// </summary>
internal sealed class MusicGlowSource : IOverlayFrameSource
{
    private const int FallbackSampleRate = 48000; // used while no device is captured; only empty spans are passed then
    // In music sync, sustained silence fades to Idle Glow over 1.5 s (PRD §2); after that only slow breathing is left.
    private const float IdleSettleSeconds = 1.75f;
    // Doc 05 §3: new album colors, or a switch between album and manual colors, crossfade over 800 ms; a manual color
    // edit over 200 ms, so a color picker feels live but smooth.
    internal static readonly TimeSpan AlbumFade = TimeSpan.FromMilliseconds(800);
    internal static readonly TimeSpan ManualEditFade = TimeSpan.FromMilliseconds(200);
    // What the light engine hears while the glow doesn't follow music, also when Settings keeps the analyzer running.
    private static readonly AudioFeatures Silence = new(0, 0, 0, true);

    private readonly LoopbackCapture capture;
    private readonly NowPlayingService? media;
    private readonly IAudioAnalyzer analyzer;
    private readonly ILightEngine engine;
    private readonly IPaletteBlender blender;
    private Palette manual;  // the settings' colors
    private Palette? album;  // the album palette last read from the media service; null: none (no session, no art)
    private Palette palette; // the palette last passed to SetTarget, which the engine gets too (H-004)
    private Settings? applied;
    private CapturedAudio? audio;
    private float[] samples = [];
    private bool listening;
    private bool gradientFilled;
    private bool paletteChanged;
    private float filledRatio = float.NaN;
    private float silentSeconds;
    private int audioWanted;   // written by the UI thread (Settings' meter and preview), read once per frame
    private long latestAudio;  // AudioReading.Pack of the last frame's features, read by the UI thread
    private int audioSequence;

    /// <summary>Creates the source (on any thread); <see cref="NextFrame"/> then runs on the overlay thread.</summary>
    /// <param name="settings">The initial settings.</param>
    /// <param name="capture">The loopback capture to drain.</param>
    /// <param name="media">Publishes the album palette; null for manual colors only.</param>
    public MusicGlowSource(Settings settings, LoopbackCapture capture, NowPlayingService? media)
    {
        this.capture = capture;
        this.media = media;
        manual = ManualPalette(settings);
        palette = manual;
        blender = CoreFactory.CreatePaletteBlender(palette);
        // Sensitivity is applied once, by the analyzer (H-007): set here and on every settings change, never per frame.
        analyzer = CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = ClampSensitivity(settings.Sensitivity) });
        engine = CoreFactory.CreateLightEngine();
        applied = settings;
    }

    /// <summary>The analyzer's features from the latest frame. Safe to read from any thread.</summary>
    public AudioReading LatestAudio => AudioReading.Unpack(Volatile.Read(ref latestAudio));

    /// <summary>
    /// Keeps the loopback stream open and the analyzer running (at least 10 times a second) even when the glow doesn't
    /// need audio (Idle Glow, Off, paused), while Settings shows the level meter or the preview. Safe to call from any
    /// thread; ask the overlay for a frame afterwards (<see cref="OverlayHost.RequestFrame"/>) so a stopped loop notices.
    /// </summary>
    /// <param name="wanted">True while something shows the audio.</param>
    public void SetAudioWanted(bool wanted) => Volatile.Write(ref audioWanted, wanted ? 1 : 0);

    public OverlayFrame NextFrame(float dtSeconds, Settings settings, bool paused, Span<float> gradient)
    {
        if (!ReferenceEquals(settings, applied)) ApplySettings(settings);

        // A new album palette (published by reference from the thread pool, doc 02): one volatile read per frame.
        Palette? latestAlbum = media?.AlbumPalette;
        if (!ReferenceEquals(latestAlbum, album))
        {
            album = latestAlbum;
            ShowPalette(settings, AlbumFade);
        }

        // Audio matters only in music sync while the glow is on and not paused, or while Settings shows it. Otherwise
        // the capture is closed (no audio thread work at all) and the analyzer hears silence.
        bool listen = settings.Enabled && settings.Animation == AnimationMode.MusicSync && !paused;
        bool wanted = Volatile.Read(ref audioWanted) != 0;
        if ((listen || wanted) != listening)
        {
            listening = listen || wanted;
            capture.SetActive(listening);
        }

        CapturedAudio? latest = capture.Current;
        if (!ReferenceEquals(latest, audio))
        {
            // A new device (or none): start the analysis over (IAudioAnalyzer.Reset, "e.g. after a device change").
            // The buffer only grows here, so draining never allocates.
            audio = latest;
            analyzer.Reset();
            if (audio is not null && samples.Length < audio.Capacity) samples = new float[audio.Capacity];
        }

        int count = audio?.Read(samples) ?? 0;
        AudioFeatures features = analyzer.Process(samples.AsSpan(0, count), audio?.SampleRate ?? FallbackSampleRate, dtSeconds);
        silentSeconds = features.IsSilent ? MathF.Min(silentSeconds + dtSeconds, 3600) : 0;
        audioSequence = AudioReading.Next(audioSequence);
        Volatile.Write(ref latestAudio, AudioReading.Pack(in features, audioSequence));

        bool crossfading = blender.IsAnimating;
        blender.Update(dtSeconds);
        // Only music sync hears the audio; a meter in Settings must not change the glow.
        LightState state = engine.Update(dtSeconds, listen ? features : Silence, palette, settings, paused);

        // Refill after every SetTarget, after every update that was animating before it (so the crossfade's last
        // frame is uploaded too, H-006), and when the ratio changes.
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

    // How the glow will change: the overlay draws music at the full rate, breathing at 10 fps, and stops drawing a
    // static glow (listening on at 10 Hz when music could bring it back, or while Settings shows the audio).
    private FrameMotion Motion(bool listen, bool wanted, in AudioFeatures features, Settings settings)
    {
        // A crossfade changes the colors every frame, even on a glow that is otherwise static.
        if (blender.IsAnimating) return FrameMotion.Full;
        if (engine.IsStatic) return listen || wanted ? FrameMotion.Listening : FrameMotion.Still;
        if (!listen) return FrameMotion.Slow; // Idle Glow (or a fade the overlay sees as fast change)
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
        // A manual color edit fades quickly; a switch between album and manual colors (Color mode, Override album
        // color) fades like a track change.
        bool albumBefore = album is not null && ReferenceEquals(palette, album);
        bool albumAfter = album is not null && UsesAlbumColors(settings);
        ShowPalette(settings, albumBefore || albumAfter ? AlbumFade : ManualEditFade);
    }

    // The album palette in Album Art mode unless Override album color is on; the manual colors otherwise, and whenever
    // there is no album palette (no session, no art; PRD §3, doc 06 §1). Allocates only when a palette is new.
    private void ShowPalette(Settings settings, TimeSpan fade)
    {
        Palette next = album is not null && UsesAlbumColors(settings) ? album : manual;
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        blender.SetTarget(next, fade);
        paletteChanged = true;
    }

    /// <summary>Whether the glow shows the album colors (when there are any) rather than the manual ones.</summary>
    internal static bool UsesAlbumColors(Settings settings) =>
        settings.ColorMode == ColorMode.AlbumArt && !settings.OverrideAlbumColor;

    /// <summary>The settings' manual colors; the default palette's for a color that isn't valid hex.</summary>
    internal static Palette ManualPalette(Settings settings)
    {
        Rgb primary = SrgbHex.TryParse(settings.PrimaryHex, out Rgb a) ? a : Palette.Default.Primary;
        Rgb secondary = SrgbHex.TryParse(settings.SecondaryHex, out Rgb b) ? b : Palette.Default.Secondary;
        return new Palette(primary, secondary, null);
    }

    private static float ClampSensitivity(float sensitivity) =>
        float.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0.25f, 2f) : 1f;
}
