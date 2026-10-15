using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.Platform.Tests;

/// <summary>One frame the modelled overlay loop drew.</summary>
internal readonly record struct ModelFrame(
    double Time,
    float Dt,
    FrameMotion Motion,
    Pace Pace,
    int Presents,
    LightState State,
    bool AnyVisible,
    int WindowsShown,
    bool CaptureOpen,
    double FullInterval,
    bool HalfScale);

/// <summary>
/// A model of <see cref="OverlayHost"/>'s overlay thread on a simulated clock, driving the real
/// <see cref="FramePacer"/>, the real <see cref="GlowConstants"/> and the app's real <c>MusicGlowSource</c> (through
/// <see cref="GlowRig"/>, with the real Core behind it). It mirrors, line for line, what decides when frames happen and
/// what they present: <c>Loop</c> (the first frame at once, a frame when the timer is due or an input arrives, the dt
/// rule and the steady cadence), <c>Wait</c> (Block waits for an input only), <c>ApplyPendingInputs</c> (paused
/// monitors, settings, battery, pause and frame requests, each noted on the pacer), <c>EffectivePaused</c>,
/// <c>RenderFrame</c> (per-monitor 300 ms fades, the light hidden below <see cref="GlowConstants.MinVisibility"/> after
/// one transparent present, no present while the screen already shows the frame, half scale under "Reduce") and
/// <see cref="OverlaySurface"/>'s presented-frame bookkeeping. Inputs are published the way OverlayHost's setters publish
/// them and wake the loop.
/// </summary>
/// <remarks>
/// Left out, because they need Windows: windows, the GPU and DirectComposition (every present completes at once, so
/// FrameReport.AllPresented is always true), vsync (the full rate runs on the timer at the same interval), display
/// changes, rebuilds and device loss. Keep it in step with OverlayHost.
/// </remarks>
internal sealed class OverlayLoopModel
{
    private const double MaxFrameDtSeconds = 0.25;   // OverlayHost.MaxFrameDtSeconds
    private const float MonitorFadeSeconds = 0.3f;   // OverlayHost.MonitorFadeSeconds

    private readonly GlowRig rig;
    private readonly double refreshHz;
    private readonly double firstFrameSeconds;
    private readonly FramePacer pacer = new();
    private readonly List<ModelOverlay> overlays = [];
    private readonly List<(double Time, int Order, Action Apply)> events = [];
    private int eventOrder;

    // Published inputs (OverlayHost's fields written by other threads).
    private Settings settings;
    private bool paused;
    private bool onBattery;
    private string[] pausedMonitors = [];
    private bool sourceChanged;

    // Overlay-thread state.
    private Settings? applied;
    private bool appliedPaused;
    private bool appliedOnBattery;
    private string[] appliedPausedMonitors = [];
    private Pace pace = Pace.Full;
    private FrameReport report;
    private double lastFrame;
    private double nextFrame;
    private bool first = true;
    private bool hasGradient;
    private int gradientVersion;
    private Rgb meanColor;

    /// <summary>Starts the model at time 0, as <c>OverlayHost.Start</c> does (Initialize applies the inputs once).</summary>
    /// <param name="rig">The frame source and its capture and media stand-ins.</param>
    /// <param name="settings">The settings at start.</param>
    /// <param name="monitors">The monitors' GDI names, sizes in pixels and DPI scales; the first is the primary.</param>
    /// <param name="refreshHz">The primary monitor's refresh rate.</param>
    /// <param name="firstFrameSeconds">How long the first frame takes (the GPU is set up then), so the second frame's dt.</param>
    public OverlayLoopModel(GlowRig rig, Settings settings, (string Name, int Width, int Height, float Scale)[] monitors,
        double refreshHz = 60, double firstFrameSeconds = 0)
    {
        this.rig = rig;
        this.settings = settings;
        this.refreshHz = refreshHz;
        this.firstFrameSeconds = firstFrameSeconds;
        foreach ((string name, int width, int height, float scale) in monitors) overlays.Add(new ModelOverlay(name, width, height, scale));
        ApplyPendingInputs(0); // Initialize: the windows are created, hidden, and the settings applied
    }

    /// <summary>The simulated time, in seconds.</summary>
    public double Now { get; private set; }

    /// <summary>Every frame drawn so far.</summary>
    public List<ModelFrame> Frames { get; } = [];

    /// <summary>The pacer, for its configuration.</summary>
    public FramePacer Pacer => pacer;

    /// <summary>Overlay windows shown now.</summary>
    public int WindowsShown => overlays.Count(o => o.WindowVisible);

