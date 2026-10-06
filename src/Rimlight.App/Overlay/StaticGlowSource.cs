using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.App.Overlay;

/// <summary>
/// K1's frame source: a static glow from the settings' manual colors and appearance (doc 07 Phase 1). K3 replaces it
/// with the audio → light engine pipeline.
/// </summary>
internal sealed class StaticGlowSource : IOverlayFrameSource
{
    private readonly IPaletteBlender blender;
    private readonly LightState state;
    private bool gradientFilled;

    public StaticGlowSource(Settings settings)
    {
        Rgb primary = SrgbHex.TryParse(settings.PrimaryHex, out Rgb a) ? a : Palette.Default.Primary;
        Rgb secondary = SrgbHex.TryParse(settings.SecondaryHex, out Rgb b) ? b : Palette.Default.Secondary;
        blender = CoreFactory.CreatePaletteBlender(new Palette(primary, secondary, null));
        state = new LightState(primary, secondary, settings.PrimaryRatio, settings.Brightness, settings.Glow,
            settings.CoreThicknessDip, Phase: 0, Pulse: 0, Visibility: settings.Enabled ? 1 : 0);
    }

    public LightState NextFrame(float dtSeconds, Span<float> gradient, out bool gradientChanged)
    {
        gradientChanged = !gradientFilled;
        if (!gradientFilled)
        {
            blender.FillGradient(gradient, state.Ratio);
            gradientFilled = true;
        }
        return state;
    }
}
