using System.Runtime.CompilerServices;
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
        Assert.Equal(64, Unsafe.SizeOf<GlowConstants>());
        Assert.Equal(GlowConstants.SizeInBytes, Unsafe.SizeOf<GlowConstants>());
        Assert.Equal(16, Unsafe.SizeOf<SurfaceInfo>());
        Assert.Equal(16, Unsafe.SizeOf<FrameInfo>());
        Assert.Equal(24, Unsafe.SizeOf<PaceInfo>());
        Assert.Equal(52, Unsafe.SizeOf<LightState>());
        Assert.Equal(12, Unsafe.SizeOf<Rgb>());
    }
}
