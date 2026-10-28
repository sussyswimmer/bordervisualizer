using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Overlay;

/// <summary>What the render thread does with one screen's overlay this frame (<see cref="MacOverlayLoop.PlanSurface"/>).</summary>
internal enum SurfaceAction
{
    /// <summary>Nothing: the screen already shows this frame, or the overlay is hidden.</summary>
    None = 0,

    /// <summary>Draw the constants and present.</summary>
    Draw = 1,

    /// <summary>Show the overlay window (it was hidden), then draw and present.</summary>
    ShowAndDraw = 2,

    /// <summary>Draw the transparent frame (the constants have Visibility 0) before the window is hidden.</summary>
    Clear = 3,
}

/// <summary>One screen's surface as the render thread describes it (<c>RlSurface</c> in rimlight_core.h).</summary>
internal struct SurfaceInfo
{
    public int WidthPx;          // the drawable's size in pixels, render scale included
    public int HeightPx;
    public float PixelsPerPoint; // the screen's backing scale factor
    public int Paused;           // 1 while a full-screen window covers this screen (doc 04 §4's per-monitor pause)
    public float NotchLeftPt;    // the camera notch in points from the overlay's top-left: NotchLeftPt to NotchRightPt
    public float NotchRightPt;   // across, NotchHeightPt down from the top; all 0 without one (OverlayController.swift)
    public float NotchHeightPt;
    public int Pad;
}

/// <summary>What one frame needs from the render thread (<c>RlFrameInfo</c> in rimlight_core.h).</summary>
internal struct FrameInfo
{
    public int GradientVersion; // changes whenever the 64 gradient texels change: upload them again
    public float RenderScale;   // 1, or 0.5 with "Reduce on battery" (M5): the drawables' size relative to the screen
    public int Listening;       // 1 when the glow wants audio (the capture runs only then, M2)
    public int Demo;            // 1 while demo mode is on
}

/// <summary>How the render thread waits after a frame (<c>RlPace</c> in rimlight_core.h).</summary>
internal struct PaceInfo
{
    public int Pace;              // 0 full rate (display link), 1 slow (a timer at IntervalSeconds), 2 none until an input
    public int Pad;
    public double IntervalSeconds;
    public double FullRateHz;     // the display link's preferred rate at the full pace
}

/// <summary>
/// The Mac overlay's loop, minus the drawing: what Windows' OverlayHost decides each frame, here in C# so it is the same
/// code (<see cref="FramePacer"/>, <see cref="GlowConstants"/>, <see cref="OverlayRules"/>) and testable off the Mac.
/// The Swift render thread asks it for a frame, a plan per screen, reports what it presented, and waits as it says.
/// </summary>
/// <remarks>
/// Threads: <see cref="ApplySettings"/>, <see cref="SetPaused"/>, <see cref="SetDemo"/> and <see cref="Settings"/> from
/// any thread (published by reference, doc 02). Everything else on the render thread only, allocation-free per frame.
/// </remarks>
internal sealed class MacOverlayLoop
{
    public const int MaxSurfaces = 32;
    private const long StalledMs = 1000; // a surface that has presented nothing this long is stalled (display asleep)

    private readonly MacGlowSource source;
    private readonly FramePacer pacer = new();
    private readonly float[] gradient = GC.AllocateArray<float>(256, pinned: true);
    private readonly Surface[] surfaces = new Surface[MaxSurfaces];
    private int surfaceCount;

    private Settings published;     // the newest settings, from any thread
    private int pausedWanted;       // SetPaused, from any thread
    private Settings applied;       // the render thread's
    private bool paused;
    private bool appliedDemo;
    private bool appliedAudioWanted;
    private double refreshHz = FramePacer.DefaultRefreshHz;
    private bool onBattery;
    private bool lowPower;
    private bool configured;

