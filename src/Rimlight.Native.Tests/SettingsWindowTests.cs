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
    public void TheMeterWakesAStoppedLoop()
    {
        var loop = new MacOverlayLoop(new Settings { Animation = AnimationMode.Off });
        loop.SetSurfaces([new SurfaceInfo { WidthPx = 1920, HeightPx = 1080, PixelsPerPoint = 1 }], 0);
        double now = 0;
        PaceInfo pace = default;
        for (int i = 0; i < 120 && pace.Pace != 2; i++, now += 1 / 60.0)
        {
            loop.ApplyInputs(now);
            loop.BeginFrame(1 / 60f, [], 0);
            if (loop.PlanSurface(0, out _) != SurfaceAction.None) loop.SurfaceDone(0, true, 0);
            pace = loop.EndFrame(now);
        }
        Assert.Equal(2, pace.Pace); // Off: nothing to draw, no frames
        loop.Source.SetAudioWanted(true);
        Assert.True(loop.ApplyInputs(now));
        Assert.Equal(1, loop.BeginFrame(1 / 60f, [], 0).Listening);
    }

    [Fact]
    public void TheMeterNeverChangesTheGlow()
    {
        // The same music through two frame sources, one with Settings' meter open: the glow is identical, only the
        // pacing knows the audio is wanted (MusicGlowSource: "a meter in Settings must not change the glow").
        var settings = new Settings { Animation = AnimationMode.IdleGlow };
        var plain = new MacGlowSource(settings);
        var metered = new MacGlowSource(settings);
        metered.SetAudioWanted(true);
        float[] a = new float[256], b = new float[256], samples = new float[800];
        for (int i = 0; i < 600; i++)
        {
            for (int s = 0; s < samples.Length; s++)
                samples[s] = (float)(Math.Exp(-(i * 800 + s) % 24000 / 4000.0) * Math.Sin(s * 0.007));
            var x = plain.NextFrame(1 / 60f, settings, false, a, [], 0, 0);
            var y = metered.NextFrame(1 / 60f, settings, false, b, samples, 48000, 1);
            Assert.True(x.State == y.State, $"frame {i}: {x.State} vs {y.State}");
            Assert.True(a.AsSpan().SequenceEqual(b));
        }
        Assert.True(metered.LatestAudio.Features.Level > 0 || metered.LatestAudio.Features.Bass > 0);
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

    [Fact]
    public void ThePreviewShowsTheEdgeStyleWithoutANotch()
    {
        var loop = new MacOverlayLoop(new Settings { EdgeStyle = EdgeStyle.Line, CoreThicknessDip = 20 });
        var preview = new MacPreview(loop);
        GlowConstants k = preview.Frame(0, 704, 396, out _);
        Assert.Equal(20 * 704 / 1280f, k.LinePx, 3); // the line is the Thickness wide
        Assert.Equal(0, k.NotchHeightPx);
        loop.ApplySettings(loop.Settings with { EdgeStyle = EdgeStyle.Glow });
        Assert.Equal(0, preview.Frame(1 / 30.0, 704, 396, out _).LinePx);
    }
}
