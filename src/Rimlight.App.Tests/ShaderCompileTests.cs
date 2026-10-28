using System.IO;
using Rimlight.Platform.Overlay;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Xunit;

namespace Rimlight.App.Tests;

// The overlay's shader compiles the way the overlay compiles it (GpuDevice: the embedded Glow.hlsl, vs_4_0 and ps_4_0,
// optimization level 3). The compiler ships with Windows only, and nothing else in the suite runs it: a mistake in
// Glow.hlsl would otherwise pass every test and ship as a glow that never draws. Here, in the Windows job, it fails CI.
public sealed class ShaderCompileTests
{
    [WindowsTheory]
    [InlineData("VSMain", "vs_4_0")]
    [InlineData("PSMain", "ps_4_0")]
    public void TheGlowShaderCompiles(string entryPoint, string profile)
    {
        byte[] source = GlowSource();
        Result result = Compiler.Compile((ReadOnlySpan<byte>)source, null!, null!, entryPoint, "Glow.hlsl", profile,
            ShaderFlags.OptimizationLevel3, out Blob blob, out Blob errors);
        try
        {
            Assert.True(result.Success && blob is not null,
                $"Glow.hlsl {entryPoint} ({profile}) failed to compile: {errors?.AsString() ?? result.Description}");
            Assert.NotEmpty(blob!.AsBytes());
        }
        finally
        {
            blob?.Dispose();
            errors?.Dispose();
        }
    }

    // The source as the overlay loads it: the resource embedded in Rimlight.Platform.
    private static byte[] GlowSource()
    {
        const string name = "Rimlight.Platform.Overlay.Glow.hlsl";
        using Stream stream = typeof(GlowProfile).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded shader {name}.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