    private OverlayFrame frame;
    private float frameDt;
    private Rgb meanColor;
    private int gradientVersion;
    private bool anyVisible, allPresented, stalled, transitioning;

    public MacOverlayLoop(Settings settings)
    {
        published = settings;
        applied = settings;
        source = new MacGlowSource(settings);
    }

    /// <summary>Where settings.json lives; the loop's settings are saved there.</summary>
    public string SettingsDirectory { get; init; } = "";

    /// <summary>The newest settings. Any thread.</summary>
    public Settings Settings => Volatile.Read(ref published);

    /// <summary>Publishes new settings to the render thread. Any thread.</summary>
    public void ApplySettings(Settings settings) => Volatile.Write(ref published, settings);

    /// <summary>Pauses the glow everywhere (the menu bar's toggle aside: sleep, lock, display off; M5). Any thread.</summary>
    public void SetPaused(bool value) => Volatile.Write(ref pausedWanted, value ? 1 : 0);

    /// <summary>Demo mode (doc 04 §5). Any thread.</summary>
    public void SetDemo(bool on) => source.SetDemo(on);

    /// <summary>Whether demo mode is on. Any thread.</summary>
    public bool Demo => source.DemoWanted;

    /// <summary>The frame source, for Settings' meter and preview (their any-thread members only).</summary>
    public MacGlowSource Source => source;

    /// <summary>The 64 RGBA texels of the gradient: a pinned buffer whose address never changes.</summary>
    public unsafe float* Gradient => (float*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(gradient));

    public int SurfaceCount => surfaceCount;

    /// <summary>
    /// Replaces the screens' surfaces (after a display or settings change). The Mac app makes new windows each time,
    /// so every surface starts hidden with nothing drawn: its first visible frame shows its window.
    /// </summary>
    /// <remarks>
    /// To render at half scale ("Reduce on battery", <see cref="FrameInfo.RenderScale"/>) the render thread halves the
    /// drawable and passes the halved size and pixels per point here, so the constants need no render scale of their
    /// own.
    /// </remarks>
    public void SetSurfaces(ReadOnlySpan<SurfaceInfo> infos, double nowSeconds)
    {
        int count = Math.Min(infos.Length, MaxSurfaces);
        for (int i = 0; i < count; i++)
            surfaces[i] = new Surface { Info = infos[i], Fade = infos[i].Paused != 0 ? 0 : 1 };
        for (int i = count; i < surfaceCount; i++) surfaces[i] = default;
        surfaceCount = count;
        pacer.NoteInputChanged(nowSeconds);
    }

    /// <summary>
    /// Pauses or resumes the glow on one screen (a full-screen window covers it, doc 04 §4): it fades out or in over
    /// 300 ms, keeping its window.
    /// </summary>
    public void SetSurfacePaused(int index, bool paused, double nowSeconds)
    {
        if ((uint)index >= (uint)surfaceCount) return;
        surfaces[index].Info.Paused = paused ? 1 : 0;
        pacer.NoteInputChanged(nowSeconds);
    }

    /// <summary>
    /// The main screen's refresh rate (0 when unknown), whether the Mac runs on battery, and whether Low Power Mode is
    /// on. On battery the OnBattery setting applies (Normal, Reduce, Pause); Low Power Mode always reduces (30 fps at
    /// half scale, 10-MACOS §5) and never pauses.
    /// </summary>
    public void SetDisplay(double refreshHz, bool onBattery, bool lowPower = false)
    {
        this.refreshHz = refreshHz;
        this.onBattery = onBattery;
        this.lowPower = lowPower;
        configured = false;
    }

