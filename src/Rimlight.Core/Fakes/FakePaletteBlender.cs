namespace Rimlight.Core.Fakes;

// K0 stand-in only: target changes are immediate; perceptual blending arrives in C5.
internal sealed class FakePaletteBlender(Palette initial) : IPaletteBlender
{
    public Palette Current { get; private set; } = initial;
    public bool IsAnimating => false;
    public void SetTarget(Palette target, TimeSpan duration) => Current = target;
    public void Update(float dtSeconds) { }

    public void FillGradient(Span<float> rgba64x4, float ratio)
    {
        if (rgba64x4.Length < 256) throw new ArgumentException("Expected 64 RGBA texels.", nameof(rgba64x4));
        for (int i = 0; i < 64; i++)
        {
            Rgb color = i / 64f < ratio ? Current.Primary : Current.Secondary;
            rgba64x4[i * 4] = color.R;
            rgba64x4[i * 4 + 1] = color.G;
            rgba64x4[i * 4 + 2] = color.B;
            rgba64x4[i * 4 + 3] = 1f;
        }
    }
}
