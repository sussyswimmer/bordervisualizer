using System.Runtime.InteropServices;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native;

/// <summary>The analyzer's latest features for Settings' meter (<c>RlAudioReading</c> in rimlight_core.h).</summary>
internal struct AudioReadingInfo
{
    public float Level;
    public float Bass;
    public float Beat;
    public int Silent;
    public int Sequence; // the same twice: no new frame came
}

// The Settings window's exports (rimlight_core.h, "Settings window"): presets, the meter and the live preview. Main
// thread, except where the header says any thread.
internal static unsafe partial class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "rl_presets_count")]
    public static int PresetsCount()
    {
        try
        {
            return Presets.All.Count;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preset_name")]
    public static int PresetName(int index, byte* buffer, int capacity)
    {
        try
        {
            if ((uint)index >= (uint)Presets.All.Count) return NativeStatus.InvalidArgument;
            return Utf8Text.Write(Presets.All[index].Name, buffer, capacity);
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preset_apply")]
    public static int PresetApply(int index, byte* settingsJson, byte* buffer, int capacity)
    {
        try
        {
            if ((uint)index >= (uint)Presets.All.Count) return NativeStatus.InvalidArgument;
            string? text = Utf8Text.Read(settingsJson);
            if (text is null) return NativeStatus.InvalidArgument;
            Settings? settings = SettingsTransport.Deserialize(text);
            if (settings is null) return NativeStatus.BadJson;
            return Utf8Text.Write(SettingsTransport.Serialize(Presets.All[index].Apply(settings)), buffer, capacity);
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_set_audio_wanted")]
    public static int LoopSetAudioWanted(nint loop, int wanted)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            l.Source.SetAudioWanted(wanted != 0);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_loop_audio")]
    public static int LoopAudio(nint loop, AudioReadingInfo* reading)
    {
        try
        {
            if (Loop(loop) is not { } l || reading == null) return NativeStatus.InvalidArgument;
            AudioReading r = l.Source.LatestAudio;
            *reading = new AudioReadingInfo
            {
                Level = r.Features.Level,
                Bass = r.Features.Bass,
                Beat = r.Features.Beat,
                Silent = r.Features.IsSilent ? 1 : 0,
                Sequence = r.Sequence,
            };
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preview_create")]
    public static nint PreviewCreate(nint loop)
    {
        try
        {
            return Loop(loop) is { } l ? GCHandle.ToIntPtr(GCHandle.Alloc(new MacPreview(l))) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preview_destroy")]
    public static void PreviewDestroy(nint preview)
    {
        try
        {
            if (preview != 0) GCHandle.FromIntPtr(preview).Free();
        }
        catch (Exception)
        {
            // An invalid handle: nothing to free.
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preview_frame")]
    public static int PreviewFrame(nint preview, double nowSeconds, int widthPx, int heightPx, GlowConstants* constants)
    {
        try
        {
            if (Preview(preview) is not { } p || constants == null || widthPx < 2 || heightPx < 2)
                return NativeStatus.InvalidArgument;
            *constants = p.Frame(nowSeconds, widthPx, heightPx, out bool gradientChanged);
            return gradientChanged ? 1 : 0;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preview_restart")]
    public static int PreviewRestart(nint preview)
    {
        try
        {
            if (Preview(preview) is not { } p) return NativeStatus.InvalidArgument;
            p.Restart();
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_preview_gradient")]
    public static float* PreviewGradient(nint preview)
    {
        try
        {
            return Preview(preview) is { } p ? p.Gradient : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MacPreview? Preview(nint handle) =>
        handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as MacPreview;
}