    /// <summary>Takes the inputs published since the last call. True when one changed (a frame may be due early).</summary>
    public bool ApplyInputs(double nowSeconds)
    {
        bool changed = false;
        Settings latest = Volatile.Read(ref published);
        if (!ReferenceEquals(latest, applied))
        {
            applied = latest;
            configured = false;
            changed = true;
        }
        // Demo mode is the frame source's own switch; it is an input here so that turning it on wakes a stopped loop.
        bool demoNow = source.DemoWanted;
        if (demoNow != appliedDemo)
        {
            appliedDemo = demoNow;
            changed = true;
        }
        // So is Settings wanting the audio: the meter must wake a stopped loop, as Windows asks for a frame after
        // SetAudioWanted.
        bool wantedNow = source.AudioWanted;
        if (wantedNow != appliedAudioWanted)
        {
            appliedAudioWanted = wantedNow;
            changed = true;
        }
        bool pause = Volatile.Read(ref pausedWanted) != 0;
        if (pause != paused)
        {
            paused = pause;
            changed = true;
        }
        if (!configured)
        {
            pacer.Configure(applied.FpsCap, refreshHz, Reduced);
            configured = true;
            changed = true;
        }
        if (changed) pacer.NoteInputChanged(nowSeconds);
        return changed;
    }

    /// <summary>Whether an input arriving <paramref name="elapsedSeconds"/> after the last frame waits for the next one.</summary>
    public bool InputWaits(int pace, double elapsedSeconds) => pacer.InputWaits((Pace)pace, elapsedSeconds);

    /// <summary>The time step for a frame <paramref name="elapsedSeconds"/> after the last (FramePacer.FrameDt).</summary>
    public float FrameDt(double elapsedSeconds, bool frameDue) => pacer.FrameDt(elapsedSeconds, frameDue);

    /// <summary>Starts a frame: runs the frame source and advances the per-screen pause fades.</summary>
    public FrameInfo BeginFrame(float dt, ReadOnlySpan<float> samples, int sampleRate, int captureGeneration = 0)
    {
        frameDt = dt;
        bool everywhere = PausedEverywhere();
        frame = source.NextFrame(dt, applied, everywhere, gradient, samples, sampleRate, captureGeneration);
        if (frame.GradientChanged)
        {
            meanColor = GlowConstants.MeanOf(gradient);
            gradientVersion++;
        }
        transitioning = false;
        for (int i = 0; i < surfaceCount; i++)
            transitioning |= OverlayRules.AdvanceFade(ref surfaces[i].Fade, surfaces[i].Info.Paused != 0, dt);
        anyVisible = false;
        allPresented = true;
        stalled = false;
        return new FrameInfo
        {
            GradientVersion = gradientVersion,
            RenderScale = Reduced ? 0.5f : 1f,
            Listening = source.Listening ? 1 : 0,
            Demo = source.DemoWanted ? 1 : 0,
        };
    }

    /// <summary>What to do with screen <paramref name="index"/> this frame, and the constants to draw it with.</summary>
    public SurfaceAction PlanSurface(int index, out GlowConstants constants)
    {
        constants = default;
        if ((uint)index >= (uint)surfaceCount) return SurfaceAction.None;
        ref Surface s = ref surfaces[index];
        LightState state = frame.State;
        float visibility = float.IsFinite(state.Visibility) ? Math.Clamp(state.Visibility, 0, 1) : 0;
        float shown = visibility * s.Fade;

        if (shown < GlowConstants.MinVisibility)
        {
            if (!s.Visible) return SurfaceAction.None;
            // Hidden only once a transparent frame is on screen, so showing it again never flashes an old frame.
            constants = Constants(state, s, 0);
            s.Planned = constants;
            s.PlannedClear = true;
            return SurfaceAction.Clear;
        }

        anyVisible = true;
        bool show = !s.Visible;
        s.Visible = true;
        constants = Constants(state, s, shown);
        s.Planned = constants;
        s.PlannedClear = false;
        // Static: nothing new to present (doc 02).
        if (!show && s.HasLast && s.LastGradient == gradientVersion && s.Last.LooksLike(constants)) return SurfaceAction.None;
        return show ? SurfaceAction.ShowAndDraw : SurfaceAction.Draw;
    }

