using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;

namespace Rimlight.App.Overlay;

/// <summary>
/// The glow driven by what's playing: each frame drains the loopback capture into the analyzer and maps the features
/// to a light state (doc 02 render-thread steps 1–2), then tells the overlay how the glow will change so it can pace
/// itself (doc 02 "Frame pacing"). The colors are the album palette the media service publishes, or the manual colors
/// (PRD §3), crossfaded by the palette blender. Demo mode (doc 04 §5) swaps the audio and the colors for a synthetic
/// 120 BPM groove and four palettes, and the debug visualizer reads and tunes the analyzer through an
/// <see cref="AnalyzerProbe"/> (doc 03 §4). Runs on the overlay thread only, apart from the documented inputs.
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
    private AnalyzerProbe? probe; // set by the UI thread while the debug visualizer shows; read once per frame
    private int demoWanted;       // written by the UI thread, read once per frame
    private readonly DemoShow demo = new();
    private bool demoActive;
    private Settings? demoSource;  // the settings demoSettings was derived from
    private Settings? demoSettings; // those settings in Music Sync, for the engine
    private float enginePhase = float.NaN; // the engine's phase last frame, to see whether it turns the gradient itself
    private float demoPhase;

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

    /// <summary>
    /// Connects the debug visualizer (null disconnects it). While one is connected the analyzer runs on every frame,
    /// and the probe records it and applies the visualizer's tuning. Safe to call from any thread; ask the overlay for
    /// a frame afterwards (<see cref="OverlayHost.RequestFrame"/>). Pair it with <see cref="SetAudioWanted"/> so the
    /// capture stays open.
    /// </summary>
    /// <param name="probe">The visualizer's probe, or null.</param>
    public void AttachProbe(AnalyzerProbe? probe) => Volatile.Write(ref this.probe, probe);

    /// <summary>
    /// Turns demo mode on or off (doc 04 §5): a synthetic 120 BPM beat and four palettes, 6 s each, instead of the
    /// captured audio and the album or manual colors, from the first beat and palette each time it starts. The glow
    /// follows it in Music Sync whatever the Mode setting; pauses and "Glow on" still apply. Safe to call from any
    /// thread; ask the overlay for a frame afterwards.
    /// </summary>
    /// <param name="on">True for demo mode.</param>
    public void SetDemo(bool on) => Volatile.Write(ref demoWanted, on ? 1 : 0);

    /// <summary>Whether demo mode is on (as last set). Safe to read from any thread.</summary>
    public bool IsDemo => Volatile.Read(ref demoWanted) != 0;

    public OverlayFrame NextFrame(float dtSeconds, Settings settings, bool paused, Span<float> gradient)
    {
        if (!ReferenceEquals(settings, applied)) ApplySettings(settings);

        bool demoNow = Volatile.Read(ref demoWanted) != 0;
        if (demoNow != demoActive)
        {
            demoActive = demoNow;
            demo.Restart();
            enginePhase = float.NaN;
            demoPhase = 0;
            ShowPalette(settings, AlbumFade); // to the demo's first palette, or back to the album or manual colors
        }

        // A new album palette (published by reference from the thread pool, doc 02): one volatile read per frame.
        Palette? latestAlbum = media?.AlbumPalette;
        if (!ReferenceEquals(latestAlbum, album))
        {
            album = latestAlbum;
            ShowPalette(settings, AlbumFade);
        }

        // Audio matters only in music sync while the glow is on and not paused (and no demo plays), or while Settings or
        // the visualizer shows it. Otherwise the capture is closed (no audio thread work at all) and the analyzer hears
        // silence.
        bool listen = settings.Enabled && settings.Animation == AnimationMode.MusicSync && !paused && !demoActive;
        bool wanted = Volatile.Read(ref audioWanted) != 0;
        AnalyzerProbe? visualizer = Volatile.Read(ref probe);
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

        // The visualizer's tuning is applied here, on the analyzer's own thread, before it runs.
        visualizer?.ApplyPendingTuning(analyzer, ClampSensitivity(settings.Sensitivity));
        int count = audio?.Read(samples) ?? 0;
        int sampleRate = audio?.SampleRate ?? FallbackSampleRate;
        AudioFeatures features = analyzer.Process(samples.AsSpan(0, count), sampleRate, dtSeconds);
        silentSeconds = features.IsSilent ? MathF.Min(silentSeconds + dtSeconds, 3600) : 0;
        audioSequence = AudioReading.Next(audioSequence);
        Volatile.Write(ref latestAudio, AudioReading.Pack(in features, audioSequence));
        visualizer?.Record(in features, dtSeconds, analyzer, sampleRate, audio, listening, demoActive);

        AudioFeatures heard;
        Settings engineSettings = settings;
        if (demoActive)
        {
            heard = demo.Advance(dtSeconds);
            ShowPalette(settings, AlbumFade); // the next palette every 6 s, crossfaded like a track change
            engineSettings = DemoSettings(settings);
        }
        else
        {
            // Only music sync hears the audio; a meter in Settings must not change the glow.
            heard = listen ? features : Silence;
        }

        bool crossfading = blender.IsAnimating;
        blender.Update(dtSeconds);
        LightState state = engine.Update(dtSeconds, in heard, palette, engineSettings, paused);
        if (demoActive) state = TurnDemoGradient(state, dtSeconds);

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

        return new OverlayFrame(state, refill, Motion(listen, wanted, visualizer is not null, in features, settings));
    }

    // How the glow will change: the overlay draws music at the full rate, breathing at 10 fps, and stops drawing a
    // static glow (listening on at 10 Hz when music could bring it back, or while Settings shows the audio).
    private FrameMotion Motion(bool listen, bool wanted, bool visualized, in AudioFeatures features, Settings settings)
    {
        // A crossfade changes the colors every frame, even on a glow that is otherwise static. The visualizer plots
        // every analyzer frame (the overlay still drops to 10 Hz while nothing is on screen).
        if (blender.IsAnimating || visualized) return FrameMotion.Full;
        if (engine.IsStatic) return listen || wanted ? FrameMotion.Listening : FrameMotion.Still;
        if (demoActive) return FrameMotion.Full;
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

    // The demo's palette in demo mode. Otherwise the album palette in Album Art mode unless Override album color is on;
    // the manual colors otherwise, and whenever there is no album palette (no session, no art; PRD §3, doc 06 §1).
    // Allocates only when a palette is new.
    private void ShowPalette(Settings settings, TimeSpan fade)
    {
        Palette next = demoActive ? demo.Palette : album is not null && UsesAlbumColors(settings) ? album : manual;
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        blender.SetTarget(next, fade);
        paletteChanged = true;
    }

    // The demo follows its beat whatever the Mode setting: the settings in Music Sync, derived once per snapshot.
    private Settings DemoSettings(Settings settings)
    {
        if (settings.Animation == AnimationMode.MusicSync) return settings;
        if (!ReferenceEquals(settings, demoSource))
        {
            demoSource = settings;
            demoSettings = settings with { Animation = AnimationMode.MusicSync };
        }
        return demoSettings!;
    }

    // The fake light engine never turns the gradient, so while the engine's phase stands still the demo turns it itself
    // (one turn per demo loop). The real engine (C6) drifts and kicks on beats, and is left as it is.
    private LightState TurnDemoGradient(LightState state, float dtSeconds)
    {
        if (state.Phase == enginePhase && float.IsFinite(dtSeconds) && dtSeconds > 0)
            demoPhase = (demoPhase + DemoShow.DriftCyclesPerSecond * dtSeconds) % 1;
        enginePhase = state.Phase;
        if (demoPhase == 0) return state;
        float phase = state.Phase + demoPhase;
        phase -= MathF.Floor(phase);
        return state with { Phase = phase < 1 ? phase : 0 };
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
