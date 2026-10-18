using Rimlight.App.Overlay;
using Rimlight.App.Visualizer;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Platform.Tests;

// MusicGlowSource (K2–K4, K7, K8) end to end: the app's real frame source with the real analyzer, light engine and
// blender from CoreFactory, a synthetic track in the loopback ring and album palettes from the media stand-in.
public sealed class MusicGlowSourceTests(ITestOutputHelper output)
{
    private const float Dt = 1 / 60f;
    private static readonly Palette Album = new(new Rgb(0.9f, 0.1f, 0.05f), new Rgb(0.05f, 0.2f, 0.9f), "app|artist|title");
    private static readonly Palette NextAlbum = new(new Rgb(0.1f, 0.8f, 0.2f), new Rgb(0.8f, 0.7f, 0.1f), "app|artist|next");

    public static TheoryData<string> Modes => new()
    {
        "music", "silence", "no packets", "Idle Glow", "Off", "glow off", "paused", "Hide, hidden", "Settings audio, Off",
        "visualizer", "visualizer, Off", "visualizer, paused", "visualizer, recording", "demo", "demo, glow off and Off",
        "demo and visualizer", "album crossfades", "on battery, 144 Hz",
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void AFrameAllocatesNothingInAnyMode(string mode)
    {
        // CLAUDE.md rule 4 and doc 02: nothing on the overlay thread allocates per frame. Settings snapshots are made
        // before the run, as the UI thread makes them; a snapshot that changes no Sensitivity or color costs nothing.
        Settings settings = mode switch
        {
            "Idle Glow" => new Settings { Animation = AnimationMode.IdleGlow },
            "Off" or "Settings audio, Off" or "visualizer, Off" => new Settings { Animation = AnimationMode.Off },
            "glow off" => new Settings { Enabled = false },
            "Hide, hidden" => new Settings { WhenSilent = SilentBehavior.Hide },
            "demo, glow off and Off" => new Settings { Enabled = false, Animation = AnimationMode.Off },
            _ => new Settings(),
        };
        bool paused = mode.Contains("paused", StringComparison.Ordinal);
        float dt = mode.Contains("144", StringComparison.Ordinal) ? 1 / 144f : Dt;
        using var rig = new GlowRig(settings);
        rig.Program = mode switch
        {
            "silence" => AudioProgram.Silence,
            "no packets" or "Hide, hidden" => AudioProgram.NoPackets,
            _ => AudioProgram.Music,
        };
        AnalysisRecording? recording = null;
        if (mode.Contains("visualizer", StringComparison.Ordinal))
        {
            var feed = new VisualizerFeed();
            // "Record 2 min": the whole run is 75 s, so every frame, warm-up included, appends to the recording.
            if (mode.Contains("recording", StringComparison.Ordinal)) feed.StartRecording(recording = new AnalysisRecording());
            rig.Source.SetVisualizer(feed);
            rig.Source.SetAudioWanted(true);
        }
        if (mode.Contains("Settings audio", StringComparison.Ordinal)) rig.Source.SetAudioWanted(true);
        if (mode.Contains("demo", StringComparison.Ordinal)) rig.Source.SetDemo(true);
        bool crossfades = mode == "album crossfades";
        Settings[] snapshots = [settings, settings with { Brightness = 0.6f }, settings with { Glow = 0.7f, FpsCap = 30 }];

        double now = 0;
        int frame = 0, snapshot = 0;
        long frameBytes = 0, editBytes = 0, edits = 0;
        void Run(int frames, int editEvery)
        {
            for (int i = 0; i < frames; i++, frame++)
            {
                now += dt;
                if (crossfades && frame % 30 == 0) rig.PublishAlbum(frame % 60 == 0 ? Album : NextAlbum);
                bool edit = i > 0 && i % editEvery == 0;
                if (edit) snapshot = (snapshot + 1) % snapshots.Length;
                rig.Frame(now, dt, snapshots[snapshot], paused);
                // The frame only: not the capture thread's or the media reader's work.
                if (!edit) frameBytes += rig.LastFrameBytes;
                else
                {
                    edits++;
                    editBytes = Math.Max(editBytes, rig.LastFrameBytes);
                }
            }
        }

        // Warm-up: the capture opens, the analyzer settles, and the JIT has compiled the edit paths as well as the frame's
        // (a method's first calls run unoptimized code, and its tier-up can allocate on the calling thread).
        Run(1500, editEvery: 5);
        frameBytes = editBytes = edits = 0;
        Run(3000, editEvery: 97);
        output.WriteLine($"{mode}: {frameBytes} B over 3000 frames; at most {editBytes} B on the {edits} frames with a new snapshot");
        Assert.Equal(0, frameBytes);
        if (recording is not null) Assert.Equal((RecordingEnd.None, 4500), (recording.End, recording.Count));
        // A new snapshot that changes neither Sensitivity nor a color costs nothing either, except in demo mode with the
        // glow off or Off: the demo's engine settings (the user's, shown on and in music sync) are made once per snapshot,
        // at most a small record (the ceiling is what counts; no allocation at all would be fine too).
        if (mode == "demo, glow off and Off") Assert.InRange(editBytes, 0, 256);
        else Assert.Equal(0, editBytes);
    }

    [Fact]
    public void ASettingsEditAllocatesOnlyWhenItChangesSensitivityOrAColor()
    {
        // The once-per-edit cost MusicGlowSource.ApplySettings documents (C12 measured 120 B per Sensitivity edit): at most
        // a small record or two. Only the ceiling is checked: an edit that allocated nothing would be fine too.
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        double now = 0;
        for (int i = 0; i < 1500; i++) rig.Frame(now += Dt, Dt, settings, false);

        long Edit(Settings next)
        {
            rig.Frame(now += Dt, Dt, next, false);
            return rig.LastFrameBytes;
        }

        Assert.Equal(0, Edit(settings with { Brightness = 0.5f }));
        Assert.Equal(0, Edit(settings with { Brightness = 0.5f, WhenSilent = SilentBehavior.Hide }));
        long sensitivity = Edit(settings with { Sensitivity = 1.5f });
        long color = Edit(settings with { Sensitivity = 1.5f, PrimaryHex = "#FF0000" });
        output.WriteLine($"Sensitivity edit: {sensitivity} B, color edit: {color} B");
        Assert.InRange(sensitivity, 0, 256);
        Assert.InRange(color, 0, 256);
    }

    [Fact]
    public void TheCaptureIsOpenOnlyWhileSomethingListens()
    {
        var music = new Settings();
        using var rig = new GlowRig(music, openDelaySeconds: 0);
        double now = 0;
        bool Open(Settings settings, bool paused = false)
        {
            for (int i = 0; i < 3; i++) rig.Frame(now += Dt, Dt, settings, paused);
            return rig.Capture.IsActive && rig.CaptureOpen;
        }

        Assert.True(Open(music));
        Assert.False(Open(music with { Animation = AnimationMode.IdleGlow }));
        Assert.False(Open(music with { Animation = AnimationMode.Off }));
        Assert.False(Open(music with { Enabled = false }));
        Assert.False(Open(music, paused: true));
        Assert.True(Open(music with { WhenSilent = SilentBehavior.Hide }));

        rig.Source.SetAudioWanted(true); // Settings' level meter or preview
        Assert.True(Open(music with { Animation = AnimationMode.Off }));
        Assert.True(Open(music, paused: true));
        rig.Source.SetAudioWanted(false);
        rig.Source.SetDemo(true); // the demo plays its own beat
        Assert.False(Open(music));
        rig.Source.SetDemo(false);
        Assert.True(Open(music));
        Assert.Equal(3, rig.Opens);
    }

    [Fact]
    public void AlbumColorsCrossfadeOver800MsAndManualEditsOver200Ms()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        double now = 0;
        for (int i = 0; i < 120; i++) rig.Frame(now += Dt, Dt, settings, false);
        float[] manual = (float[])rig.Gradient.Clone();

        // A new album palette: Full motion and a refill every frame for 800 ms, then the album colors exactly.
        rig.PublishAlbum(Album);
        int fadeFrames = FramesUntilSettled(rig, ref now, settings);
        Assert.InRange(fadeFrames, 47, 50);
        AssertGradientShows(rig.Gradient, Album);

        // Override album color: back to the manual colors over 800 ms too.
        Settings overridden = settings with { OverrideAlbumColor = true };
        Assert.InRange(FramesUntilSettled(rig, ref now, overridden), 47, 50);
        Assert.Equal(manual, rig.Gradient);

        // A manual edit while the manual colors show: 200 ms.
        Settings edited = overridden with { PrimaryHex = "#FF2BD6" };
        Assert.InRange(FramesUntilSettled(rig, ref now, edited), 11, 14);

        // Manual color mode ignores the album; with no album palette (no art, no session), Album Art mode shows the
        // manual colors too.
        Settings manualMode = edited with { OverrideAlbumColor = false, ColorMode = ColorMode.Manual };
        Assert.Equal(0, FramesUntilSettled(rig, ref now, manualMode));
        rig.PublishAlbum(null);
        Assert.Equal(0, FramesUntilSettled(rig, ref now, manualMode));
        Assert.Equal(0, FramesUntilSettled(rig, ref now, edited with { OverrideAlbumColor = false }));
    }