    /// <summary>
    /// Reports whether the planned frame reached the screen. True when the window should be hidden now (its
    /// transparent frame is on screen, or the screen is asleep).
    /// </summary>
    public bool SurfaceDone(int index, bool presented, long nowMs)
    {
        if ((uint)index >= (uint)surfaceCount) return false;
        ref Surface s = ref surfaces[index];
        if (presented)
        {
            s.Last = s.Planned;
            s.HasLast = true;
            s.LastGradient = gradientVersion;
            s.SkippingSinceMs = null;
            if (!s.PlannedClear) return false;
            s.Visible = false;
            return true;
        }
        s.SkippingSinceMs ??= nowMs;
        if (nowMs - s.SkippingSinceMs.Value >= StalledMs)
        {
            if (s.PlannedClear)
            {
                // A sleeping screen is hidden without its transparent frame, so the loop can stop.
                s.Visible = false;
                s.HasLast = false;
                s.SkippingSinceMs = nowMs;
                return true;
            }
            stalled = true;
        }
        else
        {
            allPresented = false;
        }
        return false;
    }

    /// <summary>Ends the frame: how to wait for the next.</summary>
    public PaceInfo EndFrame(double nowSeconds)
    {
        var report = new FrameReport(frame.Motion, frame.State, frameDt, frame.GradientChanged, anyVisible,
            transitioning, allPresented, stalled);
        Pace pace = pacer.Next(nowSeconds, report);
        return new PaceInfo
        {
            Pace = (int)pace,
            IntervalSeconds = pace == Pace.Block ? 0 : pacer.IntervalFor(pace),
            FullRateHz = 1 / pacer.FullInterval,
        };
    }

    private bool Reduced => OverlayRules.Reduced(applied, onBattery) || lowPower;

    private bool PausedEverywhere()
    {
        if (paused || (onBattery && applied.OnBattery == BatteryBehavior.Pause)) return true;
        if (surfaceCount == 0) return true;
        for (int i = 0; i < surfaceCount; i++)
            if (surfaces[i].Info.Paused == 0 || surfaces[i].Fade > 0) return false;
        return true;
    }

    /// <summary>
    /// The constants for <paramref name="state"/> on one surface, as the loop draws it: the settings' corner radius and
    /// edge style, and the surface's notch converted from points to its pixels, so the light goes round it.
    /// <c>rl_glow_constants</c> calls this too, so the golden table's constants come the app's way. Allocation-free.
    /// </summary>
    /// <remarks>
    /// The surface's size and pixels per point already include any render scale (<see cref="SetSurfaces"/>), so the
    /// notch lands in the drawable's own pixels at half scale too. <see cref="GlowConstants.Create"/> drops a notch
    /// that is empty, off the surface or not finite.
    /// </remarks>
    public static GlowConstants ConstantsFor(in LightState state, in SurfaceInfo info, float cornerRadiusDip,
        EdgeStyle style, Rgb meanColor)
    {
        float scale = info.PixelsPerPoint;
        var notch = new NotchPx(info.NotchLeftPt * scale, info.NotchRightPt * scale, info.NotchHeightPt * scale);
        return GlowConstants.Create(state, info.WidthPx, info.HeightPx, scale, 1, cornerRadiusDip, meanColor, style,
            notch);
    }

    private GlowConstants Constants(in LightState state, in Surface s, float visibility)
    {
        GlowConstants c = ConstantsFor(state, s.Info, applied.CornerRadiusDip, applied.EdgeStyle, meanColor);
        c.Visibility = visibility;
        return c;
    }

    private struct Surface
    {
        public SurfaceInfo Info;
        public float Fade;
        public bool Visible;
        public bool HasLast;
        public GlowConstants Last;
        public int LastGradient;
        public GlowConstants Planned;
        public bool PlannedClear;
        public long? SkippingSinceMs;
    }
}
