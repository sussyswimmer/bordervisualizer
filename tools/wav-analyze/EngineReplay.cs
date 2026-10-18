using System.Globalization;
using System.Reflection;
using Rimlight.AudioTools;
using Rimlight.Bench;
using Rimlight.Core;

namespace Rimlight.WavAnalyze;

/// <summary>One render frame of an engine replay: what the analyzer gave and what the light engine made of it.</summary>
/// <param name="Time">Frame time in seconds (the sum of the frame durations so far): when the light updates. The
/// newest audio the frame could see is the last whole packet that had arrived by then.</param>
/// <param name="Samples">New samples passed to the analyzer this frame (0 between packets).</param>
/// <param name="Features">The analyzer's output.</param>
/// <param name="BeatCount"><see cref="AnalyzerDiagnostics.BeatCount"/> after the frame.</param>
/// <param name="Fired">Beats detected during the frame.</param>
/// <param name="Flux">The newest flux history entry.</param>
/// <param name="Threshold">The newest threshold history entry.</param>
/// <param name="Bpm"><see cref="AnalyzerDiagnostics.EstimatedBpm"/>.</param>
/// <param name="State">The light engine's state: what the overlay draws.</param>
/// <param name="Dynamics">The analyzer's internal dynamics stages, where this build has them.</param>
internal readonly record struct EngineFrame(double Time, int Samples, AudioFeatures Features, int BeatCount, int Fired, float Flux,
    float Threshold, float Bpm, LightState State, DynamicsValues Dynamics);

