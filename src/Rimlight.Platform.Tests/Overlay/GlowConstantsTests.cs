using System.Runtime.InteropServices;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// GlowConstants (K1/K3): the light state in a surface's pixels, Glow.hlsl's constant buffer.
public sealed class GlowConstantsTests
{
    private static readonly LightState State = new(default, default, 0.6f, 0.8f, 0.45f, 6, 0.3f, 1.25f, 1);
    private static readonly Rgb Mean = new(0.2f, 0.3f, 0.4f);

    [Theory]
    [InlineData(1920, 1080, 1f, 0f, 0.45f)]
    [InlineData(2560, 1440, 1.25f, 12f, 0.45f)]
    [InlineData(3840, 2160, 2f, 40f, 1f)]
    [InlineData(1280, 800, 1.5f, 8f, 0f)]   // a hairline: the one-DIP minimum spread
    [InlineData(1366, 768, 1f, 500f, 0.9f)] // a corner radius past half the short side is clamped
    [InlineData(1365, 767, 1f, 12f, 0.45f)] // odd sizes (a work area, a window Windows sized): buffers round up
    [InlineData(2559, 1401, 1.75f, 500f, 1f)]
    [InlineData(1, 3, 1f, 8f, 0.45f)]
    public void HalfScaleHalvesEveryPixelMeasure(int width, int height, float scale, float cornerDip, float glow)
    {
        // "Reduce on battery" renders into a swap chain half the window's size (OverlaySurface.BufferSize, rounded up),
        // converts DIPs at the surface's render scale, and the visual stretches it back over the window. (The solid
        // core's screen-pixel fade, K3's review, is gone with the core: GlowProfileTests checks that the light keeps
        // its shape at half scale.)
        LightState state = State with { Spread = glow };
        int bufferWidth = OverlaySurface.BufferSize(width, half: true);
        int bufferHeight = OverlaySurface.BufferSize(height, half: true);
        float renderScale = OverlaySurface.RenderScaleFor(width, half: true);
        Assert.Equal(1f, OverlaySurface.RenderScaleFor(width, half: false));
        Assert.Equal(width, OverlaySurface.BufferSize(width, half: false));
        GlowConstants full = GlowConstants.Create(state, width, height, scale, 1, cornerDip, Mean);
        GlowConstants half = GlowConstants.Create(state, bufferWidth, bufferHeight, scale, renderScale, cornerDip, Mean);

        Assert.Equal((width + 1) / 2, half.ScreenWidthPx);
        Assert.Equal((height + 1) / 2, half.ScreenHeightPx);
        if (width % 2 == 0 && height % 2 == 0)
        {
            Assert.Equal(0.5f, renderScale);
            Assert.Equal(full.CornerRadiusPx / 2, half.CornerRadiusPx, 4);
            Assert.Equal(full.ShoulderPx / 2, half.ShoulderPx, 4);
            Assert.Equal(full.SpreadPx / 2, half.SpreadPx, 4);
            Assert.Equal(full.ColorRadiusPx / 2, half.ColorRadiusPx, 4);
            Assert.Equal(full.GlowRadiusPx / 2, half.GlowRadiusPx, 4);
        }
        // Stretched back over the window, every measure lands within a pixel of the full-scale one.
        float stretch = width / (float)bufferWidth;
        Assert.InRange(half.CornerRadiusPx * stretch - full.CornerRadiusPx, -1, 1);
        Assert.InRange(half.ShoulderPx * stretch - full.ShoulderPx, -1, 1);
        Assert.InRange(half.SpreadPx * stretch - full.SpreadPx, -1, 1);
        Assert.InRange(half.ColorRadiusPx * stretch - full.ColorRadiusPx, -1, 1);
        Assert.InRange(half.GlowRadiusPx * stretch - full.GlowRadiusPx, -1, 1);
        // Everything that isn't a length stays as it is.
        Assert.Equal(full.Gain, half.Gain);
        Assert.Equal(full.Pulse, half.Pulse);
        Assert.Equal(full.Phase, half.Phase);
        Assert.Equal(full.Visibility, half.Visibility);
        Assert.Equal(full.MeanRed, half.MeanRed);
    }

