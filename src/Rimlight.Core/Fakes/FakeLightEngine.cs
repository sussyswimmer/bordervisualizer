namespace Rimlight.Core.Fakes;

// K0 stand-in until C6. Intensity uses the doc 07 Phase 3 formula, Brightness × (0.35 + 0.65 × Level),
// so the glow keeps its floor with the real analyzer (whose Level is 0 in quiet passages).
internal sealed class FakeLightEngine : ILightEngine
{
    public bool IsStatic { get; private set; }

    public LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)
    {
        bool hidden = paused || !settings.Enabled || settings.Animation == AnimationMode.Off;
        IsStatic = hidden;
        return new LightState(palette.Primary, palette.Secondary, settings.PrimaryRatio,
            settings.Brightness * (0.35f + 0.65f * audio.Level), settings.Glow, settings.CoreThicknessDip,
            0f, audio.Beat, hidden ? 0f : 1f);
    }
}
