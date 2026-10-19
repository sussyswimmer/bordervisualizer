using System.Runtime.InteropServices;
using Rimlight.App;
using Rimlight.Core;

namespace Rimlight.Native;

// Now playing and album colours (rimlight_core.h, "Now playing"; docs/10-MACOS.md §4). The Swift media service reads
// the track and its cover; Core's extractor turns the cover into the palette, which the frame source picks up by
// reference (doc 02). Extraction runs on the caller's thread (the media queue), never the render or main thread.
internal static unsafe partial class Exports
{
    private static readonly IPaletteExtractor Extractor = CoreFactory.CreatePaletteExtractor();

    [UnmanagedCallersOnly(EntryPoint = "rl_album_art")]
    public static int AlbumArt(nint loop, byte* bgra, int width, int height, byte* trackId)
    {
        try
        {
            if (Loop(loop) is not { } l || bgra == null || width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return NativeStatus.InvalidArgument;
            string id = Utf8Text.Read(trackId) ?? "";
            Palette? palette = Extractor.Extract(new ReadOnlySpan<byte>(bgra, width * height * 4), width, height, id);
            if (palette is null) return 0; // "no art" (doc 05 §1): the caller keeps the previous colours or retries
            l.Source.PublishAlbum(palette);
            return 1;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_album_clear")]
    public static int AlbumClear(nint loop)
    {
        try
        {
            if (Loop(loop) is not { } l) return NativeStatus.InvalidArgument;
            l.Source.PublishAlbum(null);
            return NativeStatus.Ok;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_album_palette")]
    public static int AlbumPalette(nint loop, Rgb* primary, Rgb* secondary)
    {
        try
        {
            if (Loop(loop) is not { } l || primary == null || secondary == null) return NativeStatus.InvalidArgument;
            if (l.Source.PublishedAlbum is not { } palette) return 0;
            *primary = palette.Primary;
            *secondary = palette.Secondary;
            return 1;
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "rl_tooltip")]
    public static int ToolTip(byte* title, byte* artist, byte* sourceApp, int isPlaying, byte* buffer, int capacity)
    {
        try
        {
            string? t = Utf8Text.Read(title), a = Utf8Text.Read(artist), app = Utf8Text.Read(sourceApp);
            NowPlaying? track = t is null && a is null
                ? null
                : new NowPlaying(t, a, app, isPlaying != 0, $"{app}|{a}|{t}");
            return Utf8Text.Write(TrayToolTip.Build(track), buffer, capacity);
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }
}