    // OverlayHost's public inputs. Each takes effect on the loop turn the wake starts.
    public void ApplySettings(Settings next) => Publish(() => settings = next);

    public void SetPaused(bool value) => Publish(() => paused = value);

    public void SetOnBattery(bool value) => Publish(() => onBattery = value);

    public void SetPausedMonitors(params string[] names) => Publish(() => pausedMonitors = [.. names]);

    public void RequestFrame() => Publish(() => sourceChanged = true);

    /// <summary>Schedules something to happen at <paramref name="time"/> (an input, or a change of the audio).</summary>
    public void At(double time, Action action) => events.Add((time, eventOrder++, action));

    /// <summary>Runs the loop until <paramref name="end"/>.</summary>
    public void RunUntil(double end)
    {
        while (true)
        {
            double timerAt = first ? Now : pace == Pace.Block ? double.PositiveInfinity : nextFrame;
            double inputAt = events.Count > 0 ? events.Min(e => e.Time) : double.PositiveInfinity;
            double t = Math.Max(Now, Math.Min(timerAt, inputAt));
            if (t > end)
            {
                Now = end;
                return;
            }
            Now = t;

            bool frameDue;
            if (first)
            {
                frameDue = true;
                first = false;
            }
            else if (inputAt <= timerAt)
            {
                // Woken by the wake event: a frame is due only if the timer has run out too (Wait's last line).
                FireEventsDueBy(Now);
                frameDue = pace != Pace.Block && Now >= nextFrame;
            }
            else
            {
                frameDue = true;
            }
            FireEventsDueBy(Now); // changes of the audio that happen at the same time

            bool inputChanged = ApplyPendingInputs(Now);
            if (!frameDue && !inputChanged) continue;

            double elapsed = Now - lastFrame;
            double fullInterval = pacer.FullInterval;
            float dt = (float)(elapsed > MaxFrameDtSeconds ? fullInterval : frameDue ? elapsed : Math.Min(elapsed, fullInterval));
            lastFrame = Now;

            int presents = RenderFrame(dt);
            pace = pacer.Next(Now, report);
            double period = pacer.IntervalFor(pace);
            nextFrame = Now >= nextFrame && Now - nextFrame < period ? nextFrame + period : Now + period;
            Frames.Add(new ModelFrame(Now, dt, report.Motion, pace, presents, report.State, report.AnyVisible, WindowsShown,
                rig.CaptureOpen, fullInterval, Reduced));

            // The GPU is set up during the first frame, so the clock has moved on when the loop waits again.
            if (Frames.Count == 1 && firstFrameSeconds > 0) Now += firstFrameSeconds;
        }
    }

    /// <summary>Frames drawn in [from, to), per second.</summary>
    public double FramesPerSecond(double from, double to) => Frames.Count(f => f.Time >= from && f.Time < to) / (to - from);

    /// <summary>Presents in [from, to), per second, summed over every monitor.</summary>
    public double PresentsPerSecond(double from, double to) =>
        Frames.Where(f => f.Time >= from && f.Time < to).Sum(f => f.Presents) / (to - from);

    private void Publish(Action write) => At(Now, write);

