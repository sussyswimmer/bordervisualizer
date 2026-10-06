using System.Numerics;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Rimlight.Platform.Overlay;

// One overlay window's composition swap chain, render target, DirectComposition target/visual and glow constants
// (doc 04 §2). Owned and used by the overlay thread only.
//
// Render scale (doc 04 §2): at half scale the swap chain is half the window's width and height and the visual
// scales it back up (linear filtering), which quarters the fill cost. The glow is soft, so it barely shows.
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
    private GlowConstants lastPresented;
    private int presentedGradient;
    private bool hasPresented;
    private bool frameAcquired;

    public OverlaySurface(GpuDevice gpu, HWND hwnd, int windowWidth, int windowHeight, bool half)
    {
        this.gpu = gpu;
        WindowWidth = windowWidth;
        WindowHeight = windowHeight;
        IsHalf = half;
        Width = BufferSize(windowWidth, half);
        Height = BufferSize(windowHeight, half);
        try
        {
            var description = new SwapChainDescription1
            {
                Width = (uint)Width,
                Height = (uint)Height,
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
            // At half scale the visual stretches the swap chain over the window: filter it, and keep its outermost
            // pixels (the core line) solid instead of fading them into the edge.
            visual.SetBitmapInterpolationMode(BitmapInterpolationMode.Linear).CheckError();
            visual.SetBorderMode(BorderMode.Hard).CheckError();
            visual.SetTransform(ScaleTransform()).CheckError();
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

    // The swap chain's size in pixels: the window's, or half of it at half scale.
    public int Width { get; private set; }
    public int Height { get; private set; }

    // The window's size in pixels.
    public int WindowWidth { get; private set; }
    public int WindowHeight { get; private set; }

    public bool IsHalf { get; private set; }

    // Swap chain pixels per window pixel: 1, or about 0.5 at half scale.
    public float RenderScale => Width / (float)WindowWidth;

    // Since when (Environment.TickCount64) every try to take a frame has failed: DWM is behind, the display is off,
    // or the device is lost. Null once a frame is taken.
    public long? SkippingSinceMs { get; private set; }

    // The swap chain's frame-latency object. With a maximum frame latency of 1 it is signaled once DWM has picked up
    // the previous frame, i.e. once per display refresh while frames keep coming: the overlay thread waits on it to
    // pace frames on the display (vsync). A wait that succeeds takes the frame; report it with FrameAcquired.
    public HANDLE FrameLatency => frameLatency;

    // True while this surface may present without waiting: its latency object was signaled and taken, and nothing
    // has been presented since. Waiting on the object again would then block until a present that never comes.
    public bool HoldsFrame => frameAcquired;

    // The overlay thread's wait took this surface's latency object.
    public void FrameAcquired()
    {
        frameAcquired = true;
        SkippingSinceMs = null;
    }

    // Takes the next frame if the swap chain can take it now, without waiting. False when DWM hasn't picked up the
    // previous frame yet (or the display is off): the frame is then skipped instead of queued.
    public bool TryAcquireFrame()
    {
        if (frameAcquired) return true;
        if (PInvoke.WaitForSingleObjectEx(frameLatency, 0, false) != WAIT_EVENT.WAIT_OBJECT_0)
        {
            SkippingSinceMs ??= Environment.TickCount64;
            return false;
        }
        FrameAcquired();
        return true;
    }

    // The screen already shows this picture: the last present drew constants that look the same, with the same
    // palette gradient. Nothing needs presenting.
    public bool Shows(in GlowConstants frame, int gradientVersion) =>
        hasPresented && presentedGradient == gradientVersion && lastPresented.LooksLike(frame);

    // Nothing visible is on screen: no frame yet (a new or resized swap chain is transparent), or a transparent one.
    public bool ShowsNothing => !hasPresented || lastPresented.Visibility < GlowConstants.MinVisibility;

    // The window was resized. True when the composition changed and needs a commit.
    public bool Resize(int windowWidth, int windowHeight) => Reconfigure(windowWidth, windowHeight, IsHalf);

    // Switches between full and half render scale; the new buffers are blank and the visual transform is pending.
    // Call it only while holding a frame (TryAcquireFrame), then Render and commit at once, so the new size, the
    // frame drawn for it and the transform reach DWM back to back. Clears the device context's state.
    public void SetHalfScale(bool half) => Reconfigure(WindowWidth, WindowHeight, half);

    // Draws one frame if the swap chain can take it (TryAcquireFrame), and presents it. The caller has bound the
    // shared pipeline (GpuDevice.BindPipeline). presented: false when the frame was skipped.
    public Result Render(in GlowConstants frame, int gradientVersion, out bool presented)
    {
        presented = false;
        if (!TryAcquireFrame()) return Result.Ok;

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
        Result result = swapChain.Present(1, PresentFlags.None);
        frameAcquired = false; // the present used the frame, whether or not it succeeded
        if (result.Success)
        {
            lastPresented = frame;
            presentedGradient = gradientVersion;
            hasPresented = true;
            presented = true;
        }
        return result;
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

    private static int BufferSize(int windowSize, bool half) => half ? Math.Max(1, (windowSize + 1) / 2) : windowSize;

    // Maps the swap chain onto the whole window: exactly 2x at half scale for even sizes, slightly less for odd ones.
    private Matrix3x2 ScaleTransform() => Matrix3x2.CreateScale(WindowWidth / (float)Width, WindowHeight / (float)Height);

    private bool Reconfigure(int windowWidth, int windowHeight, bool half)
    {
        if (windowWidth == WindowWidth && windowHeight == WindowHeight && half == IsHalf) return false;
        int width = BufferSize(windowWidth, half);
        int height = BufferSize(windowHeight, half);
        if (width != Width || height != Height)
        {
            // Every reference to the back buffers must be released before ResizeBuffers.
            renderTarget?.Dispose();
            renderTarget = null;
            gpu.Context.ClearState();
            gpu.Context.Flush(); // D3D11 destroys unbound views lazily; ResizeBuffers fails while any still exists
            swapChain.ResizeBuffers(BufferCount, (uint)width, (uint)height, BackBufferFormat, Flags).CheckError();
            Width = width;
            Height = height;
            hasUploaded = false;
            hasPresented = false; // the new buffers are blank
            CreateRenderTarget();
        }
        WindowWidth = windowWidth;
        WindowHeight = windowHeight;
        IsHalf = half;
        visual.SetTransform(ScaleTransform()).CheckError();
        return true;
    }

    private void CreateRenderTarget()
    {
        using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        renderTarget = gpu.Device.CreateRenderTargetView(backBuffer);
    }
}
