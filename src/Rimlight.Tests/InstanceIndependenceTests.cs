using System.Reflection;
using System.Runtime.CompilerServices;
using Rimlight.Core;
using Rimlight.Tests.Audio;
using Xunit;

namespace Rimlight.Tests;

// H-017: the settings window's live preview (K7's GlowPreview, on the UI thread at about 30 fps) owns a light engine and
// a palette blender of its own while the overlay's pair runs on the render thread (MusicGlowSource), and K8's visualizer
// may add another analyzer. Each instance still has one thread (H-004), so the instances must share no mutable state:
// no static caches, scratch buffers or random generators. Two pairs driven together, interleaved on one thread or on
// two threads at once, must each give exactly what they give alone. The palette extractor has no fields at all; its
// scratch comes from ArrayPool<T>.Shared per call (thread-safe), and PaletteExtractorTests runs it on 8 threads at once.
public sealed class InstanceIndependenceTests
{
    // Shared by both pipelines, as the app shares the media service's album palettes and the settings snapshots.
    private static readonly Palette[] Albums =
    [
        new(new Rgb(0.9f, 0.2f, 0.1f), new Rgb(0.1f, 0.3f, 0.8f), "a"),
        new(new Rgb(0.2f, 0.8f, 0.3f), new Rgb(0.7f, 0.1f, 0.9f), "b"),
        Palette.Default,
    ];

    private static readonly Settings[] Looks =
    [
        new(),
        new() { WhenSilent = SilentBehavior.Hide },
        new() { Animation = AnimationMode.IdleGlow },
        new() { Animation = AnimationMode.Off },
        new() { Glow = 0.9f, PrimaryRatio = 0.3f, Brightness = 0.5f },
        new() { Enabled = false },
    ];

    // Static fields allowed in the render-thread types besides readonly fields of immutable types, with the reason
    // they are never written after the type initializer.
    private static readonly Dictionary<string, string> ReviewedStatics = new()
    {
        ["Rimlight.Core.Color.Srgb.ByteToLinearTable"] = "a 256-entry lookup the type initializer builds; only read",
    };

    // Records with init-only members, and string.
    private static readonly Type[] ImmutableTypes = [typeof(Settings), typeof(Palette), typeof(string)];

