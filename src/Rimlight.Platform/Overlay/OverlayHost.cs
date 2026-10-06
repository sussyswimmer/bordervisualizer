using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rimlight.Core;
using SharpGen.Runtime;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Overlay;

/// <summary>
/// Runs the glow: one click-through, topmost overlay per selected monitor, drawn with Direct3D 11 through
/// DirectComposition (doc 04 §1–3). A dedicated overlay thread owns every window and GPU object and pumps their
/// messages, so nothing here is shared with other threads except the settings snapshot and two request flags.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private const nuint RebuildTimer = 1;
    private const nuint TopmostTimer = 2;
    private const uint RebuildDelayMs = 300;   // display changes are debounced (doc 02)
    private const long TopmostThrottleMs = 250; // re-assert topmost at most this often (doc 04 §1)
    private const long GpuRetryMs = 1000;      // retry delay when the GPU can't be set up (driver update, no adapter)
    private const long HardwareProbeMs = 30_000; // while on WARP, how often to check whether hardware came back
    private const long ErrorLogIntervalMs = 5000; // repeated per-frame failures are logged at most this often
    private const int DefaultFrameRate = 60;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint TimerAllAccess = 0x1F0003;

    private static readonly WNDPROC Procedure = WindowProcedure;
    private static OverlayHost? current; // the running host; its thread dispatches every overlay window's messages

    private readonly IOverlayFrameSource source;
    private readonly Thread thread;
    private readonly AutoResetEvent wake = new(false);
    private readonly ManualResetEventSlim ready = new(false);
    private Settings settings;
    private volatile bool stopping;
    private int deviceLossRequested;
    private Exception? startupError;

    // Overlay-thread state.
    private readonly List<Overlay> overlays = [];
    private readonly float[] gradient = new float[GpuDevice.GradientTexels * 4];
    private Settings? applied;
    private GpuDevice? gpu;
    private long gpuRetryAtMs;
    private long lastGpuReleaseMs = long.MinValue / 2;
    private long nextHardwareProbeMs;
    private long lastErrorLogMs = long.MinValue / 2;
    private bool deviceCheckRequested;
    private bool hasGradient;
    private bool gradientUploaded;
    private Rgb meanColor;
    private long frameTicks = Stopwatch.Frequency / DefaultFrameRate;
    private HWND helper;
    private HANDLE frameTimer;
    private WINEVENTPROC? foregroundProcedure;
    private UnhookWinEventSafeHandle? foregroundHook;
    private long lastTopmostMs;
    private bool topmostPending;

    /// <summary>Creates the host. Call <see cref="Start"/> to show the overlays.</summary>
    /// <param name="source">Supplies the light state and palette gradient for every frame.</param>
    /// <param name="settings">The initial settings snapshot.</param>
    public OverlayHost(IOverlayFrameSource source, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        this.source = source;
        this.settings = settings;
        thread = new Thread(Run) { Name = "Rimlight overlay", IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>
    /// Starts the overlay thread and waits until its windows exist. Throws if they can't be created. GPU setup
    /// happens on the overlay thread afterwards and is retried while it fails.
    /// </summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref current, this, null) is not null)
            throw new InvalidOperationException("Only one overlay host can run at a time.");
        thread.Start();
        ready.Wait();
        if (startupError is not null)
        {
            thread.Join();
            throw new InvalidOperationException("The overlay could not start.", startupError);
        }
    }

    /// <summary>Publishes a new settings snapshot. Safe to call from any thread; applied on the next frame.</summary>
    /// <param name="settings">The new settings.</param>
    public void ApplySettings(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref this.settings, settings);
        Wake();
    }

    /// <summary>
    /// Debug aid for the device-loss path (doc 07 Phase 1): the overlay thread releases every GPU object and rebuilds
    /// them exactly as it does after <c>DXGI_ERROR_DEVICE_REMOVED</c>. Safe to call from any thread.
    /// </summary>
    public void SimulateDeviceLoss()
    {
        Interlocked.Exchange(ref deviceLossRequested, 1);
        Wake();
    }

    /// <summary>Stops the overlay thread, which removes the overlays and releases every GPU object.</summary>
    public void Dispose()
    {
        if (thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted))
        {
            wake.Dispose();
            ready.Dispose();
            return;
        }
        stopping = true;
        Wake();
        if (thread.Join(TimeSpan.FromSeconds(5)))
        {
            wake.Dispose();
            ready.Dispose();
        }
        else
        {
            Trace.WriteLine("[OverlayHost] The overlay thread did not stop within 5 s.");
        }
    }

    private void Wake()
    {
        try
        {
            wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }
    }

    private void Run()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            startupError = exception;
            Teardown();
            Interlocked.CompareExchange(ref current, null, this);
            ready.Set();
            return;
        }

        ready.Set();
        try
        {
            Loop();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[OverlayHost] The overlay thread failed: {exception}");
        }
        finally
        {
            Teardown();
            Interlocked.CompareExchange(ref current, null, this);
        }
    }

    private unsafe void Initialize()
    {
        NativeWindowClass.Register(Procedure);

        // A hidden top-level window: it receives the WM_DISPLAYCHANGE / WM_SETTINGCHANGE broadcasts (message-only
        // windows don't) and owns the debounce timers. It is never shown.
        fixed (char* className = NativeWindowClass.HelperClassName)
        fixed (char* title = "Rimlight overlay helper")
        {
            helper = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE, className, title,
                WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, default, default, NativeWindowClass.Instance, null);
        }
        if (helper.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx failed for the overlay helper window.");

        // Topmost re-assert when the foreground window changes (doc 04 §1). Out-of-context events arrive through this
        // thread's message queue, so the callback runs here.
        foregroundProcedure = OnForegroundChanged;
        foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, null,
            foregroundProcedure, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        if (foregroundHook.IsInvalid) Trace.WriteLine("[OverlayHost] SetWinEventHook failed; the overlay won't re-assert topmost.");

        // Frame pacing: a high-resolution waitable timer (Windows 10 1803+), else a regular one.
        frameTimer = PInvoke.CreateWaitableTimerEx(null, default(PCWSTR), PInvoke.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TimerAllAccess);
        if (frameTimer.IsNull) frameTimer = PInvoke.CreateWaitableTimerEx(null, default(PCWSTR), 0, TimerAllAccess);
        if (frameTimer.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWaitableTimerEx failed.");

        // Creates the windows. The GPU (device, shader compile, surfaces) is set up by the first frame, off the
        // caller's thread.
        ApplyPendingSettings();
    }

    private unsafe void Loop()
    {
        HANDLE* handles = stackalloc HANDLE[2];
        handles[0] = frameTimer;
        handles[1] = (HANDLE)wake.SafeWaitHandle.DangerousGetHandle();
        long previous = Stopwatch.GetTimestamp();
        long nextFrame = previous;

        while (!stopping)
        {
            // With no overlay (no selected monitor present) nothing can be drawn: only messages (a display change)
            // and the wake event (settings, stop) end the wait, and the frame cadence starts fresh afterwards.
            bool idle = overlays.Count == 0;
            if (!idle) ArmFrameTimer(nextFrame - Stopwatch.GetTimestamp());
            PInvoke.MsgWaitForMultipleObjectsEx(idle ? 1u : 2u, idle ? handles + 1 : handles, Infinite,
                QUEUE_STATUS_FLAGS.QS_ALLINPUT, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
            if (!PumpMessages() || stopping) break;

            long now = Stopwatch.GetTimestamp();
            if (idle) previous = nextFrame = now;
            bool frameDue = now >= nextFrame;
            if (frameDue)
            {
                // Keep a steady cadence; after a stall, restart it from now instead of rendering a burst of frames.
                nextFrame = now - nextFrame >= frameTicks ? now + frameTicks : nextFrame + frameTicks;
            }

            // One bad frame or rebuild must never end the overlay thread: log it and start the GPU over.
            try
            {
                ApplyPendingSettings();
                if (Interlocked.Exchange(ref deviceLossRequested, 0) != 0) ReleaseGpu("simulated from the tray (debug)");
                if (!frameDue || overlays.Count == 0) continue;
                float dt = (float)((now - previous) / (double)Stopwatch.Frequency);
                previous = now;
                RenderFrame(dt);
            }
            catch (Exception exception)
            {
                LogThrottled($"[OverlayHost] Frame failed: {exception}");
                ReleaseGpu("frame failed");
            }
        }
    }

    // Relative due time in 100 ns units (negative = relative to now); at least 100 ns so the timer always fires.
    private unsafe void ArmFrameTimer(long stopwatchTicks)
    {
        long hundredNanoseconds = Math.Max(1, stopwatchTicks * 10_000_000 / Stopwatch.Frequency);
        long due = -hundredNanoseconds;
        PInvoke.SetWaitableTimerEx(frameTimer, &due, 0, null, null, null, 0);
    }

    private static unsafe bool PumpMessages()
    {
        MSG message;
        while (PInvoke.PeekMessage(&message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            if (message.message == PInvoke.WM_QUIT) return false;
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
        return true;
    }

    private void ApplyPendingSettings()
    {
        Settings next = Volatile.Read(ref settings);
        if (ReferenceEquals(next, applied)) return;
        Settings? previous = applied;
        applied = next;
        frameTicks = Stopwatch.Frequency / FrameRate(next);

        bool layoutChanged = previous is null
            || next.CoverTaskbar != previous.CoverTaskbar
            || next.Monitors != previous.Monitors
            || !next.CustomMonitorIds.SequenceEqual(previous.CustomMonitorIds, StringComparer.Ordinal);
        if (layoutChanged)
        {
            Rebuild();
        }
        else if (next.HideFromScreenCapture != previous!.HideFromScreenCapture)
        {
            foreach (Overlay overlay in overlays) overlay.Window.SetCaptureExclusion(next.HideFromScreenCapture);
        }
    }

    // 30/60/120 fps caps; "native" (0) pacing with DwmFlush arrives with the K3 render loop, so it runs at 60 here.
    private static int FrameRate(Settings settings) => settings.FpsCap is 30 or 60 or 120 ? settings.FpsCap : DefaultFrameRate;

    private void ScheduleRebuild() => PInvoke.SetTimer(helper, RebuildTimer, RebuildDelayMs, null);

    // Matches overlays to the current monitors: removes overlays for monitors that are gone or deselected, moves and
    // resizes the rest, and creates the missing ones. Idempotent, so spurious notifications cost nothing visible.
    private void Rebuild()
    {
        PInvoke.KillTimer(helper, RebuildTimer);
        Settings current = applied!;
        List<DisplayMonitor> monitors = DisplayMonitors.Enumerate();
        // A display change can also mean a GPU was added or removed: then the device must be recreated.
        if (gpu is not null && gpu.IsStale) ReleaseGpu("the adapters changed");

        for (int i = overlays.Count - 1; i >= 0; i--)
        {
            Overlay overlay = overlays[i];
            DisplayMonitor? monitor = monitors.Find(m => m.DeviceName == overlay.Monitor.DeviceName);
            if (monitor is null || !IsSelected(monitor, current))
            {
                overlay.Dispose();
                overlays.RemoveAt(i);
            }
        }

        foreach (DisplayMonitor monitor in monitors)
        {
            if (!IsSelected(monitor, current)) continue;
            RECT bounds = current.CoverTaskbar ? monitor.Bounds : monitor.WorkArea;
            if (bounds.right <= bounds.left || bounds.bottom <= bounds.top) continue;

            Overlay? overlay = overlays.Find(o => o.Monitor.DeviceName == monitor.DeviceName);
            try
            {
                if (overlay is null)
                {
                    overlay = new Overlay(monitor, OverlayWindow.Create(bounds, current.HideFromScreenCapture));
                    overlays.Add(overlay);
                    overlay.Window.Show();
                }
                else
                {
                    overlay.Monitor = monitor;
                    // Always re-placed: Windows may have moved the window itself (a monitor that dropped out and
                    // came back within the debounce leaves it on another monitor at an unchanged cached rect).
                    overlay.Window.Move(bounds);
                    overlay.Window.SetCaptureExclusion(current.HideFromScreenCapture);
                }
            }
            catch (Win32Exception exception)
            {
                Trace.WriteLine($"[OverlayHost] No overlay for {monitor.DeviceName}: {exception.Message}");
                continue;
            }

            if (gpu is null) continue;
            if (overlay.Surface is null) TryCreateSurface(overlay);
            else
            {
                try
                {
                    overlay.Surface.Resize(overlay.Window.Width, overlay.Window.Height);
                }
                catch (Exception exception) when (exception is SharpGenException or COMException)
                {
                    overlay.ReleaseSurface();
                    OnSurfaceFailure(overlay, exception);
                }
            }
        }

        // Nothing to show: hold no GPU device; the first frame with an overlay creates it again.
        if (overlays.Count == 0 && gpu is not null) ReleaseGpu(null);
        if (gpu is not null)
        {
            try
            {
                CommitComposition();
            }
            catch (Exception exception) when (exception is SharpGenException or COMException)
            {
                ReleaseGpu($"commit failed: {exception.Message}");
            }
        }
        Trace.WriteLine($"[OverlayHost] {overlays.Count} overlay(s) on {monitors.Count} monitor(s).");
    }

    private static bool IsSelected(DisplayMonitor monitor, Settings settings) => settings.Monitors switch
    {
        MonitorSelection.PrimaryOnly => monitor.IsPrimary,
        MonitorSelection.Custom => settings.CustomMonitorIds.Contains(monitor.StableId, StringComparer.Ordinal),
        _ => true,
    };

    // Creates the GPU device and every overlay's surface; on failure, retries after GpuRetryMs.
    private void EnsureGpu()
    {
        if (gpu is not null || Environment.TickCount64 < gpuRetryAtMs) return;
        try
        {
            gpu = GpuDevice.Create();
            foreach (Overlay overlay in overlays)
            {
                TryCreateSurface(overlay);
                if (gpu is null) return; // the device was lost while creating surfaces; retried later
            }
            CommitComposition();
            gradientUploaded = false;
            nextHardwareProbeMs = Environment.TickCount64 + HardwareProbeMs;
            Trace.WriteLine($"[OverlayHost] GPU ready ({(gpu.IsSoftware ? "WARP" : "hardware")}).");
        }
        catch (Exception exception) when (exception is SharpGenException or COMException or InvalidOperationException)
        {
            Trace.WriteLine($"[OverlayHost] GPU setup failed, retrying in {GpuRetryMs} ms: {exception.Message}");
            ReleaseGpu(null);
            gpuRetryAtMs = Environment.TickCount64 + GpuRetryMs;
        }
    }

    private void CommitComposition() => gpu!.Composition.Commit().CheckError();

    // A surface that fails while the device is fine (an odd size, one window's DirectComposition target) leaves only
    // that monitor without a glow until the next rebuild; only a real device loss starts the whole GPU over.
    private void TryCreateSurface(Overlay overlay)
    {
        try
        {
            overlay.Surface = new OverlaySurface(gpu!, overlay.Window.Handle, overlay.Window.Width, overlay.Window.Height);
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            OnSurfaceFailure(overlay, exception);
        }
    }

    private void OnSurfaceFailure(Overlay overlay, Exception exception)
    {
        if (GpuDevice.IsDeviceLost(exception) || !gpu!.IsHealthy)
            ReleaseGpu($"surface for {overlay.Monitor.DeviceName} failed: {exception.Message}");
        else
            Trace.WriteLine($"[OverlayHost] No glow on {overlay.Monitor.DeviceName} until the next display change: {exception.Message}");
    }

    // Drops every GPU object after a device loss (or a failed setup); EnsureGpu rebuilds them on the next frame.
    // The windows stay, so nothing moves or flashes on screen except one or two missing frames. A second release
    // within GpuRetryMs (a reset still in progress, or an error that recurs every frame) waits GpuRetryMs instead.
    private void ReleaseGpu(string? reason)
    {
        if (reason is not null && gpu is not null) Trace.WriteLine($"[OverlayHost] GPU device lost ({reason}); recreating.");
        foreach (Overlay overlay in overlays) overlay.ReleaseSurface();
        gpu?.Dispose();
        gpu = null;
        long now = Environment.TickCount64;
        gpuRetryAtMs = now - lastGpuReleaseMs < GpuRetryMs ? now + GpuRetryMs : 0;
        lastGpuReleaseMs = now;
    }

    private void LogThrottled(string message)
    {
        long now = Environment.TickCount64;
        if (now - lastErrorLogMs < ErrorLogIntervalMs) return;
        lastErrorLogMs = now;
        Trace.WriteLine(message);
    }

    // Device loss that Present can't report: DirectComposition's WM_PAINT notification, a surface whose latency object
    // has stopped signaling for a second, or (on WARP) hardware that has become available again.
    private bool CheckGpu()
    {
        long now = Environment.TickCount64;
        bool check = deviceCheckRequested;
        deviceCheckRequested = false;
        foreach (Overlay overlay in overlays)
            if (overlay.Surface is { SkippedFrames: > 0 } surface && surface.SkippedFrames % 60 == 0) check = true;
        if (check && !gpu!.IsHealthy)
        {
            ReleaseGpu("the device or DirectComposition reports it lost");
            return false;
        }
        if (gpu!.IsSoftware && now >= nextHardwareProbeMs)
        {
            nextHardwareProbeMs = now + HardwareProbeMs;
            if (GpuDevice.HardwareAvailable())
            {
                ReleaseGpu("hardware rendering is available again");
                return false;
            }
        }
        return true;
    }

    private void RenderFrame(float dt)
    {
        EnsureGpu();
        // The source advances even when nothing can be drawn, so its time stays in step with the clock.
        LightState state = source.NextFrame(dt, gradient, out bool gradientChanged);
        if (gradientChanged)
        {
            hasGradient = true;
            meanColor = GlowConstants.MeanOf(gradient);
        }
        if (gpu is null || !hasGradient || !CheckGpu()) return;

        try
        {
            if (gradientChanged || !gradientUploaded)
            {
                gpu.UploadGradient(gradient);
                gradientUploaded = true;
            }
            gpu.BindPipeline();
            float cornerRadiusDip = applied!.CornerRadiusDip;
            foreach (Overlay overlay in overlays)
            {
                if (overlay.Surface is not { } surface) continue;
                var constants = GlowConstants.Create(state, surface.Width, surface.Height, overlay.Monitor.Scale, cornerRadiusDip, meanColor);
                Result result = surface.Render(constants);
                if (result.Failure)
                {
                    // DEVICE_REMOVED/RESET, or any other failure: start the GPU over (with backoff if it recurs).
                    ReleaseGpu($"Present returned {result}, removed reason {gpu.RemovedReason}");
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            ReleaseGpu(exception.Message);
        }
    }

    private void OnForegroundChanged(HWINEVENTHOOK hook, uint winEvent, HWND hwnd, int idObject, int idChild, uint idEventThread, uint eventTimeMs)
    {
        try
        {
            long elapsed = Environment.TickCount64 - lastTopmostMs;
            if (elapsed >= TopmostThrottleMs)
            {
                ReassertTopmost();
            }
            else if (!topmostPending)
            {
                // Throttled, but the last change in a burst still gets its re-assert.
                topmostPending = true;
                PInvoke.SetTimer(helper, TopmostTimer, (uint)(TopmostThrottleMs - elapsed), null);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[OverlayHost] Topmost re-assert failed: {exception.Message}");
        }
    }

    private void ReassertTopmost()
    {
        topmostPending = false;
        PInvoke.KillTimer(helper, TopmostTimer);
        lastTopmostMs = Environment.TickCount64;
        foreach (Overlay overlay in overlays) overlay.Window.ReassertTopmost();
    }

    private void OnTimer(nuint id)
    {
        if (id == RebuildTimer) Rebuild();
        else if (id == TopmostTimer) ReassertTopmost();
    }

    private void Teardown()
    {
        foreach (Overlay overlay in overlays) overlay.ReleaseSurface();
        gpu?.Dispose();
        gpu = null;
        foreach (Overlay overlay in overlays) overlay.Window.Dispose();
        overlays.Clear();
        foregroundHook?.Dispose();
        foregroundHook = null;
        if (!helper.IsNull) PInvoke.DestroyWindow(helper);
        helper = default;
        if (!frameTimer.IsNull) PInvoke.CloseHandle(frameTimer);
        frameTimer = default;
    }

    // One window procedure for the overlay and helper classes. Runs on the overlay thread, which owns both.
    private static LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case PInvoke.WM_NCHITTEST:
                    return (LRESULT)(nint)PInvoke.HTTRANSPARENT; // belt and braces with WS_EX_TRANSPARENT
                case PInvoke.WM_MOUSEACTIVATE:
                    return (LRESULT)(nint)PInvoke.MA_NOACTIVATE;
                case PInvoke.WM_ERASEBKGND:
                    return (LRESULT)1;
                case PInvoke.WM_CLOSE:
                    return default; // overlays close only with the app
                case PInvoke.WM_DPICHANGED:
                    // Ignore the suggested rectangle: overlays stay on their monitor's physical rectangle.
                    current?.ScheduleRebuild();
                    return default;
                case PInvoke.WM_DISPLAYCHANGE:
                    current?.ScheduleRebuild();
                    break;
                case PInvoke.WM_PAINT:
                    // DirectComposition signals device loss with WM_PAINT to its target windows; DefWindowProc
                    // validates the window so it isn't sent again.
                    if (current is not null) current.deviceCheckRequested = true;
                    break;
                case PInvoke.WM_SETTINGCHANGE:
                    if (wParam.Value == (nuint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA) current?.ScheduleRebuild();
                    break;
                case PInvoke.WM_TIMER:
                    current?.OnTimer(wParam.Value);
                    return default;
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[OverlayHost] Window message 0x{message:X4} failed: {exception.Message}");
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private sealed class Overlay(DisplayMonitor monitor, OverlayWindow window) : IDisposable
    {
        public DisplayMonitor Monitor { get; set; } = monitor;
        public OverlayWindow Window { get; } = window;
        public OverlaySurface? Surface { get; set; }

        public void ReleaseSurface()
        {
            Surface?.Dispose();
            Surface = null;
        }

        public void Dispose()
        {
            ReleaseSurface();
            Window.Dispose();
        }
    }
}