    [Fact]
    public void ThicknessIsTheShouldersWidth()
    {
        // Thickness (DIPs) is the width of the soft shoulder at the edge, in the surface's pixels; the light's
        // field is rounded to at least the shoulder plus two spreads (or the user's corner radius).
        GlowConstants c = GlowConstants.Create(State with { CoreThicknessDip = 6 }, 2880, 1620, 1.5f, 1, 0, Mean);
        Assert.Equal(9f, c.ShoulderPx, 5);
        Assert.Equal(2 * c.SpreadPx + 9, c.GlowRadiusPx, 4);
        GlowConstants cornered = GlowConstants.Create(State, 2880, 1620, 1.5f, 1, 200, Mean);
        Assert.Equal(200f * 1.5f, cornered.GlowRadiusPx, 4);
        Assert.Equal(cornered.CornerRadiusPx, cornered.GlowRadiusPx);

        // Thickness 0 keeps a shoulder of one DIP, so the light still starts flat at the edge; Thickness 1 draws the
        // same light (GlowProfile.MinShoulderDip says why the floor isn't lower).
        GlowConstants none = GlowConstants.Create(State with { CoreThicknessDip = 0 }, 2880, 1620, 1.5f, 1, 0, Mean);
        Assert.Equal(1.5f, none.ShoulderPx, 5);
        Assert.Equal(none.ShoulderPx, GlowConstants.Create(State with { CoreThicknessDip = 1 }, 2880, 1620, 1.5f, 1, 0, Mean).ShoulderPx);
    }

    [Fact]
    public void ValuesAreClampedAndTheLoopCoordinateWraps()
    {
        GlowConstants c = GlowConstants.Create(State with
        {
            Intensity = 3, Pulse = -1, Visibility = float.NaN, Phase = 1.25f, CoreThicknessDip = float.PositiveInfinity,
        }, 1920, 1080, 1, 1, float.NaN, new Rgb(float.NaN, 1, 0));
        Assert.Equal(GlowProfile.Gain(1, 0), c.Gain); // Intensity clamped to 1, Pulse to 0, then rolled off
        Assert.Equal(0, c.Pulse);
        Assert.Equal(0, c.Visibility);
        Assert.Equal(0.25f, c.Phase, 6);
        Assert.Equal(1f, c.ShoulderPx); // an infinite thickness acts as 0: the one-DIP shoulder
        Assert.Equal(0, c.CornerRadiusPx);
        Assert.Equal(0, c.MeanRed);
        Assert.Equal(-0.75f + 1, GlowConstants.Create(State with { Phase = -0.75f }, 100, 100, 1, 1, 0, Mean).Phase, 6);
    }

