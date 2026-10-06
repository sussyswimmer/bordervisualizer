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
/// DirectComposition (doc 04 §1–3), paced as doc 02 "Frame pacing" asks. A dedicated overlay thread owns every window
/// and GPU object and pumps their messages. Other threads only publish inputs (settings, pause, battery, paused
/// monitors, frame requests) through single fields, so there are no locks.
/// </summary>
/// <remarks>
/// Frames come at the full rate while music drives the glow (on the display's refresh when that is the cap), at
/// 10 fps for Idle Glow breathing, at 10 Hz without presenting while the glow is static but music could change it,
/// and not at all when nothing can change until an input does: then the thread only waits for input and window
/// messages (doc 02). Overlays whose glow is fully faded out are hidden, so DWM has nothing of ours to compose.
/// </remarks>
public sealed class OverlayHost : IDisposable
{
    private const nuint RebuildTimer = 1;
    private const nuint TopmostTimer = 2;
    private const uint RebuildDelayMs = 300;   // display changes are debounced (doc 02)
    private const long TopmostThrottleMs = 250; // re-assert topmost at most this often (doc 04 §1)
    private const long GpuRetryMs = 1000;      // retry delay when the GPU can't be set up (driver update, no adapter)
    private const long HardwareProbeMs = 30_000; // while on WARP, how often to check whether hardware came back
    private const long ErrorLogIntervalMs = 5000; // repeated per-frame failures are logged at most this often
    private const float MonitorFadeSeconds = 0.3f; // a monitor's pause fades out and back in over 300 ms (doc 04 §4)
    private const double MaxFrameDtSeconds = 0.25; // a longer gap (nothing to draw, a stall) counts as one frame
    private const double VsyncFallbackSeconds = 0.1; // no refresh for this long (display off): draw anyway
    private const uint Infinite = 0xFFFFFFFF;
    private const uint TimerAllAccess = 0x1F0003;

    private static readonly WNDPROC Procedure = WindowProcedure;
    private static readonly string[] PaceNames = ["Full rate (vsync)", "Full rate (timer)", "Slow (10 fps)", "Stopped", "No overlay"];
    private static OverlayHost? current; // the running host; its thread dispatches every overlay window's messages

    private readonly IOverlayFrameSource source;
    private readonly Thread thread;
    private readonly AutoResetEvent wake = new(false);
    private readonly ManualResetEventSlim ready = new(false);
    private volatile bool stopping;
    private Exception? startupError;

    // Inputs, written by any thread and read by the overlay thread once per frame.
    private Settings settings;
    private int paused;
    private int onBattery;
    private string[] pausedMonitors = [];
    private int deviceLossRequested;
    private int sourceChanged;

    // Status, written by the overlay thread and read by any thread (diagnostics only).
    private int statusPace;
    private double statusFramesPerSecond;
    private double statusPresentsPerSecond;
    private int statusOverlays;
    private int statusVisible;
    private int statusHalf;

    // Overlay-thread state.
    private readonly List<Overlay> overlays = [];
    private readonly float[] gradient = new float[GpuDevice.GradientTexels * 4];
    private readonly FramePacer pacer = new();
    private Settings? applied;
    private bool appliedPaused;
    private bool appliedOnBattery;
    private string[] appliedPausedMonitors = [];
    private uint refreshHz; // the primary monitor's, 0 if unknown; frames are paced on it
    private Pace pace = Pace.Full;
    private FrameReport report;
    private bool frameRequested;
    private GpuDevice? gpu;
    private long gpuRetryAtMs;
    private long lastGpuReleaseMs = long.MinValue / 2;
    private long nextHardwareProbeMs;
    private long lastErrorLogMs = long.MinValue / 2;
    private bool deviceCheckRequested;
    private bool hasGradient;
    private bool gradientUploaded;
    private int gradientVersion;
    private Rgb meanColor;
    private HWND helper;
    private HANDLE frameTimer;
    private WINEVENTPROC? foregroundProcedure;
    private UnhookWinEventSafeHandle? foregroundHook;
    private long lastTopmostMs;
    private bool topmostPending;
    private long statusWindowStart;
    private int statusFrames;
    private int statusPresents;