/// <summary>The analyzer's internal stages behind Level and Beat (CORE-API §4.2). NaN where the build has no such
/// stage, as before Maxwell's feedback round 1.</summary>
/// <param name="Position">SongLoudness.Position: where this moment sits in the song, 0..1.</param>
/// <param name="LoudnessDb">SongLoudness.LoudnessDb: the momentary weighted loudness.</param>
/// <param name="ReferenceDb">SongLoudness.ReferenceDb: the song's loud parts.</param>
/// <param name="Punch">BeatCharacter.Punch: 0 no drums .. 1 a drum groove.</param>
/// <param name="Drive">BeatCharacter.Drive, behind the punch.</param>
/// <param name="KickLike">BeatCharacter.LastKickLike: the latest onset's kick-likeness.</param>
/// <param name="Grade">BeatCharacter.LastGrade: the latest onset's grade so far.</param>
/// <param name="LevelTarget">AudioFrontEnd.LevelTarget: doc 03's weighted auto-gain bands, before any envelope.</param>
/// <param name="ProminenceDb">KickEnvelope's prominence at the frame's last step: the kick band's rise over the music
/// above the bass band (BeatDetector's looser median guard needs −4 dB).</param>
/// <param name="RiseDb">KickEnvelope's rise at the frame's last step: the kick band over its own level just
/// before.</param>
/// <param name="OnsetContrast">BeatDetector.OnsetContrast: music (MusicContrast, 0.23, and up) or a steady noise.</param>
/// <param name="Weight">BeatCharacter.Weight: 0 a light groove (a folk or rock kit) .. 1 a heavy one (rap, EDM).</param>
/// <param name="PumpDip">How much of Level the groove's pump takes away (0 .. AudioAnalyzer.PumpDepth).</param>
/// <param name="Memory">BeatCharacter.Memory: the groove the song had, which comes back after a breakdown.</param>
internal readonly record struct DynamicsValues(float Position, float LoudnessDb, float ReferenceDb, float Punch, float Drive,
    float KickLike, float Grade, float LevelTarget, float ProminenceDb, float RiseDb, float OnsetContrast, float Weight, float PumpDip,
    float Memory)
{
    public static readonly DynamicsValues None = new(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN,
        float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
}

/// <summary>
/// Replays a signal through what the overlay's render thread runs each frame: Rimlight.Bench's
/// <see cref="FramePipeline"/>, the copy of the app's MusicGlowSource.NextFrame that BenchMirrorTests keeps honest
/// (analyzer, palette blender and gradient, light engine, all through <see cref="CoreFactory"/>). Audio arrives as
/// in <see cref="OfflineAnalysis"/>: frames of 1/fps ± jitter, each passed the whole packets that had arrived.
/// </summary>
/// <remarks>
/// Not a hot path: the probes read internal values through reflection (a delegate per value, no boxing), so the same
/// tool replays builds from before and after a change to the internals, and a value a build doesn't have reads NaN.
/// </remarks>
internal static class EngineReplay
{
    /// <summary>Runs the pipeline over a mono signal.</summary>
    /// <param name="mono">The signal.</param>
    /// <param name="sampleRate">Its rate in Hz.</param>
    /// <param name="feed">Frame rate, jitter and packets.</param>
    /// <param name="settings">The settings snapshot every frame gets (Music Sync, unpaused). Its Sensitivity sets the
    /// analyzer's, as in the app.</param>
    /// <param name="tuning">Analyzer parameters to set after construction (the visualizer's live tuning), or null for
    /// the defaults. Its Sensitivity is replaced by the settings', as the app's ApplySettings does.</param>
    /// <returns>One frame per render frame, lazily.</returns>
    public static IEnumerable<EngineFrame> Run(float[] mono, int sampleRate, FeedOptions feed, Settings settings, AudioTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(mono);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(settings);
        feed.Validate();
        return Frames(mono, sampleRate, feed, settings, tuning);
    }

    private static IEnumerable<EngineFrame> Frames(float[] mono, int sampleRate, FeedOptions feed, Settings settings, AudioTuning? tuning)
    {
        var pipeline = new FramePipeline(settings, Palette.Default);
        if (tuning is not null) pipeline.Analyzer.Tuning = tuning with { Sensitivity = pipeline.Analyzer.Tuning.Sensitivity };
        var probe = new DynamicsProbe(pipeline.Analyzer);
        var random = new DeterministicRandom(feed.Seed);
        long packet = feed.PacketSeconds > 0 ? Math.Max(1, (long)Math.Round(feed.PacketSeconds * sampleRate)) : 1;
        double time = 0, end = mono.Length / (double)sampleRate;
        int position = 0, beatCount = 0;
        bool first = true;
        while (time < end)
        {
            double dt = 1 / feed.Fps * (1 + feed.Jitter * random.NextSigned());
            time += dt;
            long arrived = Math.Min(mono.Length, (long)(time * sampleRate) / packet * packet);
            int count = (int)Math.Max(0, arrived - position);
            // The first frame is the capture opening (LoopbackCapture.Current changes), as when the app starts.
            pipeline.NextFrame(mono.AsSpan(position, count), sampleRate, (float)dt, settings, latestAlbum: null, paused: false, captureChanged: first);
            first = false;
            position += count;

            AnalyzerDiagnostics diagnostics = pipeline.Analyzer.Diagnostics;
            int fired = diagnostics.BeatCount - beatCount;
            beatCount = diagnostics.BeatCount;
            float flux = diagnostics.FluxHistory.Length > 0 ? diagnostics.FluxHistory[^1] : 0;
            float threshold = diagnostics.ThresholdHistory.Length > 0 ? diagnostics.ThresholdHistory[^1] : 0;
            yield return new EngineFrame(time, count, pipeline.Features, beatCount, fired, flux, threshold, diagnostics.EstimatedBpm,
                pipeline.State, probe.Read());
        }
    }

    // Reads the analyzer's internal stages by name. The analyzer keeps them in readonly fields for its lifetime, so the
    // getters are bound once; a WindowSize change (never during a replay) would replace the front end.
    private sealed class DynamicsProbe
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Func<float>? position, loudnessDb, referenceDb, punch, drive, kickLike, grade, levelTarget;
        private readonly Func<float>? prominenceDb, riseDb, onsetContrast, weight, pumpDip, memory;

        public DynamicsProbe(IAudioAnalyzer analyzer)
        {
            object? loudness = Member(analyzer, "Loudness");
            object? character = Member(analyzer, "Character");
            object? frontEnd = Member(analyzer, "frontEnd");
            position = Getter(loudness, "Position");
            loudnessDb = Getter(loudness, "LoudnessDb");
            referenceDb = Getter(loudness, "ReferenceDb");
            punch = Getter(character, "Punch");
            drive = Getter(character, "Drive");
            kickLike = Getter(character, "LastKickLike");
            grade = Getter(character, "LastGrade");
            levelTarget = Getter(frontEnd, "LevelTarget");
            prominenceDb = Getter(analyzer, "KickProminenceDb");
            riseDb = Getter(analyzer, "KickRiseDb");
            onsetContrast = Getter(analyzer, "OnsetContrast");
            weight = Getter(character, "Weight");
            pumpDip = Getter(analyzer, "PumpDepthNow");
            memory = Getter(character, "Memory");
        }

        public DynamicsValues Read() => new(Value(position), Value(loudnessDb), Value(referenceDb), Value(punch), Value(drive),
            Value(kickLike), Value(grade), Value(levelTarget), Value(prominenceDb), Value(riseDb), Value(onsetContrast), Value(weight),
            Value(pumpDip), Value(memory));

        private static float Value(Func<float>? getter) => getter is null ? float.NaN : getter();

        private static object? Member(object target, string name)
        {
            Type type = target.GetType();
            if (type.GetProperty(name, Instance) is { GetMethod: not null } property) return property.GetValue(target);
            return type.GetField(name, Instance)?.GetValue(target);
        }

        private static Func<float>? Getter(object? target, string name)
        {
            if (target?.GetType().GetProperty(name, Instance) is not { GetMethod: { } get } || get.ReturnType != typeof(float)) return null;
            return (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), target, get);
        }
    }
}

/// <summary>A one-line summary of the light over a replay: how bright it was and how many pulses it showed.</summary>
internal sealed class LightSummary
{
    private const float BigPulse = 0.5f, SmallPulse = 0.2f;
    private double intensitySum;
    private float minIntensity = float.MaxValue, maxIntensity, previousPulse;
    private bool rising;