    [Fact]
    public void GlowMapsToSpreadAsThePrdSays()
    {
        // 0 is a hairline (one DIP); 1 spills about 35 % of the shorter side (three e-folding distances). In between
        // the slider is raised to the power 1.5 (soft edge, Maxwell's feedback round 1; K1 squared it, which put the
        // default at 25 px): the default 0.45 is about 38 px at 1080p, so the light fades in instead of hugging the
        // edge.
        Assert.Equal(1f, GlowProfile.SpreadFor(0, 1080, 1));
        Assert.Equal(2f, GlowProfile.SpreadFor(0, 2160, 2));
        Assert.Equal(0.35f * 1080 / 3, GlowProfile.SpreadFor(1, 1080, 1), 3);
        Assert.InRange(GlowProfile.SpreadFor(0.45f, 1080, 1), 37, 39);
        Assert.InRange(GlowProfile.SpreadFor(0.45f, 2160, 2), 75, 77); // a 4K screen at 200 %: the same share of it
        Assert.Equal(GlowProfile.SpreadFor(1, 1080, 1), GlowProfile.SpreadFor(7, 1080, 1));
        Assert.Equal(1f, GlowProfile.SpreadFor(float.NaN, 1080, 1));
        float previous = 0;
        for (float glow = 0; glow <= 1; glow += 0.01f)
        {
            float spread = GlowProfile.SpreadFor(glow, 1080, 1);
            Assert.True(spread >= previous, $"Glow {glow:0.00} reaches less far than the step before it");
            previous = spread;
        }
        Assert.Equal(GlowProfile.SpreadFor(0.45f, 1080, 1), GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean).SpreadPx);
    }

    [Fact]
    public void LooksLikeAllowsHalfACodeValueAndATinyPhaseShift()
    {
        GlowConstants a = GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean);
        GlowConstants b = a;
        b.Gain += 0.4f / 255;
        b.Phase += 0.0001f;
        Assert.True(a.LooksLike(b));
        b.Gain += 0.2f / 255;
        Assert.False(a.LooksLike(b));

        GlowConstants wrap = a;
        wrap.Phase = 0.99995f;
        GlowConstants zero = a;
        zero.Phase = 0.00005f;
        Assert.True(wrap.LooksLike(zero)); // 0.9999 and 0 are neighbours on the loop

        GlowConstants moved = a;
        moved.SpreadPx += 0.001f; // geometry must match exactly
        Assert.False(a.LooksLike(moved));
        GlowConstants faded = a;
        faded.Visibility -= 1f / 255;
        Assert.False(a.LooksLike(faded));
        GlowConstants narrower = a;
        narrower.ShoulderPx -= 0.001f; // so must the shoulder
        Assert.False(a.LooksLike(narrower));
        GlowConstants rounder = a;
        rounder.GlowRadiusPx += 0.001f;
        Assert.False(a.LooksLike(rounder));
    }

    [Fact]
    public void TheMeanColorIsTheAverageTexel()
    {
        float[] texels = new float[256];
        for (int i = 0; i < 64; i++)
        {
            texels[i * 4] = i < 32 ? 1 : 0;
            texels[i * 4 + 1] = 0.5f;
            texels[i * 4 + 2] = i / 63f;
            texels[i * 4 + 3] = 1;
        }
        Rgb mean = GlowConstants.MeanOf(texels);
        Assert.Equal(0.5f, mean.R, 5);
        Assert.Equal(0.5f, mean.G, 5);
        Assert.Equal(0.5f, mean.B, 5);
    }

    [Fact]
    public void TheLayoutMatchesTheShadersConstantBuffer()
    {
        // Ten registers (GlowShaderTests lays the cbuffer out field by field): the Flow movement's amount and comet
        // phase open register 5, and its four ripples take a register each, 6 to 9.
        Assert.Equal(GlowConstants.SizeInBytes, Marshal.SizeOf<GlowConstants>());
        Assert.Equal(160, GlowConstants.SizeInBytes);
        Assert.Equal(80, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.FlowAmount)));
        Assert.Equal(84, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.CometPhase)));
        Assert.Equal(88, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.FlowPad0)));
        Assert.Equal(92, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.FlowPad1)));
        Assert.Equal(96, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.Ripple0)));
        Assert.Equal(112, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.Ripple1)));
        Assert.Equal(128, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.Ripple2)));
        Assert.Equal(144, (int)Marshal.OffsetOf<GlowConstants>(nameof(GlowConstants.Ripple3)));
        Assert.Equal(16, Marshal.SizeOf<GlowRipple>());
        Assert.Equal(8, (int)Marshal.OffsetOf<GlowRipple>(nameof(GlowRipple.Strength)));
    }

    [Fact]
    public void CreateMapsTheFlowMovementCleanedForTheShader()
    {
        // LightState.Flow (H-033) lands in registers 5-9 as the shader reads it: the amount 0..1, the comet phase a
        // fraction, each ripple's origin a fraction, its radius at least 0 and its strength 0..1; not finite is 0.
        var flow = new FlowMotion(0.6f, 1.3f,
            new Ripple(0.25f, 0.1f, 0.8f),
            new Ripple(-0.25f, -1, 2),
            new Ripple(float.NaN, float.PositiveInfinity, float.NaN),
            default);
        GlowConstants c = GlowConstants.Create(State with { Flow = flow }, 1920, 1080, 1, 1, 0, Mean);
        Assert.Equal(0.6f, c.FlowAmount);
        Assert.Equal(0.3f, c.CometPhase, 6);
        Assert.Equal(0.25f, c.Ripple0.Origin);
        Assert.Equal(0.1f, c.Ripple0.Radius);
        Assert.Equal(0.8f, c.Ripple0.Strength);
        Assert.Equal(0.75f, c.Ripple1.Origin);
        Assert.Equal(0, c.Ripple1.Radius);
        Assert.Equal(1, c.Ripple1.Strength);
        Assert.Equal(default, c.Ripple2);
        Assert.Equal(default, c.Ripple3);
        Assert.Equal(0, c.FlowPad0);
        Assert.Equal(0, c.FlowPad1);
        // The gain rises with the amount (GlowProfile.FlowBoost), rolled off as ever.
        Assert.Equal(GlowProfile.Gain(0.8f, 1, 0.6f), c.Gain);
        Assert.True(c.Gain > GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean).Gain);

        // Out of range: the amount is clamped, a phase wraps, not finite is none.
        Assert.Equal(1, GlowConstants.Create(State with { Flow = flow with { Amount = 3 } }, 100, 100, 1, 1, 0, Mean).FlowAmount);
        Assert.Equal(0, GlowConstants.Create(State with { Flow = flow with { Amount = -1 } }, 100, 100, 1, 1, 0, Mean).FlowAmount);
        GlowConstants bad = GlowConstants.Create(State with { Flow = flow with { Amount = float.NaN, CometPhase = float.NaN } },
            100, 100, 1, 1, 0, Mean);
        Assert.Equal(0, bad.FlowAmount);
        Assert.Equal(0, bad.CometPhase);
        Assert.Equal(GlowProfile.Gain(0.8f, 1), bad.Gain);
        Assert.Equal(0.75f, GlowConstants.Create(State with { Flow = flow with { CometPhase = -0.25f } }, 100, 100, 1, 1, 0, Mean).CometPhase, 6);
    }

    [Fact]
    public void WithoutAFlowAmountTheConstantsAreAsBefore()
    {
        // The Pulse movement and Idle Glow have no Flow amount (default FlowMotion): every constant is what it was
        // before the Flow movement, the gain included, and the shader branches over the motion.
        GlowConstants c = GlowConstants.Create(State, 2560, 1440, 1.25f, 1, 8, Mean);
        Assert.Equal(GlowProfile.Gain(0.8f, 1), c.Gain);
        Assert.Equal(0, c.FlowAmount);
        Assert.Equal(0, c.CometPhase);
        Assert.Equal(default, c.Ripple0);
        Assert.Equal(default, c.Ripple3);
        // A comet phase and ripples without an amount are carried as they are, but the gain doesn't rise and the
        // shader branches over them.
        var still = new FlowMotion(0, 0.4f, new Ripple(0.5f, 0.1f, 1), default, default, default);
        GlowConstants idle = GlowConstants.Create(State with { Flow = still }, 2560, 1440, 1.25f, 1, 8, Mean);
        Assert.Equal(c.Gain, idle.Gain);
        Assert.Equal(0, idle.FlowAmount);
    }

    [Fact]
    public void LooksLikeAndEqualsSeeTheFlowMovement()
    {
        var flow = new FlowMotion(0.8f, 0.3f, new Ripple(0.2f, 0.05f, 0.7f), default, default, default);
        GlowConstants a = GlowConstants.Create(State with { Flow = flow }, 1920, 1080, 1, 1, 0, Mean);
        Assert.True(a.LooksLike(a));
        Assert.True(a.Equals(a));

        // Changes the eye can't see still look the same: under half a code value, a comet or a front moved by a
        // fraction of a pixel, the comet phase across the loop's seam.
        GlowConstants b = a;
        b.FlowAmount += 0.4f / 255;
        b.CometPhase += 0.0001f;
        b.Ripple0.Strength -= 0.4f / 255;
        b.Ripple0.Radius += 0.0001f;
        Assert.True(a.LooksLike(b));
        Assert.False(a.Equals(b));
        GlowConstants wrap = a;
        wrap.CometPhase = 0.99995f;
        GlowConstants zero = a;
        zero.CometPhase = 0.00005f;
        Assert.True(wrap.LooksLike(zero));

        foreach (Func<GlowConstants, GlowConstants> change in new Func<GlowConstants, GlowConstants>[]
        {
            k => k with { FlowAmount = k.FlowAmount - 1f / 255 },
            k => k with { CometPhase = k.CometPhase + 0.001f },
            k => k with { Ripple0 = k.Ripple0 with { Strength = k.Ripple0.Strength - 1f / 255 } },
            k => k with { Ripple0 = k.Ripple0 with { Radius = k.Ripple0.Radius + 0.001f } },
            k => k with { Ripple0 = k.Ripple0 with { Origin = k.Ripple0.Origin + 0.001f } },
            k => k with { Ripple0 = default },
            k => k with { Ripple3 = new GlowRipple { Origin = 0.5f, Strength = 0.5f } },
        })
        {
            GlowConstants moved = change(a);
            Assert.False(a.LooksLike(moved));
            Assert.False(a.Equals(moved));
        }

        // Two empty slots look alike wherever their unused origin and radius are.
        GlowConstants empty = a;
        empty.Ripple1 = new GlowRipple { Origin = 0.9f, Radius = 3 };
        Assert.True(a.LooksLike(empty));
    }

    [Fact]
    public void AFlowTooFaintToSeeLooksTheSameWhereverItsCometsAndRipplesAre()
    {
        // The last frames of a fade to Idle Glow: the amount is under half a code value, then the engine drops the
        // comets and ripples (default). The motion moves the alpha by at most half the amount, so none of it shows
        // and nothing needs presenting.
        var fading = new FlowMotion(0.4f / 255, 0.319f, new Ripple(0.2f, 0.3f, 0.4f), default, default, default);
        GlowConstants last = GlowConstants.Create(State with { Flow = fading }, 1920, 1080, 1, 1, 0, Mean);
        GlowConstants none = GlowConstants.Create(State with { Flow = default }, 1920, 1080, 1, 1, 0, Mean);
        Assert.True(last.LooksLike(none));
        Assert.True(none.LooksLike(last));
        Assert.False(last.Equals(none));

        // As soon as either amount can be seen, the comets and ripples count again.
        GlowConstants visible = last;
        visible.FlowAmount = 1f / 255;
        Assert.False(visible.LooksLike(none with { FlowAmount = 1f / 255 }));
    }

    [Theory]
    [InlineData(1920, 1080, 1f, 0f, 6f)]
    [InlineData(2560, 1440, 1.25f, 12f, 0f)]
    [InlineData(3024, 1964, 2f, 0f, 40f)]
    public void TheGlowStyleWithoutANotchIsTheGlowAsBefore(int width, int height, float scale, float cornerDip,
        float thickness)
    {
        // The default: no line, no notch, the top corners the user's corner radius, and the soft shoulder the
        // Thickness, so the glow draws exactly what it drew before the Line style.
        LightState state = State with { CoreThicknessDip = thickness };
        GlowConstants c = GlowConstants.Create(state, width, height, scale, 1, cornerDip, Mean);
        Assert.Equal(c, GlowConstants.Create(state, width, height, scale, 1, cornerDip, Mean, EdgeStyle.Glow, default));
        Assert.Equal(c, GlowConstants.Create(state, width, height, scale, 1, cornerDip, Mean, (EdgeStyle)7)); // no Line
        Assert.Equal(0, c.LinePx);
        Assert.Equal(c.CornerRadiusPx, c.TopCornerRadiusPx);
        Assert.Equal(0, c.NotchLeftPx);
        Assert.Equal(0, c.NotchRightPx);
        Assert.Equal(0, c.NotchHeightPx);
        Assert.Equal(0, c.NotchRadiusPx);
        Assert.Equal(GlowProfile.ShoulderFor(thickness * scale, scale), c.ShoulderPx);
    }

    [Fact]
    public void TheLineStyleTakesTheThicknessAsTheLinesWidth()
    {
        // In the Line style the Thickness is the line's width (at least one DIP) and the halo inside it has the
        // narrowest shoulder; the spread, the gain and everything else are the glow's.
        foreach (float thickness in new[] { 0f, 0.5f, 1, 6, 40 })
        {
            LightState state = State with { CoreThicknessDip = thickness };
            GlowConstants glow = GlowConstants.Create(state, 2880, 1620, 1.5f, 1, 8, Mean);
            GlowConstants line = GlowConstants.Create(state, 2880, 1620, 1.5f, 1, 8, Mean, EdgeStyle.Line);
            Assert.Equal(Math.Max(thickness, 1) * 1.5f, line.LinePx, 5);
            Assert.Equal(GlowProfile.LineFor(thickness * 1.5f, 1.5f), line.LinePx);
            Assert.Equal(1.5f, line.ShoulderPx);
            Assert.Equal(GlowProfile.GlowRadius(line.CornerRadiusPx, line.SpreadPx, 1.5f, 810), line.GlowRadiusPx);
            Assert.Equal(Math.Max(line.CornerRadiusPx, 6 * line.SpreadPx), line.ColorRadiusPx);
            Assert.Equal(glow.SpreadPx, line.SpreadPx);
            Assert.Equal(glow.Gain, line.Gain);
            Assert.Equal(glow.CornerRadiusPx, line.CornerRadiusPx);
            Assert.Equal(line.CornerRadiusPx, line.TopCornerRadiusPx);
            Assert.False(glow.LooksLike(line));
            Assert.False(glow.Equals(line));
        }

        // At half scale the line halves with every other measure; a beat doesn't widen it.
        GlowConstants full = GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean, EdgeStyle.Line);
        GlowConstants half = GlowConstants.Create(State, 960, 540, 1, 0.5f, 0, Mean, EdgeStyle.Line);
        Assert.Equal(full.LinePx / 2, half.LinePx, 5);
        GlowConstants calm = GlowConstants.Create(State with { Pulse = 0 }, 1920, 1080, 1, 1, 0, Mean, EdgeStyle.Line);
        Assert.Equal(full.LinePx, calm.LinePx);
    }

    [Fact]
    public void ANotchIsCheckedAndRoundsTheTopCorners()
    {
        // A 14-inch MacBook Pro at 2 px per point: a notch about 250 pt wide and 32 pt tall at the top's middle.
        var notch = new NotchPx(1262, 1762, 64);
        GlowConstants c = GlowConstants.Create(State, 3024, 1964, 2, 1, 0, Mean, EdgeStyle.Line, notch);
        Assert.Equal(1262f, c.NotchLeftPx);
        Assert.Equal(1762f, c.NotchRightPx);
        Assert.Equal(64f, c.NotchHeightPx);
        Assert.Equal(GlowProfile.NotchCornerShare * 64, c.NotchRadiusPx, 5);
        // Notched screens round their top corners by about 10 points; a larger corner radius setting wins.
        Assert.Equal(0, c.CornerRadiusPx);
        Assert.Equal(GlowProfile.NotchedDisplayTopCornerDip * 2, c.TopCornerRadiusPx);
        GlowConstants rounder = GlowConstants.Create(State, 3024, 1964, 2, 1, 30, Mean, EdgeStyle.Line, notch);
        Assert.Equal(60f, rounder.TopCornerRadiusPx);
        Assert.Equal(60f, rounder.CornerRadiusPx);
        // Both styles carry the notch; only the line uses the top corners.
        GlowConstants glow = GlowConstants.Create(State, 3024, 1964, 2, 1, 0, Mean, EdgeStyle.Glow, notch);
        Assert.Equal(64f, glow.NotchHeightPx);
        Assert.Equal(0, glow.LinePx);

        // A narrow notch keeps its corners within half its width; one past the screen's side is cut at it.
        Assert.Equal(5f, WithNotch(new NotchPx(100, 110, 64)).NotchRadiusPx);
        Assert.Equal(3024f, WithNotch(new NotchPx(2900, 3200, 64)).NotchRightPx);
        Assert.Equal(0f, WithNotch(new NotchPx(-50, 300, 64)).NotchLeftPx);

        // Anything that isn't a notch is none: empty, inverted, off the screen, not finite.
        foreach (NotchPx none in new NotchPx[]
        {
            default, new(1262, 1762, 0), new(1262, 1762, -5), new(1762, 1262, 64), new(1262, 1262, 64),
            new(3100, 3200, 64), new(float.NaN, 1762, 64), new(1262, float.PositiveInfinity, 64),
            new(1262, 1762, float.NaN),
        })
        {
            GlowConstants plain = WithNotch(none);
            Assert.Equal(0, plain.NotchHeightPx);
            Assert.Equal(0, plain.NotchLeftPx);
            Assert.Equal(0, plain.NotchRightPx);
            Assert.Equal(0, plain.NotchRadiusPx);
            Assert.Equal(plain.CornerRadiusPx, plain.TopCornerRadiusPx);
            Assert.Equal(GlowConstants.Create(State, 3024, 1964, 2, 1, 0, Mean, EdgeStyle.Line), plain);
        }

        static GlowConstants WithNotch(NotchPx notch) =>
            GlowConstants.Create(State, 3024, 1964, 2, 1, 0, Mean, EdgeStyle.Line, notch);
    }

    [Fact]
    public void TheLineAndTheNotchAreGeometryThatMustMatchExactly()
    {
        var notch = new NotchPx(1262, 1762, 64);
        GlowConstants a = GlowConstants.Create(State, 3024, 1964, 2, 1, 0, Mean, EdgeStyle.Line, notch);
        Assert.True(a.LooksLike(a));
        foreach (Func<GlowConstants, GlowConstants> change in new Func<GlowConstants, GlowConstants>[]
        {
            k => k with { LinePx = k.LinePx + 0.001f },
            k => k with { TopCornerRadiusPx = k.TopCornerRadiusPx + 0.001f },
            k => k with { NotchLeftPx = k.NotchLeftPx + 0.001f },
            k => k with { NotchRightPx = k.NotchRightPx - 0.001f },
            k => k with { NotchHeightPx = k.NotchHeightPx + 0.001f },
            k => k with { NotchRadiusPx = k.NotchRadiusPx + 0.001f },
        })
        {
            GlowConstants b = change(a);
            Assert.False(a.LooksLike(b));
            Assert.False(a.Equals(b));
        }
    }
}
