using System.Reflection;
using Rimlight.Core;
using Rimlight.Core.Audio;
using Rimlight.Core.Color;
using Rimlight.Core.Lighting;
using Rimlight.Core.SettingsStorage;
using Xunit;

namespace Rimlight.Tests;

public sealed class CoreFactoryTests
{
    [Fact]
    public void PresetCatalogIsAvailableDuringShellStartup()
    {
        Assert.Equal(5, Presets.All.Count);
    }

    [Fact]
    public void SettingsStoreIsTheJsonFileStore()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rimlight-tests", Guid.NewGuid().ToString("N"));
        try
        {
            ISettingsStore store = CoreFactory.CreateSettingsStore(directory);
            Assert.IsType<JsonSettingsStore>(store);
            Assert.Equal(System.IO.Path.Combine(directory, "settings.json"), store.Path);
            Assert.False(Directory.Exists(directory)); // nothing touched before the first call

            store.Save(new Settings { Glow = 0.7f });
            Assert.True(File.Exists(store.Path));
            Assert.Equal(0.7f, CoreFactory.CreateSettingsStore(directory).Load().Glow);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EveryFactoryMethodReturnsTheRealImplementation()
    {
        // C13: the Step 0 fakes are gone and the factory is the only seam Lane B uses.
        Assert.IsType<AudioAnalyzer>(CoreFactory.CreateAnalyzer());
        Assert.IsType<AudioAnalyzer>(CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = 1.5f }));
        Assert.IsType<PaletteExtractor>(CoreFactory.CreatePaletteExtractor());
        Assert.IsType<PaletteBlender>(CoreFactory.CreatePaletteBlender(Palette.Default));
        Assert.IsType<LightEngine>(CoreFactory.CreateLightEngine());
        Assert.IsType<JsonSettingsStore>(CoreFactory.CreateSettingsStore(System.IO.Path.GetTempPath()));
    }

    [Fact]
    public async Task TwoEnginesAndBlendersRunIndependentlyOnTwoThreads()
    {
        // H-022: the settings preview drives its own engine and blender on the UI thread while the overlay's pair runs
        // on the render thread. Run together, on two threads at once, each pair must give exactly what it gives alone,
        // so nothing mutable is shared between instances.
        const int Frames = 20_000;
        float[][] musicAlone = RunPair(Music, Frames);
        float[][] idleAlone = RunPair(Idle, Frames);

        for (int round = 0; round < 3; round++)
        {
            using var start = new Barrier(2);
            Task<float[][]> music = Task.Factory.StartNew(() => { start.SignalAndWait(); return RunPair(Music, Frames); }, TaskCreationOptions.LongRunning);
            Task<float[][]> idle = Task.Factory.StartNew(() => { start.SignalAndWait(); return RunPair(Idle, Frames); }, TaskCreationOptions.LongRunning);
            float[][][] both = await Task.WhenAll(music, idle).WaitAsync(TimeSpan.FromMinutes(2));
            AssertSameFrames(musicAlone, both[0]);
            AssertSameFrames(idleAlone, both[1]);
        }

        static void AssertSameFrames(float[][] expected, float[][] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.True(expected[i].AsSpan().SequenceEqual(actual[i]), $"Frame {i} differs.");
        }
    }

    // One overlay-like pair: Music Sync, a beat every 0.5 s, a new album palette every 3 s, silence (Hide) in the middle.
    private static (AudioFeatures, Palette?, Settings) Music(int frame)
    {
        float t = frame / 60f;
        float sinceBeat = t % 0.5f;
        bool silent = t is > 120 and < 140;
        var audio = silent ? new AudioFeatures(0, 0, 0, true) : new AudioFeatures(0.5f + 0.4f * MathF.Exp(-sinceBeat / 0.18f), 0.6f, MathF.Exp(-sinceBeat / 0.12f), false);
        Palette? palette = frame % 180 == 0 ? new Palette(new Rgb(frame % 360 / 360f, 0.2f, 0.8f), new Rgb(0.9f, 0.5f, 0.1f), $"track{frame / 180}") : null;
        return (audio, palette, MusicSettings);
    }

    // One preview-like pair: Idle Glow, a manual color drag (a new palette every 5 frames), Glow changing.
    private static (AudioFeatures, Palette?, Settings) Idle(int frame)
    {
        Palette? palette = frame % 5 == 0 ? new Palette(new Rgb(0.1f, frame % 100 / 100f, 0.3f), new Rgb(0.7f, 0.7f, 0.2f), null) : null;
        return (new AudioFeatures(0, 0, 0, true), palette, frame % 600 < 300 ? IdleSettings : IdleSettingsWide);
    }

    private static readonly Settings MusicSettings = new() { WhenSilent = SilentBehavior.Hide };
    private static readonly Settings IdleSettings = new() { Animation = AnimationMode.IdleGlow, Glow = 0.3f };
    private static readonly Settings IdleSettingsWide = IdleSettings with { Glow = 0.8f };

    // Runs a fresh blender and engine for `frames` frames of 1/60 s and returns each frame's light state and gradient.
    private static float[][] RunPair(Func<int, (AudioFeatures Audio, Palette? Palette, Settings Settings)> script, int frames)
    {
        IPaletteBlender blender = CoreFactory.CreatePaletteBlender(Palette.Default);
        ILightEngine engine = CoreFactory.CreateLightEngine();
        Palette palette = Palette.Default;
        var gradient = new float[256];
        var output = new float[frames][];
        for (int frame = 0; frame < frames; frame++)
        {
            (AudioFeatures audio, Palette? next, Settings settings) = script(frame);
            if (next is not null)
            {
                palette = next;
                blender.SetTarget(next, TimeSpan.FromMilliseconds(next.SourceTrackId is null ? 200 : 800));
            }
            blender.Update(1 / 60f);
            LightState s = engine.Update(1 / 60f, in audio, palette, settings, paused: false);
            blender.FillGradient(gradient, s.Ratio);
            float[] row = new float[10 + 8];
            row[0] = s.Intensity; row[1] = s.Pulse; row[2] = s.Phase; row[3] = s.Visibility; row[4] = s.Spread;
            row[5] = s.Ratio; row[6] = s.CoreThicknessDip; row[7] = s.ColorA.R; row[8] = s.ColorB.G; row[9] = engine.IsStatic ? 1 : 0;
            for (int i = 0; i < 8; i++) row[10 + i] = gradient[i * 32] + gradient[i * 32 + 1] + gradient[i * 32 + 2];
            output[frame] = row;
        }
        return output;
    }

    [Fact]
    public void CoreContainsNoFakes()
    {
        Type[] types = typeof(CoreFactory).Assembly.GetTypes();
        Assert.DoesNotContain(types, t => t.Namespace?.Contains("Fakes", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(types, t => t.Name.StartsWith("Fake", StringComparison.Ordinal));
    }

    [Fact]
    public void FactorySurfaceIsTheOneCoreApiDocuments()
    {
        // The surface is pinned, so changing it fails here as a reminder to update docs/CORE-API.md.
        string[] methods = [.. typeof(CoreFactory).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["AnalysisSampleRate", "CreateAnalyzer", "CreateLightEngine", "CreatePaletteBlender", "CreatePaletteExtractor", "CreateSettingsStore"], methods);
        string[] publicTypes = [.. typeof(CoreFactory).Assembly.GetExportedTypes().Where(t => !t.IsNested).Select(t => t.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["AnalyzerDiagnostics", "AnimationMode", "AppInfo", "AudioFeatures", "AudioTuning", "BatteryBehavior", "ColorMode", "CoreFactory",
             "IAudioAnalyzer", "ILightEngine", "IPaletteBlender", "IPaletteExtractor", "ISettingsStore", "LightState", "MonitorSelection",
             "NowPlaying", "Palette", "Presets", "Rgb", "Settings", "SilentBehavior"],
            publicTypes);

        // And the page itself names every factory method and exported type in code, as `Name`, `Name(…)` or `Name.Member`,
        // so updating only the lists above doesn't pass.
        string page = File.ReadAllText(CoreApiPagePath());
        string[] undocumented = [.. methods.Concat(publicTypes).Where(name => !page.Contains('`' + name + '`', StringComparison.Ordinal)
            && !page.Contains('`' + name + '(', StringComparison.Ordinal) && !page.Contains('`' + name + '.', StringComparison.Ordinal))];
        Assert.True(undocumented.Length == 0, "docs/CORE-API.md doesn't name: " + string.Join(", ", undocumented));
    }

    // docs/CORE-API.md in the source tree, found from the test output folder (bin/<configuration>/<framework>/).
    private static string CoreApiPagePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rimlight.sln")))
                return Path.Combine(directory.FullName, "docs", "CORE-API.md");
        }
        throw new DirectoryNotFoundException("Rimlight.sln not found above " + AppContext.BaseDirectory);
    }
}
