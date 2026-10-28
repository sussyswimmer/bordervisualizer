using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Tests;

// The structs the C ABI passes by pointer must keep the sizes rimlight_core.h and glow_math.h assert.
public sealed class AbiLayoutTests
{
    [Fact]
    public void StructsHaveTheHeadersSizes()
    {
        Assert.Equal(80, Unsafe.SizeOf<GlowConstants>());
        Assert.Equal(GlowConstants.SizeInBytes, Unsafe.SizeOf<GlowConstants>());
        Assert.Equal(32, Unsafe.SizeOf<SurfaceInfo>());
        Assert.Equal(16, Unsafe.SizeOf<FrameInfo>());
        Assert.Equal(24, Unsafe.SizeOf<PaceInfo>());
        Assert.Equal(52, Unsafe.SizeOf<LightState>());
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

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);
}