    [Fact]
    public void TheDemoShowsItsOwnPalettesAndLeavesTheAlbumColorsWhereTheyWere()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        rig.PublishAlbum(Album);
        double now = 0;
        for (int i = 0; i < 120; i++) rig.Frame(now += Dt, Dt, settings, false);
        AssertGradientShows(rig.Gradient, Album);

        rig.Source.SetDemo(true);
        for (int i = 0; i < 120; i++)
        {
            OverlayFrame frame = rig.Frame(now += Dt, Dt, settings, false);
            Assert.Equal(FrameMotion.Full, frame.Motion);
        }
        AssertGradientShows(rig.Gradient, DemoBeat.Palettes[0]);
        rig.PublishAlbum(NextAlbum); // a track change during the demo applies when it ends
        for (int i = 0; i < 60; i++) rig.Frame(now += Dt, Dt, settings, false);
        AssertGradientShows(rig.Gradient, DemoBeat.Palettes[0]);

        rig.Source.SetDemo(false);
        Assert.InRange(FramesUntilSettled(rig, ref now, settings), 47, 50);
        AssertGradientShows(rig.Gradient, NextAlbum);
    }

    [Fact]
    public void TheDemoStartsOverFromItsFirstPaletteEachTime()
    {
        // Every --demo (and every Demo mode click) starts the track from its first downbeat and the first palette, so a
        // recording always shows the same sequence.
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        double now = 0;
        rig.Source.SetDemo(true);
        for (int i = 0; i < 60 * 8; i++) rig.Frame(now += Dt, Dt, settings, false); // 8 s: the second palette shows
        AssertGradientShows(rig.Gradient, DemoBeat.Palettes[1]);

        rig.Source.SetDemo(false);
        for (int i = 0; i < 60; i++) rig.Frame(now += Dt, Dt, settings, false);
        rig.Source.SetDemo(true);
        for (int i = 0; i < 60; i++) rig.Frame(now += Dt, Dt, settings, false); // past the 800 ms crossfade
        AssertGradientShows(rig.Gradient, DemoBeat.Palettes[0]);
    }

    [Fact]
    public void TheMotionTellsTheOverlayHowToPace()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        double now = 0;
        FrameMotion Run(double seconds, Settings s, bool paused = false)
        {
            FrameMotion motion = FrameMotion.Full;
            for (int i = 0; i < seconds * 60; i++) motion = rig.Frame(now += Dt, Dt, s, paused).Motion;
            return motion;
        }

        Assert.Equal(FrameMotion.Full, Run(3, settings));                         // music
        rig.Program = AudioProgram.NoPackets;
        Assert.Equal(FrameMotion.Full, Run(3.5, settings));                       // silent, still fading to Idle Glow
        Assert.Equal(FrameMotion.Slow, Run(0.5, settings));                       // breathing from 1.75 s after IsSilent
        Settings hide = settings with { WhenSilent = SilentBehavior.Hide };
        Assert.Equal(FrameMotion.Listening, Run(2, hide));                        // hidden, music could bring it back
        Assert.Equal(FrameMotion.Still, Run(1, hide with { Animation = AnimationMode.Off }));
        Assert.Equal(FrameMotion.Slow, Run(1, hide with { Animation = AnimationMode.IdleGlow }));
        Assert.Equal(FrameMotion.Still, Run(1, hide, paused: true));
        rig.Source.SetAudioWanted(true);
        Assert.Equal(FrameMotion.Listening, Run(0.2, hide, paused: true));        // Settings keeps the meter going
        rig.Source.SetVisualizer(new VisualizerFeed());
        Assert.Equal(FrameMotion.Observed, Run(0.2, hide, paused: true));         // the visualizer plots every frame
    }

    // Frames from now until the blender has settled, with a refill on each of them.
    private static int FramesUntilSettled(GlowRig rig, ref double now, Settings settings)
    {
        int frames = 0;
        while (true)
        {
            OverlayFrame frame = rig.Frame(now += Dt, Dt, settings, false);
            if (!frame.GradientChanged) return frames;
            Assert.True(frames < 200, "the crossfade never ends");
            frames++;
        }
    }

    // The gradient's pure texels (away from the two blends) are the palette's colors.
    private static void AssertGradientShows(float[] gradient, Palette palette)
    {
        Assert.Equal(palette.Primary.R, gradient[16 * 4], 4);     // u ≈ 0.26: inside Primary's arc (ratio 0.6)
        Assert.Equal(palette.Primary.G, gradient[16 * 4 + 1], 4);
        Assert.Equal(palette.Secondary.R, gradient[52 * 4], 4);   // u ≈ 0.82: inside Secondary's arc
        Assert.Equal(palette.Secondary.B, gradient[52 * 4 + 2], 4);
    }
}
