using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Overlay;

/// <summary>
/// The Appearance page's live preview (doc 06 §3.1), as the Windows GlowPreview: its own light engine and palette
/// blender, fed the current settings, the colors the glow shows (album or manual, with the same crossfades) and the
/// analyzer's latest features from the render thread. It returns the shader constants for a small card drawn as if it
/// were a 1280-point-wide screen (GlowRaster's convention), which the Mac draws with the overlay's own shader. Main
/// thread only; the Core objects are its own, never shared with the render thread.
/// </summary>
internal sealed class MacPreview
{
    /// <summary>The width, in points, of the screen the preview stands for: thickness and corner radius scale by it.</summary>
    public const float ScreenWidthPoints = 1280;

    private const double MaxDtSeconds = 0.1;
    private const double StaleAudioSeconds = 0.5;
    private static readonly AudioFeatures Silence = new(0, 0, 0, true);

    private readonly MacOverlayLoop loop;
    private readonly ILightEngine engine;
    private readonly IPaletteBlender blender;
    private readonly float[] gradient = GC.AllocateArray<float>(256, pinned: true);
    private Settings? applied;
    private Settings shown = new();
    private Palette manual;
    private Palette? album;
    private Palette palette;
    private bool paletteChanged = true;
    private float filledRatio = float.NaN;
    private Rgb meanColor;
    private double lastTick = double.NaN;
    private int lastSequence = -2;
    private double lastSequenceAt = double.NegativeInfinity;

    public MacPreview(MacOverlayLoop loop)
    {
        this.loop = loop;
        manual = MacGlowSource.ManualPalette(loop.Settings);
        palette = manual;
        blender = CoreFactory.CreatePaletteBlender(palette);
        engine = CoreFactory.CreateLightEngine();
        lastSequence = loop.Source.LatestAudio.Sequence; // older readings count as stale
    }

    /// <summary>The 64 RGBA texels of the preview's gradient: a pinned buffer whose address never changes.</summary>
    public unsafe float* Gradient => (float*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(gradient));

    /// <summary>
    /// Advances the preview to <paramref name="nowSeconds"/> and returns the constants for a card of
    /// <paramref name="widthPx"/> × <paramref name="heightPx"/> pixels. <paramref name="gradientChanged"/> is true
    /// when the gradient was refilled.
    /// </summary>
    public GlowConstants Frame(double nowSeconds, int widthPx, int heightPx, out bool gradientChanged)
    {
        float dt = double.IsNaN(lastTick) ? 0 : (float)Math.Clamp(nowSeconds - lastTick, 0, MaxDtSeconds);
        lastTick = nowSeconds;

        Settings settings = loop.Settings;
        if (!ReferenceEquals(settings, applied)) ApplySettings(settings);
        Palette? latestAlbum = loop.Source.PublishedAlbum;
        if (!ReferenceEquals(latestAlbum, album))
        {
            album = latestAlbum;
            ShowPalette(MacGlowSource.AlbumFade);
        }

        bool wasAnimating = blender.IsAnimating;
        blender.Update(dt);
        LightState state = engine.Update(dt, Audio(nowSeconds), palette, shown, paused: false);
        gradientChanged = paletteChanged || wasAnimating || state.Ratio != filledRatio;
        if (gradientChanged)
        {
            blender.FillGradient(gradient, state.Ratio);
            meanColor = GlowConstants.MeanOf(gradient);
            paletteChanged = false;
            filledRatio = state.Ratio;
        }

        float pixelsPerPoint = widthPx / ScreenWidthPoints;
        return GlowConstants.Create(state, widthPx, heightPx, pixelsPerPoint, 1, settings.CornerRadiusDip, meanColor);
    }

    // The analyzer's latest features while music drives the glow; silence when the render thread stopped sending them
    // and outside music sync, as the overlay's engine gets.
    private AudioFeatures Audio(double now)
    {
        if (shown.Animation != AnimationMode.MusicSync) return Silence;
        AudioReading reading = loop.Source.LatestAudio;
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
            manual = MacGlowSource.ManualPalette(settings);
        bool albumBefore = album is not null && ReferenceEquals(palette, album);
        bool albumAfter = album is not null && MacGlowSource.UsesAlbumColors(settings);
        ShowPalette(albumBefore || albumAfter ? MacGlowSource.AlbumFade : MacGlowSource.ManualEditFade);
    }

    private void ShowPalette(TimeSpan fade)
    {
        Palette next = album is not null && MacGlowSource.UsesAlbumColors(shown) ? album : manual;
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        blender.SetTarget(next, fade);
        paletteChanged = true;
    }
}