    [Fact]
    public void TheRenderThreadTypesHoldNoMutableStaticState()
    {
        // Every static field in the analyzer's, blender's, extractor's and engine's namespaces must be readonly and
        // hold an immutable value. A static scratch array that each call fills before reading would pass the
        // interleaving tests below, which run on one thread, and only break under real concurrency: this scan is the
        // guard for those. Compiler-generated delegate caches are skipped: they only ever hold equivalent delegates.
        string[] namespaces = ["Rimlight.Core.Audio", "Rimlight.Core.Color", "Rimlight.Core.Lighting"];
        var problems = new List<string>();
        var seen = new List<string>();
        int types = 0;
        foreach (Type type in typeof(CoreFactory).Assembly.GetTypes())
        {
            if (!namespaces.Contains(type.Namespace) || type.IsDefined(typeof(CompilerGeneratedAttribute))) continue;
            types++;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral) continue;
                string name = $"{type.FullName}.{field.Name}";
                seen.Add(name);
                Type fieldType = field.FieldType;
                bool immutable = fieldType.IsPrimitive || fieldType.IsEnum || ImmutableTypes.Contains(fieldType);
                if (field.IsDefined(typeof(ThreadStaticAttribute))) problems.Add($"{name} is [ThreadStatic]");
                else if (!field.IsInitOnly) problems.Add($"{name} is not readonly");
                else if (!immutable && !ReviewedStatics.ContainsKey(name)) problems.Add($"{name} holds a {fieldType.Name}, which can change");
            }
        }
        Assert.Empty(problems);
        Assert.True(types >= 20, $"only {types} types found");
        // The three there are today, so a rename can't make the scan pass by finding nothing.
        Assert.Contains("Rimlight.Core.Color.Srgb.ByteToLinearTable", seen);
        Assert.Contains("Rimlight.Core.Color.PaletteBlender.DefaultRatio", seen);
        Assert.Contains("Rimlight.Core.Lighting.LightEngine.Defaults", seen);
    }

    // One glow as the app drives it: a light engine and a palette blender from CoreFactory, palette changes with
    // MusicGlowSource's fades, settings switches, pauses, beats and silences, all from its own seeded generator. Each
    // frame records a hash of every bit the renderer reads: the light state, IsStatic, IsAnimating and the gradient, and
    // of Current before and after the step. The app reads Current only off the per-frame path (H-004); reading it on
    // every frame here gives its cached blend every chance to leak between instances.
    private sealed class GlowPipeline(float fps, int seed, bool preview)
    {
        private readonly ILightEngine engine = CoreFactory.CreateLightEngine();
        private readonly IPaletteBlender blender = CoreFactory.CreatePaletteBlender(Albums[0]);
        private readonly float[] gradient = new float[256];
        private readonly Random random = new(seed);
        private Settings settings = Looks[0];
        private Palette palette = Albums[0];
        private bool paused, silent;
        private float beat;
        private int frame;

        public List<ulong> Hashes { get; } = [];

        public void Step()
        {
            frame++;
            float dt = 1 / fps * (1 + 0.1f * (float)(random.NextDouble() * 2 - 1));
            if (random.Next((int)(4 * fps)) == 0)
            {
                // A new album (800 ms), or in the preview a manual color edit (200 ms): a palette of its own.
                palette = preview && random.Next(2) == 0
                    ? new Palette(new Rgb((float)random.NextDouble(), 0.5f, 0.2f), Albums[1].Secondary, null)
                    : Albums[random.Next(Albums.Length)];
                blender.SetTarget(palette, TimeSpan.FromMilliseconds(preview ? 200 : 800));
            }
            if (random.Next((int)(5 * fps)) == 0)
            {
                settings = Looks[random.Next(Looks.Length)];
                // The preview shows the glow as it looks when on (GlowPreview): Off shows Idle Glow, disabled shows on.
                if (preview && settings.Animation == AnimationMode.Off) settings = Looks[2];
                else if (preview && !settings.Enabled) settings = settings with { Enabled = true };
            }
            if (!preview && random.Next((int)(8 * fps)) == 0) paused = !paused;
            if (random.Next((int)(6 * fps)) == 0) silent = !silent;
            beat = !silent && random.Next((int)(fps / 2)) == 0 ? 1 : beat * MathF.Exp(-dt / 0.12f);
            var audio = new AudioFeatures(silent ? 0 : (float)random.NextDouble(), 0.5f, beat, silent);

            ulong hash = Hash(14695981039346656037, blender.Current);
            blender.Update(dt);
            LightState state = engine.Update(dt, audio, palette, settings, paused);
            blender.FillGradient(gradient, state.Ratio);

            hash = Hash(hash, state.ColorA.R, state.ColorA.G, state.ColorA.B, state.ColorB.R, state.ColorB.G,
                state.ColorB.B, state.Ratio, state.Intensity, state.Spread, state.CoreThicknessDip, state.Phase, state.Pulse, state.Visibility);
            hash = Hash(hash, engine.IsStatic ? 1 : 0, blender.IsAnimating ? 1 : 0);
            foreach (float texel in gradient) hash = Hash(hash, texel);
            Hashes.Add(Hash(hash, blender.Current));
        }
    }

    private static ulong Hash(ulong hash, Palette p) =>
        Hash(hash, p.Primary.R, p.Primary.G, p.Primary.B, p.Secondary.R, p.Secondary.G, p.Secondary.B);

    // FNV-1a over the raw bits of each value.
    private static ulong Hash(ulong hash, params float[] values)
    {
        foreach (float value in values)
        {
            hash ^= (uint)BitConverter.SingleToInt32Bits(value);
            hash *= 1099511628211;
        }
        return hash;
    }

    private const int OverlayFrames = 60 * 90, PreviewFrames = 30 * 90; // 90 s each

    private static GlowPipeline Overlay() => new(60, 1, preview: false);

    private static GlowPipeline Preview() => new(30, 2, preview: true);

    private static (List<ulong> Overlay, List<ulong> Preview) Solo()
    {
        GlowPipeline overlay = Overlay(), preview = Preview();
        for (int i = 0; i < OverlayFrames; i++) overlay.Step();
        for (int i = 0; i < PreviewFrames; i++) preview.Step();
        return (overlay.Hashes, preview.Hashes);
    }

    private static void AssertSame(List<ulong> expected, List<ulong> actual, string what)
    {
        Assert.Equal(expected.Count, actual.Count);
        int first = expected.Zip(actual).TakeWhile(p => p.First == p.Second).Count();
        Assert.True(first == expected.Count, $"{what} differs from its solo run from frame {first}");
    }

    [Fact]
    public void TwoEnginesAndBlendersInterleavedMatchTheirSoloRunsBitForBit()
    {
        // The overlay at 60 fps and the preview at 30 fps, stepped in time order on one thread.
        var (soloOverlay, soloPreview) = Solo();
        Assert.True(soloOverlay.Distinct().Count() > OverlayFrames / 2); // the scenario moves
        GlowPipeline overlay = Overlay(), preview = Preview();
        int o = 0, p = 0;
        while (o < OverlayFrames || p < PreviewFrames)
        {
            bool overlayNext = p >= PreviewFrames || (o < OverlayFrames && (o + 1) / 60.0 <= (p + 1) / 30.0);
            if (overlayNext) { overlay.Step(); o++; }
            else { preview.Step(); p++; }
        }
        AssertSame(soloOverlay, overlay.Hashes, "the overlay's pair");
        AssertSame(soloPreview, preview.Hashes, "the preview's pair");
    }

    [Fact]
    public void TwoEnginesAndBlendersOnTwoThreadsMatchTheirSoloRunsBitForBit()
    {
        // The render thread and the UI thread at once, each with its own pair. Several rounds, for overlap.
        var (soloOverlay, soloPreview) = Solo();
        for (int round = 0; round < 4; round++)
        {
            GlowPipeline overlay = Overlay(), preview = Preview();
            using var start = new Barrier(2);
            var render = new Thread(() => { start.SignalAndWait(); for (int i = 0; i < OverlayFrames; i++) overlay.Step(); });
            var ui = new Thread(() => { start.SignalAndWait(); for (int i = 0; i < PreviewFrames; i++) preview.Step(); });
            render.Start();
            ui.Start();
            render.Join();
            ui.Join();
            AssertSame(soloOverlay, overlay.Hashes, "the overlay's pair");
            AssertSame(soloPreview, preview.Hashes, "the preview's pair");
        }
    }

    // An analyzer fed a track the way the render loop feeds it: each frame drains what has arrived, except every
    // seventh frame, when no packet has (the next frame then gets both). Each frame records a hash of the features and
    // of every diagnostics value.
    private sealed class AnalyzerFeed(AudioTuning? tuning, float[] track, int rate, int samplesPerFrame)
    {
        private int frame, position;

        public IAudioAnalyzer Analyzer { get; } = CoreFactory.CreateAnalyzer(tuning);

        public List<ulong> Hashes { get; } = [];

        public void Step()
        {
            frame++;
            int arrived = frame % 7 == 3 ? position : Math.Min(track.Length, frame * samplesPerFrame);
            ReadOnlySpan<float> samples = track.AsSpan(position, arrived - position);
            position = arrived;
            AudioFeatures f = Analyzer.Process(samples, rate, samplesPerFrame / (float)rate);
            AnalyzerDiagnostics d = Analyzer.Diagnostics;
            ulong hash = Hash(14695981039346656037, f.Level, f.Bass, f.Beat, f.IsSilent ? 1 : 0, d.EstimatedBpm, d.BeatCount);
            hash = Hash(hash, d.Spectrum);
            hash = Hash(hash, d.FluxHistory);
            hash = Hash(hash, d.ThresholdHistory);
            Hashes.Add(hash);
        }
    }

    [Fact]
    public void TwoAnalyzersInterleavedMatchTheirSoloRunsBitForBit()
    {
        // A second analyzer (K8's visualizer could own one) next to the overlay's: different tracks, rates, frame rates
        // and tunings, stepped in turn on one thread.
        float[] kicks = SyntheticAudio.KickTrack(48000, 12, 128, 0.8f, 0.2f);
        float[] music = SyntheticAudio.MusicLike(44100, 12, 124);
        AnalyzerFeed First() => new(null, kicks, 48000, 800);                                        // 60 fps
        AnalyzerFeed Second() => new(new AudioTuning { Sensitivity = 1.6f }, music, 44100, 441);     // 100 fps
        const int Frames = 12 * 60;

        AnalyzerFeed soloFirst = First(), soloSecond = Second();
        for (int i = 0; i < Frames; i++) soloFirst.Step();
        for (int i = 0; i < Frames; i++) soloSecond.Step();
        Assert.True(soloFirst.Analyzer.Diagnostics.BeatCount > 10, $"first: {soloFirst.Analyzer.Diagnostics.BeatCount} beats");
        Assert.True(soloSecond.Analyzer.Diagnostics.BeatCount > 3, $"second: {soloSecond.Analyzer.Diagnostics.BeatCount} beats");

        AnalyzerFeed first = First(), second = Second();
        for (int i = 0; i < Frames; i++)
        {
            first.Step();
            second.Step();
        }
        AssertSame(soloFirst.Hashes, first.Hashes, "the first analyzer");
        AssertSame(soloSecond.Hashes, second.Hashes, "the second analyzer");
    }
}
