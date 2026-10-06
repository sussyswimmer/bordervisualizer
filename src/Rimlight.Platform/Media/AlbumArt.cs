using Rimlight.Core;

namespace Rimlight.Platform.Media;

/// <summary>
/// A track's cover art, decoded small: at most 64 pixels on the long side, aspect ratio kept (doc 05 §1). Enough for
/// palette extraction and for a "Now playing" thumbnail.
/// </summary>
/// <param name="Pixels">
/// Width × Height × 4 bytes of BGRA8 with straight (not premultiplied) alpha, rows tightly packed, top row first
/// (WPF's <c>PixelFormats.Bgra32</c>). Never modified after it is published.
/// </param>
/// <param name="Width">Width in pixels, 1..64.</param>
/// <param name="Height">Height in pixels, 1..64.</param>
/// <param name="TrackId">The track the art was read for (<see cref="NowPlaying.TrackId"/>).</param>
public sealed record AlbumArt(ReadOnlyMemory<byte> Pixels, int Width, int Height, string TrackId)
{
    // The same picture for the same track (record equality compares the buffers by reference only).
    internal static bool SameImage(AlbumArt? a, AlbumArt? b) =>
        ReferenceEquals(a, b)
        || (a is not null && b is not null && a.Width == b.Width && a.Height == b.Height
            && string.Equals(a.TrackId, b.TrackId, StringComparison.Ordinal)
            && a.Pixels.Span.SequenceEqual(b.Pixels.Span));
}
