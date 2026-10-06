using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rimlight.App.Overlay;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// The Appearance page's live preview (doc 06 §3.1): its own light engine and palette blender from
/// <see cref="CoreFactory"/>, fed the current settings, the colors the glow shows (album or manual, with the same
/// crossfades) and the analyzer's latest features from the overlay thread, drawn by <see cref="GlowRaster"/> into a
/// bitmap about 30 times a second. Runs only between <see cref="Start"/> and <see cref="Stop"/> (the page on screen).
/// UI thread only; the Core objects are this instance's own, so they are never shared with the render thread.
/// </summary>
internal sealed class GlowPreview
{
    private const int PixelWidth = 352; // the card's size in DIPs: one pixel per DIP at 100% scaling
    private const int PixelHeight = 198;
    private const double FrameSeconds = 1.0 / 30;
    private const double MaxDtSeconds = 0.1;   // after a stall, one ordinary frame (as the overlay does)
    private const double StaleAudioSeconds = 0.5; // no new frame from the overlay for this long: treat as silence

    private static readonly AudioFeatures Silence = new(0, 0, 0, true);

    private readonly AppController app;
    private readonly DispatcherTimer timer;
    private readonly WriteableBitmap bitmap;
    private readonly GlowRaster raster = new(PixelWidth, PixelHeight);
    private readonly ILightEngine engine;
    private readonly IPaletteBlender blender;
    private readonly float[] gradient = new float[256];
    private readonly Stopwatch clock = new();
    private Settings? applied;
    private Settings shown = new(); // the settings the preview draws: the glow as it looks when on
    private Palette manual;
    private Palette? album;
    private Palette palette;
    private bool paletteChanged = true;
    private float filledRatio = float.NaN;
    private double lastTick;
    private int lastSequence = -2;
    private double lastSequenceAt;
    private bool failed;

    /// <summary>Creates the preview and shows its bitmap in <paramref name="image"/>.</summary>
    public GlowPreview(AppController app, Image image)
    {
        this.app = app;
        bitmap = new WriteableBitmap(PixelWidth, PixelHeight, 96, 96, PixelFormats.Pbgra32, null);
        image.Source = bitmap;
        manual = MusicGlowSource.ManualPalette(app.SettingsService.Current);
        palette = manual;
        blender = CoreFactory.CreatePaletteBlender(palette);
        engine = CoreFactory.CreateLightEngine();
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(FrameSeconds) };
        timer.Tick += (_, _) => Tick();
    }

    /// <summary>Starts drawing (the page is on screen). Draws the first frame at once.</summary>
    public void Start()
    {
        if (timer.IsEnabled || failed) return;
        clock.Restart();
        lastTick = -FrameSeconds;
        timer.Start();
        Tick();
    }

    /// <summary>Stops drawing; nothing runs until the next <see cref="Start"/>.</summary>
    public void Stop()
    {
        timer.Stop();
        clock.Stop();
    }

    private void Tick()
    {
        try
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Clamp(now - lastTick, 0, MaxDtSeconds);
            lastTick = now;

            Settings settings = app.SettingsService.Current;
            if (!ReferenceEquals(settings, applied)) ApplySettings(settings);
            Palette? latestAlbum = app.Media?.AlbumPalette;
            if (!ReferenceEquals(latestAlbum, album))
            {
                album = latestAlbum;
                ShowPalette(MusicGlowSource.AlbumFade);
            }

            bool wasAnimating = blender.IsAnimating;
            blender.Update(dt);
            LightState state = engine.Update(dt, Audio(now), palette, shown, paused: false);
            if (paletteChanged || wasAnimating || state.Ratio != filledRatio)
            {
                blender.FillGradient(gradient, state.Ratio);
                raster.SetGradient(gradient);
                paletteChanged = false;
                filledRatio = state.Ratio;
            }

            if (raster.Render(state, settings.CornerRadiusDip))
                bitmap.WritePixels(new Int32Rect(0, 0, PixelWidth, PixelHeight), raster.Pixels, PixelWidth * 4, 0);
        }
        catch (Exception exception)
        {
            // A preview must never take the window down; it stays as it is.
            Trace.WriteLine($"[Settings] The preview failed and stopped: {exception}");
            failed = true;
            Stop();
        }
    }

    // The analyzer's latest features while music drives the glow; silence when the overlay stopped sending them
    // (the overlay isn't running, or no monitor shows it) and outside music sync, as the overlay's engine gets.
    private AudioFeatures Audio(double now)
    {
        if (shown.Animation != AnimationMode.MusicSync || app.Glow is not { } glow) return Silence;
        AudioReading reading = glow.LatestAudio;
        if (reading.Sequence != lastSequence)
        {
            lastSequence = reading.Sequence;
            lastSequenceAt = now;
        }
        return reading.Sequence >= 0 && now - lastSequenceAt < StaleAudioSeconds ? reading.Features : Silence;
    }

    private void ApplySettings(Settings settings)
    {
        Settings? previous = applied;
        applied = settings;
        // The preview shows the glow as it looks when it's on: "Glow on" off, or Motion set to Off, shows Idle Glow.
        shown = settings.Enabled && settings.Animation != AnimationMode.Off
            ? settings
            : settings with { Enabled = true, Animation = settings.Animation == AnimationMode.Off ? AnimationMode.IdleGlow : settings.Animation };
        if (previous is null || previous.PrimaryHex != settings.PrimaryHex || previous.SecondaryHex != settings.SecondaryHex)
            manual = MusicGlowSource.ManualPalette(settings);
        // The overlay's rule (MusicGlowSource): a manual edit fades quickly, a switch between album and manual colors
        // like a track change.
        bool albumBefore = album is not null && ReferenceEquals(palette, album);
        bool albumAfter = album is not null && MusicGlowSource.UsesAlbumColors(settings);
        ShowPalette(albumBefore || albumAfter ? MusicGlowSource.AlbumFade : MusicGlowSource.ManualEditFade);
    }

    private void ShowPalette(TimeSpan fade)
    {
        Palette next = album is not null && MusicGlowSource.UsesAlbumColors(shown) ? album : manual;
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        blender.SetTarget(next, fade);
        paletteChanged = true;
    }
}
