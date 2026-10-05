namespace Rimlight.Core.Fakes;

internal sealed class FakeLightEngine : ILightEngine
{
    public bool IsStatic { get; private set; }

    public LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)
    {
        bool hidden = paused || !settings.Enabled || settings.Animation == AnimationMode.Off;
        IsStatic = hidden;
        return new LightState(palette.Primary, palette.Secondary, settings.PrimaryRatio,
            settings.Brightness * audio.Level, settings.Glow, settings.CoreThicknessDip,
            0f, audio.Beat, hidden ? 0f : 1f);
    }
}
