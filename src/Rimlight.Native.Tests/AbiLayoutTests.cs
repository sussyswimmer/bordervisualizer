using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Tests;

// The structs the C ABI passes by pointer must keep the sizes rimlight_core.h and glow_math.h assert.
public sealed unsafe class AbiLayoutTests
{
    [Fact]
    public void StructsHaveTheHeadersSizes()
    {
        Assert.Equal(160, Unsafe.SizeOf<GlowConstants>());
        Assert.Equal(GlowConstants.SizeInBytes, Unsafe.SizeOf<GlowConstants>());
        Assert.Equal(16, Unsafe.SizeOf<GlowRipple>());
        Assert.Equal(32, Unsafe.SizeOf<SurfaceInfo>());
        Assert.Equal(16, Unsafe.SizeOf<FrameInfo>());
        Assert.Equal(24, Unsafe.SizeOf<PaceInfo>());
        Assert.Equal(108, Unsafe.SizeOf<LightState>());
        Assert.Equal(56, Unsafe.SizeOf<FlowMotion>());
        Assert.Equal(12, Unsafe.SizeOf<Ripple>());
        Assert.Equal(12, Unsafe.SizeOf<Rgb>());
        Assert.Equal(20, Unsafe.SizeOf<AudioReadingInfo>());
    }

    [Fact]
    public void TheNewFieldsSitWhereTheHeadersPutThem()
    {
        // RlGlowConstants' 20 floats: the line width replaced the first pad, the top corners the second, and the
        // notch is the fifth register. RlSurface's notch follows paused.
        Assert.Equal(44, Offset<GlowConstants>(nameof(GlowConstants.LinePx)));
        Assert.Equal(60, Offset<GlowConstants>(nameof(GlowConstants.TopCornerRadiusPx)));
        Assert.Equal(64, Offset<GlowConstants>(nameof(GlowConstants.NotchLeftPx)));
        Assert.Equal(76, Offset<GlowConstants>(nameof(GlowConstants.NotchRadiusPx)));
        Assert.Equal(16, Offset<SurfaceInfo>(nameof(SurfaceInfo.NotchLeftPt)));
        Assert.Equal(24, Offset<SurfaceInfo>(nameof(SurfaceInfo.NotchHeightPt)));
    }

    [Fact]
    public void TheFlowMovementsRegistersFollowTheNotch()
    {
        // RlGlowConstants' sixth register (flowAmount, cometPhase and two pads), then one RlGlowRipple per register.
        Assert.Equal(80, Offset<GlowConstants>(nameof(GlowConstants.FlowAmount)));
        Assert.Equal(84, Offset<GlowConstants>(nameof(GlowConstants.CometPhase)));
        Assert.Equal(96, Offset<GlowConstants>(nameof(GlowConstants.Ripple0)));
        Assert.Equal(144, Offset<GlowConstants>(nameof(GlowConstants.Ripple3)));
        Assert.Equal(8, Offset<GlowRipple>(nameof(GlowRipple.Strength)));
    }

    [Fact]
    public void TheLightStatesFlowSitsWhereRlLightStatePutsIt()
    {
        // RlLightState is LightState's 13 floats, then RlFlowMotion: the amount, the comet phase and four ripples of
        // origin, radius and strength, 27 floats in all. The C side reads it by pointer, so the managed layout itself
        // is what counts: each float is written through the record and read back by its index.
        var state = new LightState(new Rgb(1, 2, 3), new Rgb(4, 5, 6), 7, 8, 9, 10, 11, 12, 13)
        {
            Flow = new FlowMotion(14, 15, new Ripple(16, 17, 18), new Ripple(19, 20, 21), new Ripple(22, 23, 24),
                new Ripple(25, 26, 27)),
        };
        float* floats = (float*)&state;
        for (int i = 0; i < 27; i++) Assert.Equal(i + 1, floats[i]);
    }

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);
}
