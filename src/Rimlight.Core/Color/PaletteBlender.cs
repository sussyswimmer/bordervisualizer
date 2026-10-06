namespace Rimlight.Core.Color;

/// <summary>
/// Doc 05 §3: crossfades the displayed palette to a new one in Oklab with an ease-in-out, and fills the 64-texel
/// perimeter gradient the glow shader samples (doc 04 §3 steps 4–5, H-008 item 1).
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> Not thread-safe and lock-free: one thread owns an instance, the render thread, and calls
/// every member there, <see cref="Current"/> included. <see cref="Update"/> and <see cref="FillGradient"/> never
/// allocate, and neither does <see cref="SetTarget"/> for a palette whose channels are already in 0..1.</para>
/// <para><b>Crossfade.</b> <see cref="SetTarget"/> starts from the colors displayed at that moment, so a new target
/// in the middle of a fade carries on from where the old one had got to, without a jump. Primary fades to Primary and
/// Secondary to Secondary along straight lines in Oklab, with smoothstep easing (<see cref="Ease"/>) of the elapsed
/// share of the duration. The fade ends on the <see cref="Update"/> that reaches the duration; from then on the
/// target's own linear colors are shown, exactly. A zero or negative duration, or a target with the colors already
/// displayed, switches at once.</para>
/// <para><b>Gradient.</b> Texel i is the color at perimeter coordinate u = (i + 0.5) / 64 before the shader's
/// <c>frac(t + Phase)</c> rotation; the shader samples it with WRAP addressing and linear filtering. Primary covers the
/// arc from u = 0 to u = ratio (centered on ratio / 2), Secondary the rest. Each of the two boundaries, u = ratio and
/// the seam u = 0 ≡ 1 between texels 63 and 0, is a smoothstep <see cref="BlendWidth"/> wide centered on it and mixed
/// in Oklab, so the loop is seamless and Primary's weight over the whole loop averages exactly ratio. Texels away from
/// the blends hold the displayed colors themselves. Linear RGB, alpha 1, not premultiplied. Oklab mixes that leave
/// sRGB (up to about 0.11 over in one channel, between white and saturated red) are clamped to 0..1 per channel, as
/// the shader's <c>saturate</c> would.</para>
/// </remarks>
internal sealed class PaletteBlender : IPaletteBlender
{
    /// <summary>Texels in the gradient.</summary>
    public const int TexelCount = 64;

    /// <summary>Floats the gradient needs: 64 RGBA texels.</summary>
    public const int FloatCount = TexelCount * 4;

    /// <summary>Width of each soft boundary between the two colors, as a share of the loop (doc 04 §3 step 4).</summary>
    public const float BlendWidth = 0.08f;

    /// <summary>Smallest Primary share. Below 0.1 Primary's arc would be shorter than one blend and never pure.</summary>
    public const float MinRatio = 0.1f;

    /// <summary>Largest Primary share, so Secondary keeps an arc of at least 0.1 with a pure middle.</summary>
    public const float MaxRatio = 0.9f;

    /// <summary>The share used for a NaN ratio: the <see cref="Settings.PrimaryRatio"/> default.</summary>
    public static readonly float DefaultRatio = new Settings().PrimaryRatio;

    private const float HalfBlend = BlendWidth / 2;

    private Palette target;                   // the palette last set, with channels in 0..1: Current once settled
    private Oklab fromPrimary, fromSecondary; // displayed when the fade started
    private Oklab toPrimary, toSecondary;     // the target's colors
    private double elapsed, duration;         // seconds, double so long fades keep their precision
    private bool animating;
    private Palette? blended;                 // Current during a fade, built on first read after each step

    /// <summary>Creates a blender that shows <paramref name="initial"/>, with no fade running.</summary>
    /// <param name="initial">The palette to show first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="initial"/> is null.</exception>
    public PaletteBlender(Palette initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        target = InRange(initial);
        toPrimary = fromPrimary = Oklab.FromLinearSrgb(target.Primary);
        toSecondary = fromSecondary = Oklab.FromLinearSrgb(target.Secondary);
    }

