using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Rimlight.Platform.Overlay;

// One overlay window's composition swap chain, render target, DirectComposition target/visual and glow constants
// (doc 04 §2). Owned and used by the overlay thread only.
internal sealed class OverlaySurface : IDisposable
{
    private const uint BufferCount = 2;
    private const Format BackBufferFormat = Format.B8G8R8A8_UNorm;
    private const SwapChainFlags Flags = SwapChainFlags.FrameLatencyWaitableObject;

    private readonly GpuDevice gpu;
    private readonly IDXGISwapChain1 swapChain;
    private readonly IDXGISwapChain2 swapChain2;
    private readonly IDCompositionTarget compositionTarget;
    private readonly IDCompositionVisual visual;
    private readonly ID3D11Buffer constants;
    private readonly HANDLE frameLatency;
    private ID3D11RenderTargetView? renderTarget;
    private GlowConstants uploaded;
    private bool hasUploaded;

    public OverlaySurface(GpuDevice gpu, HWND hwnd, int width, int height)
    {
        this.gpu = gpu;
        Width = width;
        Height = height;
        try
        {
            var description = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = BackBufferFormat,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = BufferCount,
                Scaling = Scaling.Stretch, // the only scaling composition swap chains support
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied,
                Flags = Flags,
            };
            swapChain = gpu.Factory.CreateSwapChainForComposition(gpu.Device, description);
            swapChain2 = swapChain.QueryInterface<IDXGISwapChain2>();
            swapChain2.MaximumFrameLatency = 1;
            frameLatency = (HANDLE)swapChain2.FrameLatencyWaitableObject;
            CreateRenderTarget();

            gpu.Composition.CreateTargetForHwnd(hwnd, true, out compositionTarget).CheckError();
            visual = gpu.Composition.CreateVisual();
            visual.SetContent(swapChain).CheckError();
            compositionTarget.SetRoot(visual).CheckError();

            constants = gpu.Device.CreateBuffer(GlowConstants.SizeInBytes, BindFlags.ConstantBuffer);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    // Consecutive frames skipped because the swap chain wasn't ready (DWM behind, display off, or a lost device).
    public int SkippedFrames { get; private set; }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;
        // Every reference to the back buffers must be released before ResizeBuffers.
        renderTarget?.Dispose();
        renderTarget = null;
        gpu.Context.ClearState();
        gpu.Context.Flush(); // D3D11 destroys unbound views lazily; ResizeBuffers fails while any still exists
        swapChain.ResizeBuffers(BufferCount, (uint)width, (uint)height, BackBufferFormat, Flags).CheckError();
        Width = width;
        Height = height;
        hasUploaded = false;
        CreateRenderTarget();
    }

    // Draws one frame if the swap chain can take it. With a maximum frame latency of 1 the waitable object is signaled
    // once the previous frame has been picked up; when it isn't (DWM is behind, or the display is off), the frame is
    // skipped instead of queued. The caller has bound the shared pipeline (GpuDevice.BindPipeline).
    public Result Render(in GlowConstants frame)
    {
        if (PInvoke.WaitForSingleObjectEx(frameLatency, 0, false) != WAIT_EVENT.WAIT_OBJECT_0)
        {
            SkippedFrames++;
            return Result.Ok;
        }
        SkippedFrames = 0;

        ID3D11DeviceContext context = gpu.Context;
        if (!hasUploaded || !frame.Equals(uploaded))
        {
            context.UpdateSubresource(in frame, constants);
            uploaded = frame;
            hasUploaded = true;
        }
        context.OMSetRenderTargets(renderTarget!);
        context.RSSetViewport(0, 0, Width, Height);
        context.PSSetConstantBuffer(0, constants);
        context.Draw(3, 0);
        return swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        compositionTarget?.SetRoot(null);
        visual?.SetContent(null);
        constants?.Dispose();
        visual?.Dispose();
        compositionTarget?.Dispose();
        renderTarget?.Dispose();
        if (!frameLatency.IsNull) PInvoke.CloseHandle(frameLatency);
        swapChain2?.Dispose();
        swapChain?.Dispose();
    }

    private void CreateRenderTarget()
    {
        using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        renderTarget = gpu.Device.CreateRenderTargetView(backBuffer);
    }
}