    private void FireEventsDueBy(double time)
    {
        while (true)
        {
            int index = -1;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Time <= time && (index < 0 || events[i].Order < events[index].Order)) index = i;
            if (index < 0) return;
            Action apply = events[index].Apply;
            events.RemoveAt(index);
            apply();
        }
    }

    // OverlayHost.ApplyPendingInputs (paused monitors before settings; no rebuilds: the monitors are fixed).
    private bool ApplyPendingInputs(double now)
    {
        bool changed = false;
        if (!ReferenceEquals(pausedMonitors, appliedPausedMonitors))
        {
            appliedPausedMonitors = pausedMonitors;
            foreach (ModelOverlay overlay in overlays) overlay.Paused = appliedPausedMonitors.Contains(overlay.Name, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        if (!ReferenceEquals(settings, applied))
        {
            bool created = applied is null;
            applied = settings;
            ConfigurePacer();
            if (created)
            {
                // Rebuild creates the overlays: hidden, and faded out on a monitor that starts paused.
                foreach (ModelOverlay overlay in overlays) overlay.Fade = overlay.Paused ? 0 : 1;
            }
            changed = true;
        }
        if (onBattery != appliedOnBattery)
        {
            appliedOnBattery = onBattery;
            ConfigurePacer();
            changed = true;
        }
        if (paused != appliedPaused)
        {
            appliedPaused = paused;
            changed = true;
        }
        if (sourceChanged)
        {
            sourceChanged = false;
            changed = true;
        }
        if (changed) pacer.NoteInputChanged(now);
        return changed;
    }

    private bool Reduced => appliedOnBattery && applied!.OnBattery == BatteryBehavior.Reduce;

    private void ConfigurePacer() => pacer.Configure(applied!.FpsCap, refreshHz, Reduced);

    private bool EffectivePaused
    {
        get
        {
            if (appliedPaused || (appliedOnBattery && applied!.OnBattery == BatteryBehavior.Pause)) return true;
            foreach (ModelOverlay overlay in overlays)
                if (!overlay.Paused || overlay.Fade > 0) return false;
            return true;
        }
    }

    // OverlayHost.RenderFrame with a GPU that never fails.
    private int RenderFrame(float dt)
    {
        OverlayFrame frame = rig.Frame(Now, dt, applied!, EffectivePaused);
        LightState state = frame.State;
        if (frame.GradientChanged)
        {
            hasGradient = true;
            gradientVersion++;
            meanColor = GlowConstants.MeanOf(rig.Gradient);
        }

        bool transitioning = false;
        foreach (ModelOverlay overlay in overlays) transitioning |= overlay.AdvanceFade(dt);

        int presents = 0;
        bool anyVisible = false;
        bool gpuReady = hasGradient;
        if (gpuReady)
            foreach (ModelOverlay overlay in overlays) overlay.SetHalfScale(Reduced);

        float visibility = float.IsFinite(state.Visibility) ? Math.Clamp(state.Visibility, 0, 1) : 0;
        foreach (ModelOverlay overlay in overlays)
        {
            float shown = visibility * overlay.Fade;
            if (shown < GlowConstants.MinVisibility)
            {
                if (!overlay.WindowVisible) continue;
                if (gpuReady && !overlay.ShowsNothing)
                {
                    overlay.Present(Constants(state, overlay, 0), gradientVersion);
                    presents++;
                }
                overlay.WindowVisible = false;
                continue;
            }

            anyVisible = true;
            if (!gpuReady) continue;
            GlowConstants constants = Constants(state, overlay, shown);
            overlay.WindowVisible = true;
            if (overlay.Shows(constants, gradientVersion)) continue;
            overlay.Present(constants, gradientVersion);
            presents++;
        }

        report = new FrameReport(frame.Motion, state, dt, frame.GradientChanged, anyVisible, transitioning, AllPresented: true);
        return presents;
    }

    private GlowConstants Constants(in LightState state, ModelOverlay overlay, float visibility)
    {
        GlowConstants constants = GlowConstants.Create(state, overlay.BufferWidth, overlay.BufferHeight,
            overlay.Scale * overlay.RenderScale, applied!.CornerRadiusDip, meanColor);
        constants.Visibility = visibility;
        return constants;
    }

    // One monitor's overlay window and swap chain (OverlayHost.Overlay and OverlaySurface).
    private sealed class ModelOverlay(string name, int width, int height, float scale)
    {
        private readonly int windowWidth = width;
        private readonly int windowHeight = height;
        private GlowConstants lastPresented;
        private bool hasPresented;
        private int presentedGradient;

        public string Name { get; } = name;
        public float Scale { get; } = scale;
        public bool Paused { get; set; }
        public float Fade { get; set; } = 1;
        public bool WindowVisible { get; set; }
        public int BufferWidth { get; private set; } = width;
        public int BufferHeight { get; private set; } = height;
        public float RenderScale => BufferWidth / (float)windowWidth;

        public bool ShowsNothing => !hasPresented || lastPresented.Visibility < GlowConstants.MinVisibility;

        public bool AdvanceFade(float dt)
        {
            float target = Paused ? 0 : 1;
            if (Fade == target) return false;
            float step = dt / MonitorFadeSeconds;
            Fade = target > Fade ? Math.Min(target, Fade + step) : Math.Max(target, Fade - step);
            return true;
        }

        public void SetHalfScale(bool half)
        {
            int w = half ? Math.Max(1, (windowWidth + 1) / 2) : windowWidth;
            int h = half ? Math.Max(1, (windowHeight + 1) / 2) : windowHeight;
            if (w == BufferWidth && h == BufferHeight) return;
            BufferWidth = w;
            BufferHeight = h;
            hasPresented = false; // the new buffers are blank
        }

        public bool Shows(in GlowConstants frame, int gradientVersion) =>
            hasPresented && presentedGradient == gradientVersion && lastPresented.LooksLike(frame);

        public void Present(in GlowConstants frame, int gradientVersion)
        {
            lastPresented = frame;
            presentedGradient = gradientVersion;
            hasPresented = true;
        }
    }
}