    /// <summary>
    /// The displayed palette. While no fade runs it is the palette last passed to <see cref="SetTarget"/> (or to the
    /// constructor): the same instance, unless a channel had to be clamped into 0..1. During a fade it is the blend,
    /// clamped to 0..1, with the target's <see cref="Palette.SourceTrackId"/>; reading it then allocates one
    /// <see cref="Palette"/> on the first read after each <see cref="Update"/> or <see cref="SetTarget"/> and returns
    /// that cached instance until the next one. Never allocates while idle.
    /// </summary>
    public Palette Current
    {
        get
        {
            if (!animating) return target;
            if (blended is not null) return blended;
            float progress = Progress;
            return blended = new Palette(
                ToRgb(Oklab.Lerp(fromPrimary, toPrimary, progress)),
                ToRgb(Oklab.Lerp(fromSecondary, toSecondary, progress)),
                target.SourceTrackId);
        }
    }

    /// <summary>Whether a fade is running: true from <see cref="SetTarget"/> until the <see cref="Update"/> that
    /// reaches its duration.</summary>
    public bool IsAnimating => animating;

    // Eased share of the fade done, 0..1.
    private float Progress => Ease((float)(elapsed / duration));

    /// <summary>
    /// Fades from the colors displayed now to <paramref name="target"/> over <paramref name="duration"/> (doc 05 §3:
    /// 800 ms for new album colors, 200 ms for a manual edit). Restarts the clock, so the new fade gets the whole
    /// duration whatever was running. A zero or negative duration, or a target with the colors already displayed,
    /// switches at once and leaves <see cref="IsAnimating"/> false.
    /// </summary>
    /// <param name="target">The palette to show. NaN channels count as 0; channels outside 0..1 are clamped.</param>
    /// <param name="duration">How long the fade takes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    public void SetTarget(Palette target, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(target);
        // Start from what is on screen now: the blend reached so far, or the settled target.
        if (animating)
        {
            float progress = Progress;
            fromPrimary = Oklab.Lerp(fromPrimary, toPrimary, progress);
            fromSecondary = Oklab.Lerp(fromSecondary, toSecondary, progress);
        }
        else
        {
            fromPrimary = toPrimary;
            fromSecondary = toSecondary;
        }
        this.target = InRange(target);
        toPrimary = Oklab.FromLinearSrgb(this.target.Primary);
        toSecondary = Oklab.FromLinearSrgb(this.target.Secondary);
        elapsed = 0;
        this.duration = duration.TotalSeconds;
        blended = null;
        animating = duration > TimeSpan.Zero && (fromPrimary != toPrimary || fromSecondary != toSecondary);
    }

    /// <summary>Advances a running fade by <paramref name="dtSeconds"/>. NaN, negative and zero steps are ignored;
    /// an infinite one finishes the fade. Allocation-free.</summary>
    /// <param name="dtSeconds">Seconds since the previous frame.</param>
    public void Update(float dtSeconds)
    {
        if (!animating || !(dtSeconds > 0)) return;
        elapsed += dtSeconds;
        blended = null;
        if (elapsed >= duration) animating = false; // from now on the target itself is shown
    }

    /// <summary>
    /// Writes the displayed palette as 64 RGBA texels into the first 256 floats of <paramref name="rgba64x4"/>
    /// (anything after them is left alone): texel i is the color at perimeter coordinate u = (i + 0.5) / 64, Primary
    /// on u = 0..ratio and Secondary on the rest, with Oklab blends <see cref="BlendWidth"/> wide centered on both
    /// boundaries (H-008 item 1). Linear RGB in 0..1, alpha 1, not premultiplied. Allocation-free.
    /// </summary>
    /// <param name="rgba64x4">Destination of at least 256 floats.</param>
    /// <param name="ratio">Primary's share of the loop, clamped to <see cref="MinRatio"/>..<see cref="MaxRatio"/>
    /// so both colors keep a pure middle; NaN uses <see cref="DefaultRatio"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="rgba64x4"/> holds fewer than 256 floats.</exception>
    public void FillGradient(Span<float> rgba64x4, float ratio)
    {
        if (rgba64x4.Length < FloatCount)
            throw new ArgumentException("Expected at least 256 floats (64 RGBA texels).", nameof(rgba64x4));
        ratio = ClampRatio(ratio);

        Oklab primaryLab = toPrimary, secondaryLab = toSecondary;
        Rgb primary = target.Primary, secondary = target.Secondary;
        if (animating)
        {
            float progress = Progress;
            primaryLab = Oklab.Lerp(fromPrimary, toPrimary, progress);
            secondaryLab = Oklab.Lerp(fromSecondary, toSecondary, progress);
            primary = ToRgb(primaryLab);
            secondary = ToRgb(secondaryLab);
        }

        Span<float> texels = rgba64x4[..FloatCount];
        for (int i = 0; i < TexelCount; i++)
        {
            float weight = PrimaryWeight((i + 0.5f) / TexelCount, ratio);
            // Only the texels inside the two blends (about 10 of 64) need a conversion.
            Rgb color = weight >= 1 ? primary
                : weight <= 0 ? secondary
                : ToRgb(Oklab.Lerp(secondaryLab, primaryLab, weight));
            int at = i * 4;
            texels[at] = color.R;
            texels[at + 1] = color.G;
            texels[at + 2] = color.B;
            texels[at + 3] = 1f;
        }
    }

    /// <summary>
    /// Primary's weight at perimeter coordinate <paramref name="u"/>: 1 on Primary's arc, 0 on Secondary's, and a
    /// smoothstep <see cref="BlendWidth"/> wide centered on each boundary (0.5 exactly on it). Periodic in u with period
    /// 1, so the boundary at u = 0 ≡ 1 is blended like the one at u = ratio.
    /// </summary>
    /// <param name="u">Perimeter coordinate, 0..1.</param>
    /// <param name="ratio">Primary's share, already clamped to <see cref="MinRatio"/>..<see cref="MaxRatio"/>.</param>
    /// <returns>The weight, 0..1.</returns>
    internal static float PrimaryWeight(float u, float ratio)
    {
        float half = 0.5f * ratio;
        float offset = u - half;
        offset -= MathF.Floor(offset + 0.5f); // around the loop from the middle of Primary's arc, −0.5..0.5
        float inside = half - MathF.Abs(offset); // distance to the nearer boundary, positive on Primary's arc
        return SmoothStep((inside + HalfBlend) / BlendWidth);
    }

    /// <summary>The crossfade's ease-in-out: smoothstep, 3t² − 2t³ of t clamped to 0..1. It leaves and arrives with
    /// zero speed and is half way at half time.</summary>
    /// <param name="t">Elapsed share of the duration.</param>
    /// <returns>Share of the way from the old colors to the new ones.</returns>
    internal static float Ease(float t) => SmoothStep(t);

    /// <summary>Clamps a Primary share to <see cref="MinRatio"/>..<see cref="MaxRatio"/>; NaN gives
    /// <see cref="DefaultRatio"/>.</summary>
    /// <param name="ratio">Requested share.</param>
    /// <returns>The share the gradient uses.</returns>
    internal static float ClampRatio(float ratio) =>
        float.IsNaN(ratio) ? DefaultRatio : Math.Clamp(ratio, MinRatio, MaxRatio);

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // Oklab → linear RGB, clamped per channel into 0..1.
    private static Rgb ToRgb(Oklab color)
    {
        Rgb rgb = color.ToLinearSrgb();
        return new Rgb(Math.Clamp(rgb.R, 0f, 1f), Math.Clamp(rgb.G, 0f, 1f), Math.Clamp(rgb.B, 0f, 1f));
    }

    // The palette itself when every channel is already in 0..1; otherwise a clamped copy (NaN → 0).
    private static Palette InRange(Palette palette) =>
        InRange(palette.Primary) && InRange(palette.Secondary)
            ? palette
            : palette with { Primary = Clamp(palette.Primary), Secondary = Clamp(palette.Secondary) };

    private static bool InRange(Rgb color) =>
        color.R is >= 0f and <= 1f && color.G is >= 0f and <= 1f && color.B is >= 0f and <= 1f;

    private static Rgb Clamp(Rgb color) => new(Clamp(color.R), Clamp(color.G), Clamp(color.B));

    private static float Clamp(float channel) => float.IsNaN(channel) ? 0f : Math.Clamp(channel, 0f, 1f);
}
