using Rimlight.Core;

namespace Rimlight.AudioTools;

/// <summary>How audio reaches the analyzer: a render loop's frame rate and timing, and the capture packet size.</summary>
public sealed record FeedOptions
{
    /// <summary>Render frames per second.</summary>
    public double Fps { get; init; } = 60;
    /// <summary>Frame-time jitter as a fraction: 0.2 makes each frame 80–120 % of 1/<see cref="Fps"/>.</summary>
    public double Jitter { get; init; }
    /// <summary>Capture packet length in seconds; 0 means samples arrive continuously. WASAPI delivers 10–20 ms packets.</summary>
    public double PacketSeconds { get; init; }
    /// <summary>Seed for the frame jitter.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Throws when a parameter is out of range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public void Validate()
    {
        if (!(Fps >= 1 && Fps <= 1000)) throw new ArgumentOutOfRangeException(nameof(Fps), "Expected 1 to 1000 fps.");
        if (!(Jitter >= 0 && Jitter < 1)) throw new ArgumentOutOfRangeException(nameof(Jitter), "Expected a fraction from 0 up to (not including) 1.");
        if (!(PacketSeconds >= 0 && PacketSeconds <= 1)) throw new ArgumentOutOfRangeException(nameof(PacketSeconds), "Expected 0 to 1 s.");
    }
}

/// <summary>One render frame of an offline run.</summary>
/// <param name="Time">Frame time in seconds (the sum of all frame durations so far), when the light would update.</param>
/// <param name="Samples">New samples passed to <see cref="IAudioAnalyzer.Process"/> this frame (0 between packets).</param>
/// <param name="Features">What the analyzer returned.</param>
/// <param name="Flux">The newest <see cref="AnalyzerDiagnostics.FluxHistory"/> entry: the largest bass flux of the frame.</param>
/// <param name="Threshold">The newest <see cref="AnalyzerDiagnostics.ThresholdHistory"/> entry: the effective beat threshold.</param>
/// <param name="Bpm"><see cref="AnalyzerDiagnostics.EstimatedBpm"/> after the frame; 0 while unknown.</param>
/// <param name="BeatsFired">Beats detected during the frame (normally 0 or 1).</param>
public readonly record struct AnalysisFrame(double Time, int Samples, AudioFeatures Features, float Flux, float Threshold, float Bpm, int BeatsFired);

/// <summary>The outcome of feeding a whole signal through the analyzer.</summary>
/// <param name="Frames">Every frame, in order.</param>
/// <param name="BeatTimes">The frame time of each detected beat.</param>
/// <param name="SampleRate">The signal's sample rate.</param>
/// <param name="AudioSeconds">The signal's duration.</param>
public sealed record AnalysisResult(IReadOnlyList<AnalysisFrame> Frames, IReadOnlyList<double> BeatTimes, int SampleRate, double AudioSeconds)
{
    /// <summary>The last non-zero <see cref="AnalyzerDiagnostics.EstimatedBpm"/>, and the frame time it was reported at.</summary>
    /// <returns>The estimate and its time, or (0, 0) if the analyzer never had one.</returns>
    public (float Bpm, double Time) LastAnalyzerBpm()
    {
        for (int i = Frames.Count - 1; i >= 0; i--)
            if (Frames[i].Bpm > 0) return (Frames[i].Bpm, Frames[i].Time);
        return (0, 0);
    }

    /// <summary>The fraction of frames where the analyzer reported sustained silence.</summary>
    /// <returns>0 to 1.</returns>
    public double SilentFraction() => Frames.Count == 0 ? 0 : Frames.Count(f => f.Features.IsSilent) / (double)Frames.Count;
}

/// <summary>Runs the real analyzer (<see cref="CoreFactory.CreateAnalyzer"/>) offline, frame by frame, like the render loop.</summary>
public static class OfflineAnalysis
{
    /// <summary>Feeds a mono signal through a new analyzer in frame-sized chunks.</summary>
    /// <param name="mono">The signal.</param>
    /// <param name="sampleRate">Its sample rate in Hz.</param>
    /// <param name="feed">Frame rate, jitter and packet size.</param>
    /// <param name="tuning">Analyzer parameters, or null for the defaults.</param>
    /// <returns>Every frame and the detected beat times.</returns>
    /// <remarks>
    /// Each frame lasts 1/fps (± jitter). The samples that have "arrived" by the end of the frame are passed to
    /// <see cref="IAudioAnalyzer.Process"/>: all of them when continuous, otherwise only whole packets, so some frames
    /// get none. This is the render loop of doc 02 with a perfect clock: no audio is lost or queued.
    /// </remarks>
    public static AnalysisResult Run(ReadOnlySpan<float> mono, int sampleRate, FeedOptions feed, AudioTuning? tuning = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentNullException.ThrowIfNull(feed);
        feed.Validate();
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer(tuning);
        var random = new DeterministicRandom(feed.Seed);
        long packet = feed.PacketSeconds > 0 ? Math.Max(1, (long)Math.Round(feed.PacketSeconds * sampleRate)) : 1;
        double time = 0, end = mono.Length / (double)sampleRate;
        var frames = new List<AnalysisFrame>((int)Math.Min(int.MaxValue, end * feed.Fps * 1.25 + 16));
        var beats = new List<double>();
        int position = 0, beatCount = 0;
        while (time < end)
        {
            double dt = 1 / feed.Fps * (1 + feed.Jitter * random.NextSigned());
            time += dt;
            long arrived = Math.Min(mono.Length, (long)(time * sampleRate) / packet * packet);
            int count = (int)Math.Max(0, arrived - position);
            AudioFeatures features = analyzer.Process(mono.Slice(position, count), sampleRate, (float)dt);
            position += count;

            AnalyzerDiagnostics diagnostics = analyzer.Diagnostics;
            int fired = diagnostics.BeatCount - beatCount;
            beatCount = diagnostics.BeatCount;
            float flux = diagnostics.FluxHistory.Length > 0 ? diagnostics.FluxHistory[^1] : 0;
            float threshold = diagnostics.ThresholdHistory.Length > 0 ? diagnostics.ThresholdHistory[^1] : 0;
            frames.Add(new AnalysisFrame(time, count, features, flux, threshold, diagnostics.EstimatedBpm, fired));
            for (int b = 0; b < fired; b++) beats.Add(time);
        }
        return new AnalysisResult(frames, beats, sampleRate, end);
    }
}