    /// <summary>Frames added.</summary>
    public int Frames { get; private set; }
    /// <summary>Pulse peaks of at least 0.5.</summary>
    public int BigPulses { get; private set; }
    /// <summary>Pulse peaks of at least 0.2 (the big ones included).</summary>
    public int SmallPulses { get; private set; }
    /// <summary>The mean Intensity.</summary>
    public double MeanIntensity => Frames == 0 ? 0 : intensitySum / Frames;

    /// <summary>Adds a frame.</summary>
    /// <param name="frame">The next frame of the replay.</param>
    public void Add(in EngineFrame frame)
    {
        float intensity = frame.State.Intensity, pulse = frame.State.Pulse;
        Frames++;
        intensitySum += intensity;
        minIntensity = MathF.Min(minIntensity, intensity);
        maxIntensity = MathF.Max(maxIntensity, intensity);
        // A peak is the last frame of a rise.
        if (pulse > previousPulse) rising = true;
        else if (pulse < previousPulse && rising)
        {
            rising = false;
            if (previousPulse >= SmallPulse) SmallPulses++;
            if (previousPulse >= BigPulse) BigPulses++;
        }
        previousPulse = pulse;
    }

    /// <summary>Describes the replay.</summary>
    /// <param name="seconds">The audio's length, for the rates.</param>
    /// <returns>For example "Intensity mean 0.62 (0.28 to 0.80); pulses >= 0.5: 12 (3.0 a minute), >= 0.2: 20 (5.0 a minute)".</returns>
    public string Describe(double seconds)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        double minutes = Math.Max(seconds, 1e-9) / 60;
        return $"Intensity mean {MeanIntensity.ToString("F2", c)} ({(Frames == 0 ? 0 : minIntensity).ToString("F2", c)} to {maxIntensity.ToString("F2", c)}); " +
            $"pulses >= 0.5: {BigPulses} ({(BigPulses / minutes).ToString("F1", c)} a minute), >= 0.2: {SmallPulses} ({(SmallPulses / minutes).ToString("F1", c)} a minute)";
    }
}

/// <summary>Writes an engine replay as CSV, one row per render frame.</summary>
internal static class EngineCsv
{
    /// <summary>The header row. The last fourteen columns are the internal dynamics stages, empty where the build has none.</summary>
    public const string Header = "time,samples,level,bass,beat,isSilent,beatCount,fired,flux,threshold,bpm,intensity,pulse,spread,visibility,phase," +
        "position,loudnessDb,referenceDb,punch,drive,kickLike,grade,levelTarget,prominenceDb,riseDb,onsetContrast,weight,pumpDip,memory";

    /// <summary>Writes the frames as they come, with invariant-culture numbers.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="frames">The frames.</param>
    /// <param name="onFrame">Called with each frame after its row is written (for a summary), or null.</param>
    public static void Write(TextWriter writer, IEnumerable<EngineFrame> frames, Action<EngineFrame>? onFrame = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(frames);
        CultureInfo c = CultureInfo.InvariantCulture;
        writer.WriteLine(Header);
        foreach (EngineFrame f in frames)
        {
            writer.Write(f.Time.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Samples.ToString(c)); writer.Write(',');
            writer.Write(f.Features.Level.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.Bass.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.Beat.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.IsSilent ? '1' : '0'); writer.Write(',');
            writer.Write(f.BeatCount.ToString(c)); writer.Write(',');
            writer.Write(f.Fired.ToString(c)); writer.Write(',');
            writer.Write(f.Flux.ToString("G6", c)); writer.Write(',');
            writer.Write(f.Threshold.ToString("G6", c)); writer.Write(',');
            writer.Write(f.Bpm.ToString("F2", c)); writer.Write(',');
            writer.Write(f.State.Intensity.ToString("F4", c)); writer.Write(',');
            writer.Write(f.State.Pulse.ToString("F4", c)); writer.Write(',');
            writer.Write(f.State.Spread.ToString("F4", c)); writer.Write(',');
            writer.Write(f.State.Visibility.ToString("F4", c)); writer.Write(',');
            writer.Write(f.State.Phase.ToString("F5", c));
            DynamicsValues d = f.Dynamics;
            Optional(writer, d.Position, "F4");
            Optional(writer, d.LoudnessDb, "F2");
            Optional(writer, d.ReferenceDb, "F2");
            Optional(writer, d.Punch, "F4");
            Optional(writer, d.Drive, "F4");
            Optional(writer, d.KickLike, "F4");
            Optional(writer, d.Grade, "F4");
            Optional(writer, d.LevelTarget, "F4");
            Optional(writer, d.ProminenceDb, "F2");
            Optional(writer, d.RiseDb, "F2");
            Optional(writer, d.OnsetContrast, "F4");
            Optional(writer, d.Weight, "F4");
            Optional(writer, d.PumpDip, "F4");
            Optional(writer, d.Memory, "F4");
            writer.WriteLine();
            onFrame?.Invoke(f);
        }
    }

    private static void Optional(TextWriter writer, float value, string format)
    {
        writer.Write(',');
        if (float.IsFinite(value)) writer.Write(value.ToString(format, CultureInfo.InvariantCulture));
    }
}
