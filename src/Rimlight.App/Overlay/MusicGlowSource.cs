using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Overlay;

namespace Rimlight.App.Overlay;

/// <summary>
/// The glow driven by what's playing (K2): each frame drains the loopback capture into the analyzer and maps the
/// features to a light state (doc 02 render-thread steps 1–2). Runs on the overlay thread only. K3 adds pacing,
/// idling and live settings; K4 the album-art palette.
/// </summary>
internal sealed class MusicGlowSource : IOverlayFrameSource
{
    private const int FallbackSampleRate = 48000; // used while no device is captured; only empty spans are passed then

    private readonly LoopbackCapture capture;
    private readonly IAudioAnalyzer analyzer;
    private readonly ILightEngine engine;
    private readonly IPaletteBlender blender;
    private readonly Palette palette;
    private readonly Settings settings;
    private CapturedAudio? audio;
    private float[] samples = [];
    private bool gradientFilled;

    public MusicGlowSource(Settings settings, LoopbackCapture capture)
    {
        this.settings = settings;
        this.capture = capture;
        Rgb primary = SrgbHex.TryParse(settings.PrimaryHex, out Rgb a) ? a : Palette.Default.Primary;
        Rgb secondary = SrgbHex.TryParse(settings.SecondaryHex, out Rgb b) ? b : Palette.Default.Secondary;
        palette = new Palette(primary, secondary, null);
        blender = CoreFactory.CreatePaletteBlender(palette);
        // Sensitivity is applied once, by the analyzer (H-007); K7 updates it when the setting changes.
        analyzer = CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = Math.Clamp(settings.Sensitivity, 0.25f, 2f) });
        engine = CoreFactory.CreateLightEngine();
    }

    public LightState NextFrame(float dtSeconds, Span<float> gradient, out bool gradientChanged)
    {
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
        LightState state = engine.Update(dtSeconds, in features, palette, settings, paused: false);

        gradientChanged = !gradientFilled;
        if (!gradientFilled)
        {
            blender.FillGradient(gradient, state.Ratio);
            gradientFilled = true;
        }
        return state;
    }
}
