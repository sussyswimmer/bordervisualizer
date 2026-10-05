namespace Rimlight.Core;

/// <summary>Render-thread palette transitions and gradient generation.</summary>
public interface IPaletteBlender
{
    /// <summary>Starts a transition to the target palette.</summary>
    /// <param name="target">Destination palette.</param>
    /// <param name="duration">Transition duration.</param>
    void SetTarget(Palette target, TimeSpan duration);
    /// <summary>Current interpolated palette.</summary>
    Palette Current { get; }
    /// <summary>Whether a transition remains in progress.</summary>
    bool IsAnimating { get; }
    /// <summary>Advances the transition without allocating.</summary>
    /// <param name="dtSeconds">Elapsed time in seconds.</param>
    void Update(float dtSeconds);
    /// <summary>Fills 64 linear RGBA texels without premultiplication or allocation.</summary>
    /// <param name="rgba64x4">Destination of at least 256 floats.</param>
    /// <param name="ratio">Primary color share of the gradient.</param>
    void FillGradient(Span<float> rgba64x4, float ratio);
}
