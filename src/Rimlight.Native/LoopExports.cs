using System.Runtime.InteropServices;
using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native;

// The overlay loop's exports (rimlight_core.h, "Overlay loop"). A loop is a GCHandle to a MacOverlayLoop. The per-frame
// calls (begin, plan, done, end and the pacing helpers) allocate nothing and look the handle up without allocating.
internal static unsafe partial class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "rl_loop_create")]
    public static nint LoopCreate(byte* settingsDirectory, int demo)
    {
        try
        {
            string? dir = Utf8Text.Read(settingsDirectory);
            if (string.IsNullOrWhiteSpace(dir)) return 0;
            var loop = new MacOverlayLoop(CoreFactory.CreateSettingsStore(dir).Load()) { SettingsDirectory = dir };
            loop.SetDemo(demo != 0);
            return GCHandle.ToIntPtr(GCHandle.Alloc(loop));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_destroy")]
    public static void LoopDestroy(nint loop)
    {
        try
        {
            if (loop != 0) GCHandle.FromIntPtr(loop).Free();
        }
        catch (Exception)
        {
            // An invalid handle: nothing to free.
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_settings")]
    public static int LoopSettings(nint loop, byte* buffer, int capacity)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            return Utf8Text.Write(SettingsTransport.Serialize(l.Settings), buffer, capacity);
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_apply_settings")]
    public static int LoopApplySettings(nint loop, byte* json, int save)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            string? text = Utf8Text.Read(json);
            if (text is null) return NativeStatus.InvalidArgument;
            Settings? settings = SettingsTransport.Deserialize(text);
            if (settings is null) return NativeStatus.BadJson;
            if (save != 0)
            {
                // The store validates on save; the loop then gets exactly what the file holds.
                ISettingsStore store = CoreFactory.CreateSettingsStore(l.SettingsDirectory);
                try
                {
                    store.Save(settings);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    l.ApplySettings(settings);
                    return NativeStatus.IoError;
                }
                settings = store.Load();
            }
            l.ApplySettings(settings);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_set_demo")]
    public static int LoopSetDemo(nint loop, int on)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            l.SetDemo(on != 0);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_demo")]
    public static int LoopDemo(nint loop)
    {
        try
        {
            return Loop(loop) is { } l ? (l.Demo ? 1 : 0) : NativeStatus.InvalidArgument;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_set_paused")]
    public static int LoopSetPaused(nint loop, int paused)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            l.SetPaused(paused != 0);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_set_surfaces")]
    public static int LoopSetSurfaces(nint loop, SurfaceInfo* surfaces, int count, double nowSeconds)
    {
        try
        {
            if (Loop(loop) is not { } l || count < 0 || (surfaces == null && count > 0)) return NativeStatus.InvalidArgument;
            l.SetSurfaces(new ReadOnlySpan<SurfaceInfo>(surfaces, count), nowSeconds);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_set_surface_paused")]
    public static int LoopSetSurfacePaused(nint loop, int index, int paused, double nowSeconds)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            l.SetSurfacePaused(index, paused != 0, nowSeconds);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_set_display")]
    public static int LoopSetDisplay(nint loop, double refreshHz, int onBattery, int lowPower)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            l.SetDisplay(refreshHz, onBattery != 0, lowPower != 0);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_apply_inputs")]
    public static int LoopApplyInputs(nint loop, double nowSeconds)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            return l.ApplyInputs(nowSeconds) ? 1 : 0;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_input_waits")]
    public static int LoopInputWaits(nint loop, int pace, double elapsedSeconds)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            return l.InputWaits(pace, elapsedSeconds) ? 1 : 0;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_frame_dt")]
    public static float LoopFrameDt(nint loop, double elapsedSeconds, int frameDue)
    {
        try
        {
            return Loop(loop) is { } l ? l.FrameDt(elapsedSeconds, frameDue != 0) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_begin_frame")]
    public static int LoopBeginFrame(nint loop, float dt, float* samples, int count, int sampleRate,
        int captureGeneration, FrameInfo* info)
    {
        try
        {
            if (Loop(loop) is not { } l || info == null || count < 0 || (samples == null && count > 0))
                return NativeStatus.InvalidArgument;
            *info = l.BeginFrame(dt, new ReadOnlySpan<float>(samples, count), sampleRate, captureGeneration);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_gradient")]
    public static float* LoopGradient(nint loop)
    {
        try
        {
            return Loop(loop) is { } l ? l.Gradient : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_plan_surface")]
    public static int LoopPlanSurface(nint loop, int index, GlowConstants* constants)
    {
        try
        {
            if (Loop(loop) is not { } l || constants == null) return NativeStatus.InvalidArgument;
            SurfaceAction action = l.PlanSurface(index, out GlowConstants c);
            *constants = c;
            return (int)action;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_surface_done")]
    public static int LoopSurfaceDone(nint loop, int index, int presented, long nowMs)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            return l.SurfaceDone(index, presented != 0, nowMs) ? 1 : 0;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_end_frame")]
    public static int LoopEndFrame(nint loop, double nowSeconds, PaceInfo* pace)
    {
        try
        {
            if (Loop(loop) is not { } l || pace == null) return NativeStatus.InvalidArgument;
            *pace = l.EndFrame(nowSeconds);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    // The shared constants math alone, for the glow parity tests (10-MACOS §1.4): GlowConstants.Create for a light
    // state on a surface, as the loop computes it.
    [UnmanagedCallersOnly(EntryPoint = "rl_glow_constants")]
    public static int GlowConstantsFor(LightState* state, int widthPx, int heightPx, float pixelsPerPoint,
        float renderScale, float cornerRadiusDip, Rgb* meanColor, GlowConstants* constants)
    {
        try
        {
            if (state == null || meanColor == null || constants == null) return NativeStatus.InvalidArgument;
            *constants = GlowConstants.Create(*state, widthPx, heightPx, pixelsPerPoint, renderScale, cornerRadiusDip,
                *meanColor);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    private static MacOverlayLoop? Loop(nint handle) =>
        handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as MacOverlayLoop;
}
