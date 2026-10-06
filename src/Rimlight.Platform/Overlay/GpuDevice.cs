using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;

namespace Rimlight.Platform.Overlay;

// The one D3D11 device and DirectComposition device shared by every overlay (doc 04 §2), plus the glow pipeline:
// shaders, the 64-texel palette gradient and its sampler. Owned and used by the overlay thread only.
internal sealed class GpuDevice : IDisposable
{
    public const int GradientTexels = 64;

    private static readonly FeatureLevel[] FeatureLevels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private static byte[]? vertexBytecode;
    private static byte[]? pixelBytecode;

    private readonly ID3D11VertexShader vertexShader;
    private readonly ID3D11PixelShader pixelShader;
    private readonly ID3D11SamplerState sampler;
    private readonly ID3D11Texture2D gradient;
    private readonly ID3D11ShaderResourceView gradientView;
    private readonly Half[] gradientTexels = new Half[GradientTexels * 4];

    private GpuDevice(DriverType driverType)
    {
        try
        {
            D3D11.D3D11CreateDevice(null, driverType, DeviceCreationFlags.BgraSupport, FeatureLevels, out ID3D11Device device, out ID3D11DeviceContext context)
                .CheckError();
            Device = device;
            Context = context;
            IsSoftware = driverType == DriverType.Warp;

            // Swap chains must come from the factory that owns the device's adapter.
            DxgiDevice = device.QueryInterface<IDXGIDevice>();
            using (IDXGIAdapter adapter = DxgiDevice.GetAdapter())
                Factory = adapter.GetParent<IDXGIFactory2>();
            Composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(DxgiDevice);

            (byte[] vs, byte[] ps) = Bytecode();
            vertexShader = device.CreateVertexShader(vs);
            pixelShader = device.CreatePixelShader(ps);
            sampler = device.CreateSamplerState(SamplerDescription.LinearWrap);
            // 16-bit float: linear filtering of it is required on every feature level from 10.0 (32-bit float isn't).
            var description = new Texture2DDescription(Format.R16G16B16A16_Float, GradientTexels, 1, 1, 1,
                BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
            gradient = device.CreateTexture2D(in description);
            gradientView = device.CreateShaderResourceView(gradient);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ID3D11Device Device { get; } = null!;
    public ID3D11DeviceContext Context { get; } = null!;
    public IDXGIDevice DxgiDevice { get; } = null!;
    public IDXGIFactory2 Factory { get; } = null!;
    public IDCompositionDevice Composition { get; } = null!;
    public bool IsSoftware { get; }

    // A hardware device, or WARP (CPU rendering) only when no adapter supports feature level 10.0. Any other hardware
    // failure (a TDR or driver update still in progress) is rethrown, so the caller retries hardware later.
    public static GpuDevice Create()
    {
        try
        {
            return new GpuDevice(DriverType.Hardware);
        }
        catch (SharpGenException hardware) when (hardware.ResultCode == Vortice.DXGI.ResultCode.Unsupported)
        {
            Trace.WriteLine($"[{nameof(GpuDevice)}] No feature level 10.0 hardware ({hardware.Message}); using WARP.");
            return new GpuDevice(DriverType.Warp);
        }
    }

    // Cheap probe used while running on WARP: can a hardware device be created now?
    public static bool HardwareAvailable()
    {
        Result result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.None, FeatureLevels, out ID3D11Device? device);
        device?.Dispose();
        return result.Success;
    }

    // False once the D3D device was removed or DirectComposition reports its device invalid (doc 02 device loss).
    // Used when Present can't report it: a surface whose latency object stopped signaling, or DComp's WM_PAINT.
    public bool IsHealthy =>
        Device.DeviceRemovedReason.Success && Composition.CheckDeviceState(out RawBool valid).Success && valid;

    // The adapter set changed (a GPU was added, removed or its driver replaced): the device should be recreated.
    public bool IsStale => !Factory.IsCurrent;

    // The device is gone (driver update, TDR, adapter removed): everything must be recreated (doc 02).
    public static bool IsDeviceLost(Result result) =>
        result == Vortice.DXGI.ResultCode.DeviceRemoved || result == Vortice.DXGI.ResultCode.DeviceReset;

    public static bool IsDeviceLost(Exception exception) =>
        exception is SharpGenException sharpGen && IsDeviceLost(sharpGen.ResultCode);

    public Result RemovedReason => Device.DeviceRemovedReason;

    // texels: 64 x RGBA, linear, as IPaletteBlender.FillGradient writes them. Called only when the colors change.
    public void UploadGradient(ReadOnlySpan<float> texels)
    {
        for (int i = 0; i < gradientTexels.Length; i++) gradientTexels[i] = (Half)texels[i];
        Context.UpdateSubresource((ReadOnlySpan<Half>)gradientTexels, gradient, 0, GradientTexels * 8, 0);
    }

    // Binds everything every overlay draw shares. Render targets and constants are bound per surface.
    public void BindPipeline()
    {
        Context.IASetInputLayout(null);
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Context.VSSetShader(vertexShader);
        Context.PSSetShader(pixelShader);
        Context.PSSetShaderResource(0, gradientView);
        Context.PSSetSampler(0, sampler);
    }

    public void Dispose()
    {
        Context?.ClearState();
        Context?.Flush();
        gradientView?.Dispose();
        gradient?.Dispose();
        sampler?.Dispose();
        pixelShader?.Dispose();
        vertexShader?.Dispose();
        Composition?.Dispose();
        Factory?.Dispose();
        DxgiDevice?.Dispose();
        Context?.Dispose();
        Device?.Dispose();
    }

    // Compiled once per process from the embedded Glow.hlsl; device recreation reuses the bytecode.
    private static (byte[] Vertex, byte[] Pixel) Bytecode()
    {
        if (vertexBytecode is null || pixelBytecode is null)
        {
            byte[] source = LoadShaderSource();
            vertexBytecode = Compile(source, "VSMain", "vs_4_0");
            pixelBytecode = Compile(source, "PSMain", "ps_4_0");
        }
        return (vertexBytecode, pixelBytecode);
    }

    // The source is passed to D3DCompile as bytes with their true length (Vortice's string overloads convert to the
    // ANSI code page but pass the UTF-16 length, which would truncate a non-ASCII source on some code pages).
    private static byte[] LoadShaderSource()
    {
        const string name = "Rimlight.Platform.Overlay.Glow.hlsl";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded shader {name}.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static byte[] Compile(byte[] source, string entryPoint, string profile)
    {
        Result result = Compiler.Compile((ReadOnlySpan<byte>)source, null!, null!, entryPoint, "Glow.hlsl", profile,
            ShaderFlags.OptimizationLevel3, out Blob blob, out Blob errors);
        try
        {
            if (result.Failure || blob is null)
                throw new InvalidOperationException($"Glow.hlsl {entryPoint} ({profile}) failed to compile: {errors?.AsString() ?? result.Description}");
            return blob.AsBytes();
        }
        finally
        {
            blob?.Dispose();
            errors?.Dispose();
        }
    }
}
