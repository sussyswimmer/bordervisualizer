using System.Diagnostics;
using Rimlight.Core;
using Rimlight.Platform.Audio;

namespace Rimlight.App.Visualizer;

/// <summary>
/// The overlay thread's half of the debug visualizer (doc 03 §4), owned by <see cref="Overlay.MusicGlowSource"/>.
/// While a <see cref="VisualizerFeed"/> is attached it applies the tunings the UI posts (H-004: only the render thread
/// touches the analyzer), records Level, Bass, Beat and the time step of every analyzer call next to the analyzer's
/// own flux and threshold histories, and about 60 times a second copies all of it into the feed's next snapshot.
/// Nothing here allocates per frame. With no feed attached it costs nothing and holds on to none
/// (<see cref="Detach"/>).
/// </summary>
internal sealed class AnalyzerTap
{
    // At most about 60 snapshots a second, whatever the frame rate: the visualizer draws at 30.
    private const float SnapshotSeconds = 1f / 60;

    private VisualizerFeed? feed;  // the feed the rings below are recorded for; null while none is attached
    private float[] level = new float[VisualizerFeed.HistoryEntries];
    private float[] bass = new float[VisualizerFeed.HistoryEntries];
    private float[] beat = new float[VisualizerFeed.HistoryEntries];
    private float[] seconds = new float[VisualizerFeed.HistoryEntries];
    private int head;              // where the next entry goes
    private float sinceSnapshot;
    private long sequence;

    /// <summary>
    /// Before the analyzer runs this frame: applies the newest tuning the visualizer posted. The analyzer's setter
    /// validates it; an invalid one throws there, the analyzer keeps its previous tuning, and the reason goes back to
    /// the UI in the snapshots.
    /// </summary>
    /// <param name="current">The attached feed.</param>
    /// <param name="analyzer">The overlay's analyzer.</param>
    /// <param name="sensitivity">The setting's Sensitivity, already clamped: it always wins over the request's (H-007).</param>
    public void ApplyTuning(VisualizerFeed current, IAudioAnalyzer analyzer, float sensitivity)
    {
        Attach(current);
        if (current.TakeTuning() is not { } request) return;
        // Settings.Sensitivity is the single source of truth (H-007). A tuning the visualizer posted just before the
        // setting changed must not set the old value back, so the setting's value replaces whatever the request holds.
        AudioTuning wanted = request.Tuning.Sensitivity == sensitivity ? request.Tuning : request.Tuning with { Sensitivity = sensitivity };
        try
        {
            analyzer.Tuning = wanted; // a WindowSize change allocates and resets in here (CoreFactory.CreateAnalyzer)
            current.TuningError = null;
        }
        catch (Exception exception)
        {
            // Validation failed (ArgumentException); the analyzer kept the previous tuning.
            current.TuningError = exception.Message;
            Trace.WriteLine($"[Visualizer] Tuning not applied: {exception.Message}");
        }
        current.HandledRequest = request.Id;
        sinceSnapshot = SnapshotSeconds; // report the outcome on this frame
    }

