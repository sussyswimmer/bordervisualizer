using System.Text;
using Rimlight.Core;
using Rimlight.Native.Overlay;

namespace Rimlight.Native.Tests;

// Album colours and the tooltip through the C ABI.
public sealed unsafe class MediaExportsTests
{
    [Fact]
    public void ACoverBecomesTheAlbumPalette()
    {
        var loop = new MacOverlayLoop(new Settings());
        nint handle = System.Runtime.InteropServices.GCHandle.ToIntPtr(System.Runtime.InteropServices.GCHandle.Alloc(loop));
        try
        {
            delegate* unmanaged<nint, byte*, int, int, byte*, int> art = &Exports.AlbumArt;
            delegate* unmanaged<nint, Rgb*, Rgb*, int> palette = &Exports.AlbumPalette;
            delegate* unmanaged<nint, int> clear = &Exports.AlbumClear;

            // 64 x 64: 70 % blue, 30 % orange (doc 05 §4), BGRA.
            byte[] pixels = new byte[64 * 64 * 4];
            for (int i = 0; i < 64 * 64; i++)
            {
                bool blue = i % 64 < 45;
                pixels[i * 4] = (byte)(blue ? 230 : 20);
                pixels[i * 4 + 1] = (byte)(blue ? 80 : 140);
                pixels[i * 4 + 2] = (byte)(blue ? 30 : 250);
                pixels[i * 4 + 3] = 255;
            }
            byte[] id = Encoding.UTF8.GetBytes("Music|Artist|Title\0");
            Rgb primary, secondary;
            fixed (byte* p = pixels)
            fixed (byte* t = id)
            {
                Assert.Equal(0, palette(handle, &primary, &secondary));
                Assert.Equal(1, art(handle, p, 64, 64, t));
            }
            Assert.Equal(1, palette(handle, &primary, &secondary));
            Assert.True(primary.B > primary.R, $"primary {primary} should be the blue");
            Assert.NotNull(loop.Source.PublishedAlbum);
            Assert.Equal(NativeStatus.Ok, clear(handle));
            Assert.Null(loop.Source.PublishedAlbum);
        }
        finally
        {
            System.Runtime.InteropServices.GCHandle.FromIntPtr(handle).Free();
        }
    }

    [Fact]
    public void AFlatGreyCoverGlowsGreyAndOnlyATransparentOneIsNoArt()
    {
        // Maxwell's feedback: a flat grey or black picture was "no art", so an all-black cover showed the manual
        // colours. It is art now and glows grey; only a picture with no opaque pixel is no art.
        var loop = new MacOverlayLoop(new Settings());
        nint handle = System.Runtime.InteropServices.GCHandle.ToIntPtr(System.Runtime.InteropServices.GCHandle.Alloc(loop));
        try
        {
            delegate* unmanaged<nint, byte*, int, int, byte*, int> art = &Exports.AlbumArt;
            delegate* unmanaged<nint, Rgb*, Rgb*, int> palette = &Exports.AlbumPalette;
            byte[] transparent = new byte[32 * 32 * 4];
            fixed (byte* p = transparent) Assert.Equal(0, art(handle, p, 32, 32, null));
            Assert.Null(loop.Source.PublishedAlbum);

            byte[] grey = new byte[32 * 32 * 4];
            for (int i = 0; i < grey.Length; i++) grey[i] = (byte)(i % 4 == 3 ? 255 : 128);
            fixed (byte* p = grey) Assert.Equal(1, art(handle, p, 32, 32, null));
            Rgb primary, secondary;
            Assert.Equal(1, palette(handle, &primary, &secondary));
            foreach (Rgb color in new[] { primary, secondary })
                Assert.True(MathF.Abs(color.R - color.G) < 1e-4f && MathF.Abs(color.G - color.B) < 1e-4f, $"{color} is not grey");
        }
        finally
        {
            System.Runtime.InteropServices.GCHandle.FromIntPtr(handle).Free();
        }
    }

    [Fact]
    public void TheTooltipIsTheWindowsOne()
    {
        delegate* unmanaged<byte*, byte*, byte*, int, byte*, int, int> tooltip = &Exports.ToolTip;
        byte[] title = Encoding.UTF8.GetBytes("Song\0"), artist = Encoding.UTF8.GetBytes("Band\0");
        byte[] buffer = new byte[512];
        fixed (byte* t = title)
        fixed (byte* a = artist)
        fixed (byte* b = buffer)
        {
            int n = tooltip(t, a, null, 1, b, buffer.Length);
            Assert.Equal(AppInfo.Name + " — Song · Band", Encoding.UTF8.GetString(buffer, 0, n));
            n = tooltip(null, null, null, 0, b, buffer.Length);
            Assert.Equal(AppInfo.Name + " — Waiting for music", Encoding.UTF8.GetString(buffer, 0, n));
        }
    }
}
