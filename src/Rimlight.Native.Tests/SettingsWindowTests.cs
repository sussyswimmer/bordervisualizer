using System.Text;
using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Tests;

// What the Settings window needs from Core: presets, the level meter and the live preview.
public sealed unsafe class SettingsWindowTests
{
    [Fact]
    public void PresetsComeFromCoreAndApplyTheirLook()
    {
        delegate* unmanaged<int> count = &Exports.PresetsCount;
        delegate* unmanaged<int, byte*, int, int> name = &Exports.PresetName;
        delegate* unmanaged<int, byte*, byte*, int, int> apply = &Exports.PresetApply;
        Assert.Equal(Presets.All.Count, count());

        byte[] buffer = new byte[4096];
        fixed (byte* b = buffer)
        {
            int length = name(0, b, buffer.Length);
            Assert.Equal(Presets.All[0].Name, Encoding.UTF8.GetString(buffer, 0, length));

            byte[] json = Encoding.UTF8.GetBytes(SettingsTransport.Serialize(new Settings { Brightness = 0.3f }) + "\0");
            fixed (byte* j = json)
            {
                length = apply(0, j, b, buffer.Length);
                Assert.True(length > 0);
                Settings applied = SettingsTransport.Deserialize(Encoding.UTF8.GetString(buffer, 0, length))!;
                Assert.Equal(Presets.All[0].Apply(new Settings { Brightness = 0.3f }).PrimaryHex, applied.PrimaryHex);
                Assert.True(applied.OverrideAlbumColor);
                Assert.Equal(NativeStatus.InvalidArgument, apply(99, j, b, buffer.Length));
            }
        }
    }

    [Fact]
    public void TheMeterKeepsTheCaptureOpenAndReportsTheLatestFrame()
    {
        // Idle Glow doesn't listen; the meter wants the audio anyway (MusicGlowSource.SetAudioWanted).
        var loop = new MacOverlayLoop(new Settings { Animation = AnimationMode.IdleGlow });
        loop.SetSurfaces([new SurfaceInfo { WidthPx = 1920, HeightPx = 1080, PixelsPerPoint = 1 }], 0);
        loop.ApplyInputs(0);
        Assert.Equal(0, loop.BeginFrame(1 / 60f, [], 0).Listening);
        loop.Source.SetAudioWanted(true);
        Assert.Equal(1, loop.BeginFrame(1 / 60f, [], 0).Listening);
        int first = loop.Source.LatestAudio.Sequence;
        loop.BeginFrame(1 / 60f, [], 0);
        Assert.NotEqual(first, loop.Source.LatestAudio.Sequence);
    }

    [Fact]
    public void ThePreviewDrawsTheGlowAsAScreenOf1280Points()
    {
        var loop = new MacOverlayLoop(new Settings { Enabled = false, CoreThicknessDip = 20 });
        var preview = new MacPreview(loop);
        GlowConstants first = preview.Frame(0, 704, 396, out bool refilled);
        Assert.True(refilled);
        Assert.Equal(704, first.ScreenWidthPx);
        // Thickness 20 points on a 1280-point screen drawn 704 px wide: 11 px.
        Assert.Equal(20 * 704 / 1280f, first.ShoulderPx, 3);
        // Glow off still shows the glow as it looks when on: it fades in.
        GlowConstants later = default;
        for (int i = 1; i <= 30; i++) later = preview.Frame(i / 30.0, 704, 396, out _);
        Assert.True(later.Visibility > 0.9f);
    }
}