    /// <summary>Creates the host. Call <see cref="Start"/> to show the overlays.</summary>
    /// <param name="source">Supplies the light state and palette gradient for every frame.</param>
    /// <param name="settings">The initial settings snapshot.</param>
    public OverlayHost(IOverlayFrameSource source, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        this.source = source;
        this.settings = settings;
        // The power source at start; SetOnBattery keeps it current from WM_POWERBROADCAST.
        onBattery = PowerStatus.IsOnBattery() ? 1 : 0;
        thread = new Thread(Run) { Name = "Rimlight overlay", IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>What the overlay thread is doing right now, for diagnostics. Safe to read from any thread.</summary>
    public RenderStatus Status => new(
        PaceNames[Volatile.Read(ref statusPace)],
        Volatile.Read(ref statusFramesPerSecond),
        Volatile.Read(ref statusPresentsPerSecond),
        Volatile.Read(ref statusHalf) != 0,
        Volatile.Read(ref statusOverlays),
        Volatile.Read(ref statusVisible));

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
    /// Pauses or resumes the glow everywhere (session locked, display off, fullscreen exclusive app, presentation
    /// mode). The light engine fades it out and back in. Safe to call from any thread.
    /// </summary>
    /// <param name="paused">True to pause.</param>
    public void SetPaused(bool paused)
    {
        if (Interlocked.Exchange(ref this.paused, paused ? 1 : 0) != (paused ? 1 : 0)) Wake();
    }

    /// <summary>
    /// Tells the overlay whether the machine runs on battery, so <see cref="Settings.OnBattery"/> applies ("Reduce":
    /// at most 30 fps at half render scale; "Pause": paused). Starts from <see cref="PowerStatus.IsOnBattery"/>.
    /// Safe to call from any thread.
    /// </summary>
    /// <param name="onBattery">True on battery power.</param>
    public void SetOnBattery(bool onBattery)
    {
        if (Interlocked.Exchange(ref this.onBattery, onBattery ? 1 : 0) != (onBattery ? 1 : 0)) Wake();
    }

    /// <summary>
    /// Pauses the glow on these monitors only (a fullscreen window covers them, doc 04 §4): each fades out over
    /// 300 ms and back in when it leaves the set. Safe to call from any thread.
    /// </summary>
    /// <param name="deviceNames">GDI device names of the monitors to pause (<c>MONITORINFOEX.szDevice</c>, e.g. <c>\\.\DISPLAY1</c>).</param>
    public void SetPausedMonitors(IReadOnlyCollection<string> deviceNames)
    {
        ArgumentNullException.ThrowIfNull(deviceNames);
        Volatile.Write(ref pausedMonitors, [.. deviceNames]);
        Wake();
    }

    /// <summary>
    /// Draws a frame now because something the frame source reads besides these inputs changed, such as a new album
    /// palette: a static glow draws no frames by itself. Safe to call from any thread.
    /// </summary>
    public void RequestFrame()
    {
        Interlocked.Exchange(ref sourceChanged, 1);
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

        // Creates the windows (hidden until their first visible frame). The GPU (device, shader compile, surfaces)
        // is set up by the first frame, off the caller's thread.
        ApplyPendingInputs(Stopwatch.GetTimestamp());
    }

    private unsafe void Loop()
    {
        HANDLE* handles = stackalloc HANDLE[3];
        handles[0] = frameTimer;
        handles[1] = (HANDLE)wake.SafeWaitHandle.DangerousGetHandle();
        long lastFrame = Stopwatch.GetTimestamp();
        long nextFrame = lastFrame;
        bool first = true; // the first frame comes at once

        while (!stopping)
        {
            bool frameDue = first || Wait(handles, nextFrame);
            first = false;
            if (!PumpMessages() || stopping) break;

            long now = Stopwatch.GetTimestamp();
            // One bad frame or rebuild must never end the overlay thread: log it and start the GPU over.
            try
            {
                bool inputChanged = ApplyPendingInputs(now);
                if (overlays.Count == 0)
                {
                    PublishStatus(now, 0, idle: true);
                    continue;
                }
                if (!frameDue && !inputChanged) continue;

                // The source's time runs on without jumps: a gap in frames (nothing could change, or a stall) counts
                // as one ordinary frame, so fades and smoothing continue where they stopped (doc 02 idle pacing).
                // So does the time before an input that brings a frame forward: a fade the input starts begins on
                // this frame instead of jumping ahead by the time since the last 10 Hz frame.
                double elapsed = (now - lastFrame) / (double)Stopwatch.Frequency;
                double fullInterval = pacer.FullInterval;
                float dt = (float)(elapsed > MaxFrameDtSeconds ? fullInterval : frameDue ? elapsed : Math.Min(elapsed, fullInterval));
                lastFrame = now;

                int presents = RenderFrame(dt);
                pace = pacer.Next(Seconds(now), report);
                long period = (long)(pacer.IntervalFor(pace) * Stopwatch.Frequency);
                // Keep a steady cadence; after a stall, or a frame an input brought forward, restart it from now.
                nextFrame = now >= nextFrame && now - nextFrame < period ? nextFrame + period : now + period;
                PublishStatus(now, presents, idle: false);
            }
            catch (Exception exception)
            {
                LogThrottled($"[OverlayHost] Frame failed: {exception}");
                ReleaseGpu("frame failed");
                // Retry at the full rate, never in a tight loop.
                pace = Pace.Full;
                nextFrame = Stopwatch.GetTimestamp() + (long)(pacer.FullInterval * Stopwatch.Frequency);
            }
        }
    }

    // Waits for the next frame, an input change or a window message. True when a frame is due: the timer fired at
    // nextFrame, or the display refreshed (vsync pacing).
    private unsafe bool Wait(HANDLE* handles, long nextFrame)
    {
        const QUEUE_STATUS_FLAGS AllInput = QUEUE_STATUS_FLAGS.QS_ALLINPUT;
        const MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS InputAvailable = MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE;

        // Nothing can change until an input does (or there is no overlay): only the wake event and messages end the
        // wait. No timer runs, so the thread costs nothing (doc 02 "CPU must be ~0%").
        if (overlays.Count == 0 || pace == Pace.Block)
        {
            PInvoke.MsgWaitForMultipleObjectsEx(1, handles + 1, Infinite, AllInput, InputAvailable);
            return false;
        }

        // Paced by the display: a swap chain's frame-latency object is signaled once DWM has taken the previous frame,
        // i.e. on each refresh while frames keep coming. Waiting here, not in DwmFlush, keeps the thread pumping
        // messages. The timer covers a display that stops refreshing (turned off, or DWM stalled).
        if (pace == Pace.Full && pacer.UseVsync && VsyncSurface() is { HoldsFrame: false } vsync)
        {
            handles[2] = vsync.FrameLatency;
            ArmFrameTimer((long)(VsyncFallbackSeconds * Stopwatch.Frequency));
            WAIT_EVENT result = PInvoke.MsgWaitForMultipleObjectsEx(3, handles, Infinite, AllInput, InputAvailable);
            if (result == WAIT_EVENT.WAIT_OBJECT_0 + 2)
            {
                vsync.FrameAcquired(); // the wait took the frame; Render presents without waiting again
                return true;
            }
            return result == WAIT_EVENT.WAIT_OBJECT_0;
        }

        ArmFrameTimer(nextFrame - Stopwatch.GetTimestamp());
        PInvoke.MsgWaitForMultipleObjectsEx(2, handles, Infinite, AllInput, InputAvailable);
        return Stopwatch.GetTimestamp() >= nextFrame;
    }

    // Relative due time in 100 ns units (negative = relative to now); at least 100 ns so the timer always fires.
    private unsafe void ArmFrameTimer(long stopwatchTicks)
    {
        long hundredNanoseconds = Math.Max(1, stopwatchTicks * 10_000_000 / Stopwatch.Frequency);
        long due = -hundredNanoseconds;
        PInvoke.SetWaitableTimerEx(frameTimer, &due, 0, null, null, null, 0);
    }

    private static double Seconds(long timestamp) => timestamp / (double)Stopwatch.Frequency;

    // The surface whose refresh paces frames: the primary monitor's, else the first shown one on a monitor with the
    // same refresh rate (a faster one would exceed the cap). None: pace with the timer.
    private OverlaySurface? VsyncSurface()
    {
        OverlaySurface? first = null;
        foreach (Overlay overlay in overlays)
        {
            if (overlay.Surface is not { } surface || !overlay.Window.IsVisible || overlay.Monitor.RefreshHz != refreshHz) continue;
            if (overlay.Monitor.IsPrimary) return surface;
            first ??= surface;
        }
        return first;
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

    // Takes the inputs other threads published since the last frame. True when anything changed: a frame is then
    // drawn at once, and transitions it starts get the full rate for a moment.
    private bool ApplyPendingInputs(long now)
    {
        bool changed = false;
        Settings next = Volatile.Read(ref settings);
        if (!ReferenceEquals(next, applied))
        {
            ApplySettingsSnapshot(next);
            changed = true;
        }

        bool battery = Volatile.Read(ref onBattery) != 0;
        if (battery != appliedOnBattery)
        {
            appliedOnBattery = battery;
            ConfigurePacer();
            changed = true;
        }

        bool pause = Volatile.Read(ref paused) != 0;
        if (pause != appliedPaused)
        {
            appliedPaused = pause;
            changed = true;
        }

        string[] monitors = Volatile.Read(ref pausedMonitors);
        if (!ReferenceEquals(monitors, appliedPausedMonitors))
        {
            appliedPausedMonitors = monitors;
            foreach (Overlay overlay in overlays) overlay.Paused = IsMonitorPaused(overlay.Monitor);
            changed = true;
        }

        if (Interlocked.Exchange(ref deviceLossRequested, 0) != 0)
        {
            ReleaseGpu("simulated from the tray (debug)");
            changed = true;
        }

        if (Interlocked.Exchange(ref sourceChanged, 0) != 0) changed = true;

        // Last: a rebuild above (or one run by a WM_TIMER, or a WM_PAINT) asks for a frame here.
        changed |= frameRequested;
        frameRequested = false;
        if (changed) pacer.NoteInputChanged(Seconds(now));
        return changed;
    }

    private void ApplySettingsSnapshot(Settings next)
    {
        Settings? previous = applied;
        applied = next;
        ConfigurePacer();

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

    // "Reduce on battery": at most 30 fps (PRD §5) at half render scale (doc 04 §2).
    private bool Reduced => appliedOnBattery && applied!.OnBattery == BatteryBehavior.Reduce;

    private void ConfigurePacer() => pacer.Configure(applied!.FpsCap, refreshHz, Reduced);

    // Paused everywhere: by SetPaused, by "Pause on battery", or on every monitor once their fades have finished
    // (so the fade shows whatever the engine does with the pause). The light engine fades the glow out, and frames
    // stop once it is static.
    private bool EffectivePaused
    {
        get
        {
            if (appliedPaused || (appliedOnBattery && applied!.OnBattery == BatteryBehavior.Pause)) return true;
            if (overlays.Count == 0) return false;
            foreach (Overlay overlay in overlays)
                if (!overlay.Paused || overlay.Fade > 0) return false;
            return true;
        }
    }

    private bool IsMonitorPaused(DisplayMonitor monitor)
    {
        foreach (string name in appliedPausedMonitors)
            if (string.Equals(name, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void ScheduleRebuild() => PInvoke.SetTimer(helper, RebuildTimer, RebuildDelayMs, null);

    // Matches overlays to the current monitors: removes overlays for monitors that are gone or deselected, moves and
    // resizes the rest, and creates the missing ones. Idempotent, so spurious notifications cost nothing visible.
    private void Rebuild()
    {
        PInvoke.KillTimer(helper, RebuildTimer);
        frameRequested = true; // new and moved overlays need a frame
        Settings current = applied!;
        List<DisplayMonitor> monitors = DisplayMonitors.Enumerate();
        // A display change can also mean a GPU was added or removed: then the device must be recreated.
        if (gpu is not null && gpu.IsStale) ReleaseGpu("the adapters changed");

        // Pace on the primary monitor's refresh (doc 02); a mode change arrives as WM_DISPLAYCHANGE, which rebuilds.
        refreshHz = monitors.Find(m => m.IsPrimary)?.RefreshHz ?? 0;
        ConfigurePacer();

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

        bool compositionChanged = false;
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
                    // Hidden until its first visible frame (RenderFrame shows it).
                    overlay = new Overlay(monitor, OverlayWindow.Create(bounds, current.HideFromScreenCapture));
                    overlay.Paused = IsMonitorPaused(monitor);
                    overlay.Fade = overlay.Paused ? 0 : 1;
                    overlays.Add(overlay);
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
            if (overlay.Surface is null)
            {
                TryCreateSurface(overlay);
                compositionChanged = true;
            }
            else
            {
                try
                {
                    compositionChanged |= overlay.Surface.Resize(overlay.Window.Width, overlay.Window.Height);
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
        if (gpu is not null && compositionChanged)
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
        Trace.WriteLine($"[OverlayHost] {overlays.Count} overlay(s) on {monitors.Count} monitor(s), primary at {refreshHz} Hz.");
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
            overlay.Surface = new OverlaySurface(gpu!, overlay.Window.Handle, overlay.Window.Width, overlay.Window.Height, Reduced);
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
    // has stopped signaling for 60 frames, or (on WARP) hardware that has become available again.
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

    // Draws one frame on every overlay that needs it and records what it showed in `report` for the pacer. Returns
    // the number of presents. Allocates nothing.
    private int RenderFrame(float dt)
    {
        EnsureGpu();
        // The source advances even when nothing can be drawn, so its time stays in step with the clock.
        OverlayFrame frame = source.NextFrame(dt, applied!, EffectivePaused, gradient);
        LightState state = frame.State;
        if (frame.GradientChanged)
        {
            hasGradient = true;
            gradientVersion++;
            meanColor = GlowConstants.MeanOf(gradient);
        }

        bool transitioning = false;
        foreach (Overlay overlay in overlays) transitioning |= overlay.AdvanceFade(dt);

        int presents = 0;
        bool anyVisible = false, allPresented = true, compositionChanged = false;
        bool gpuReady = gpu is not null && hasGradient && CheckGpu();
        try
        {
            if (gpuReady)
            {
                // Render scale first: resizing a swap chain clears the device context's state.
                bool half = Reduced;
                foreach (Overlay overlay in overlays)
                    if (overlay.Surface is { } scaled) compositionChanged |= scaled.SetHalfScale(half);
                if (frame.GradientChanged || !gradientUploaded)
                {
                    gpu!.UploadGradient(gradient);
                    gradientUploaded = true;
                }
                gpu!.BindPipeline();
            }

            float cornerRadiusDip = applied!.CornerRadiusDip;
            float visibility = float.IsFinite(state.Visibility) ? Math.Clamp(state.Visibility, 0, 1) : 0;
            foreach (Overlay overlay in overlays)
            {
                OverlaySurface? surface = gpuReady ? overlay.Surface : null;
                float shown = visibility * overlay.Fade; // per-monitor pause fade (doc 04 §4) on top of the engine's

                if (shown < GlowConstants.MinVisibility)
                {
                    if (!overlay.Window.IsVisible) continue;
                    // Hidden only once a transparent frame is on screen, so showing the window again never flashes
                    // an old frame.
                    if (surface is not null && !surface.ShowsNothing)
                    {
                        GlowConstants clear = Constants(state, overlay, surface, cornerRadiusDip, 0);
                        Result cleared = surface.Render(clear, gradientVersion, out bool done);
                        if (cleared.Failure)
                        {
                            ReleaseGpu($"Present returned {cleared}, removed reason {gpu!.RemovedReason}");
                            allPresented = false;
                            break;
                        }
                        if (!done)
                        {
                            allPresented = false;
                            continue;
                        }
                        presents++;
                    }
                    overlay.Window.Hide();
                    continue;
                }

                anyVisible = true;
                if (surface is null)
                {
                    // Waiting for the GPU (retried every frame); an overlay whose own surface failed waits for the
                    // next display change instead.
                    if (!gpuReady) allPresented = false;
                    continue;
                }
                GlowConstants constants = Constants(state, overlay, surface, cornerRadiusDip, shown);
                // Shown before its frame is presented: DWM may not take frames from a hidden window. Until then it
                // shows its last frame, which is transparent (see above) or blank.
                if (!overlay.Window.IsVisible) overlay.Window.Show();
                if (surface.Shows(constants, gradientVersion)) continue; // static: nothing new to present (doc 02)

                Result result = surface.Render(constants, gradientVersion, out bool presented);
                if (result.Failure)
                {
                    // DEVICE_REMOVED/RESET, or any other failure: start the GPU over (with backoff if it recurs).
                    ReleaseGpu($"Present returned {result}, removed reason {gpu!.RemovedReason}");
                    allPresented = false;
                    break;
                }
                if (presented) presents++;
                else allPresented = false;
            }
            // A render-scale change applies its visual transform together with the frame that uses it.
            if (compositionChanged && gpu is not null) CommitComposition();
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            ReleaseGpu(exception.Message);
            allPresented = false;
        }

        report = new FrameReport(frame.Motion, state, dt, frame.GradientChanged, anyVisible, transitioning, allPresented);
        return presents;
    }

    // One overlay's constants for this frame. At half scale the swap chain's pixels are twice as large, so every
    // pixel measure is converted at the surface's render scale and the shader's arithmetic stays the same.
    private GlowConstants Constants(in LightState state, Overlay overlay, OverlaySurface surface, float cornerRadiusDip, float visibility)
    {
        GlowConstants constants = GlowConstants.Create(state, surface.Width, surface.Height,
            overlay.Monitor.Scale * surface.RenderScale, cornerRadiusDip, meanColor);
        constants.Visibility = visibility;
        return constants;
    }

    // Diagnostics: frames and presents per second over the last second, and how frames are paced.
    private void PublishStatus(long now, int presents, bool idle)
    {
        int paceIndex = idle ? 4 : pace switch
        {
            Pace.Full => pacer.UseVsync ? 0 : 1,
            Pace.Slow => 2,
            _ => 3,
        };
        Volatile.Write(ref statusPace, paceIndex);
        Volatile.Write(ref statusHalf, Reduced ? 1 : 0);
        Volatile.Write(ref statusOverlays, overlays.Count);
        int visible = 0;
        foreach (Overlay overlay in overlays)
            if (overlay.Window.IsVisible) visible++;
        Volatile.Write(ref statusVisible, visible);

        if (!idle)
        {
            statusFrames++;
            statusPresents += presents;
        }
        long window = now - statusWindowStart;
        if (idle || pace == Pace.Block || window >= Stopwatch.Frequency)
        {
            // While blocked no frames come, so the rates read zero until frames resume.
            bool stopped = idle || pace == Pace.Block;
            double seconds = Math.Max(window, 1) / (double)Stopwatch.Frequency;
            Volatile.Write(ref statusFramesPerSecond, stopped ? 0 : statusFrames / seconds);
            Volatile.Write(ref statusPresentsPerSecond, stopped ? 0 : statusPresents / seconds);
            statusFrames = statusPresents = 0;
            statusWindowStart = now;
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
        foreach (Overlay overlay in overlays)
            if (overlay.Window.IsVisible) overlay.Window.ReassertTopmost(); // hidden ones go topmost when shown
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
                    // validates the window so it isn't sent again. A static glow draws no frames by itself, so the
                    // check gets one.
                    if (current is not null) current.deviceCheckRequested = current.frameRequested = true;
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

        // Paused on this monitor (SetPausedMonitors), and the fade toward it: 1 shown, 0 paused.
        public bool Paused { get; set; }
        public float Fade { get; set; } = 1;

        // Moves the fade toward its target. True while it moves (including the step that arrives).
        public bool AdvanceFade(float dt)
        {
            float target = Paused ? 0 : 1;
            if (Fade == target) return false;
            float step = dt / MonitorFadeSeconds;
            Fade = target > Fade ? Math.Min(target, Fade + step) : Math.Max(target, Fade - step);
            return true;
        }

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

/// <summary>What the overlay thread is doing (diagnostics, e.g. the Debug tray menu).</summary>
/// <param name="Pace">How frames are paced: full rate (vsync or timer), slow (10 fps), stopped, or no overlay.</param>
/// <param name="FramesPerSecond">Frames over the last second (the frame source was called this often).</param>
/// <param name="PresentsPerSecond">Presents over the last second, summed over every overlay.</param>
/// <param name="HalfScale">True while overlays render at half scale ("Reduce on battery").</param>
/// <param name="Overlays">Overlay windows (one per selected monitor).</param>
/// <param name="VisibleOverlays">Overlay windows currently shown; faded-out ones are hidden.</param>
public readonly record struct RenderStatus(string Pace, double FramesPerSecond, double PresentsPerSecond, bool HalfScale, int Overlays, int VisibleOverlays);
