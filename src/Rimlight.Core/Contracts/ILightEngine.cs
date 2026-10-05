namespace Rimlight.Core;

/// <summary>Maps audio, colors and settings to a render state without allocating.</summary>
public interface ILightEngine
{
    /// <summary>Advances lighting on the render thread.</summary>
    /// <param name="dtSeconds">Elapsed frame time in seconds.</param>
    /// <param name="audio">Current audio features.</param>
    /// <param name="palette">Current palette.</param>
    /// <param name="settings">Immutable settings snapshot.</param>
    /// <param name="paused">Whether lighting is paused.</param>
    /// <returns>The updated render state.</returns>
    LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused);
    /// <summary>True when no visible change can occur until inputs change, allowing the renderer to idle.</summary>
    bool IsStatic { get; }
}