    /// <summary>
    /// After the analyzer ran this frame: records the frame and, at most about 60 times a second, publishes a snapshot.
    /// </summary>
    /// <param name="current">The attached feed.</param>
    /// <param name="analyzer">The overlay's analyzer, which just ran.</param>
    /// <param name="features">What it returned.</param>
    /// <param name="dtSeconds">The time step it was given.</param>
    /// <param name="audio">The captured device, or null while there is none.</param>
    /// <param name="analyzedSampleRate">The sample rate passed to the analyzer.</param>
    /// <param name="demo">Whether the glow shows the demo instead of the music.</param>
    public void Record(VisualizerFeed current, IAudioAnalyzer analyzer, in AudioFeatures features, float dtSeconds,
        CapturedAudio? audio, int analyzedSampleRate, bool demo)
    {
        Attach(current);
        AnalyzerDiagnostics diagnostics = analyzer.Diagnostics; // re-read every frame: a WindowSize change replaces it
        int history = diagnostics.FluxHistory.Length;
        if (level.Length < history) Grow(history);

        // One entry per analyzer call, like the analyzer's own histories (H-007), so the plots line up. The time step
        // is the one the analyzer got: the plots' time axis is the analyzer's time, at whatever cadence it was called.
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        level[head] = features.Level;
        bass[head] = features.Bass;
        beat[head] = features.Beat;
        seconds[head] = dt;
        head = (head + 1) % level.Length;

        sinceSnapshot += dt;
        if (sinceSnapshot < SnapshotSeconds) return;
        // The remainder carries over, so faster displays still average 60 snapshots a second.
        sinceSnapshot = MathF.Min(sinceSnapshot - SnapshotSeconds, SnapshotSeconds);

        AnalyzerSnapshot snapshot = current.Back;
        int bins = diagnostics.Spectrum.Length;
        snapshot.EnsureCapacity(bins, history);
        diagnostics.Spectrum.AsSpan().CopyTo(snapshot.Spectrum);
        snapshot.SpectrumLength = bins;
        diagnostics.FluxHistory.AsSpan(0, history).CopyTo(snapshot.Flux);
        diagnostics.ThresholdHistory.AsSpan(0, history).CopyTo(snapshot.Threshold);
        CopyOldestFirst(level, snapshot.Level, history);
        CopyOldestFirst(bass, snapshot.Bass, history);
        CopyOldestFirst(beat, snapshot.Beat, history);
        CopyOldestFirst(seconds, snapshot.FrameSeconds, history);
        snapshot.HistoryLength = history;
        snapshot.Features = features;
        snapshot.EstimatedBpm = diagnostics.EstimatedBpm;
        snapshot.BeatCount = diagnostics.BeatCount;
        snapshot.CaptureSampleRate = audio?.SampleRate ?? 0;
        snapshot.AnalysisSampleRate = CoreFactory.AnalysisSampleRate(analyzedSampleRate);
        snapshot.DeviceName = audio?.DeviceName;
        snapshot.Format = audio?.Format;
        snapshot.DroppedSamples = audio?.DroppedSamples ?? 0;
        snapshot.Tuning = analyzer.Tuning;
        snapshot.TuningRequest = current.HandledRequest;
        snapshot.TuningError = current.TuningError;
        snapshot.Demo = demo;
        snapshot.Sequence = ++sequence;
        current.Publish();
    }

    /// <summary>
    /// A frame without a visualizer: lets go of the feed (its window closed, hid or was minimized), so a closed
    /// window's snapshots aren't kept alive, and the next attachment, even of the same feed, starts over.
    /// </summary>
    public void Detach() => feed = null;

    // Every attachment starts with empty plots and gets a snapshot on its first frame. While none was attached the
    // analyzer went on adding to its own histories (one entry per call) and nothing was recorded here, so older
    // entries would pair Level, Beat and time steps from before with flux from after. The feed keeps its requests'
    // outcome.
    private void Attach(VisualizerFeed current)
    {
        if (ReferenceEquals(current, feed)) return;
        feed = current;
        Array.Clear(level);
        Array.Clear(bass);
        Array.Clear(beat);
        Array.Clear(seconds);
        head = 0;
        sinceSnapshot = SnapshotSeconds;
    }

    // An analyzer with a longer history than the default 240 entries: allocates on that one frame.
    private void Grow(int history)
    {
        level = new float[history];
        bass = new float[history];
        beat = new float[history];
        seconds = new float[history];
        head = 0;
    }

    // The newest `count` entries of a ring whose next write goes to `head`, oldest first, newest last.
    private void CopyOldestFirst(float[] ring, float[] destination, int count)
    {
        int length = ring.Length;
        int start = (head - count + length) % length;
        int first = Math.Min(count, length - start);
        ring.AsSpan(start, first).CopyTo(destination);
        ring.AsSpan(0, count - first).CopyTo(destination.AsSpan(first));
    }
}
