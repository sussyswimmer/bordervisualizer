using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// The debug visualizer's line to the overlay thread (doc 03 §4, H-004): analyzer snapshots come one way through a
/// <see cref="TripleBuffer{T}"/>, tuning edits the other way through a latest-wins slot. No locks: the overlay thread
/// never waits for the UI, and the UI never touches the analyzer. One feed per open visualizer window; hand it to
/// <see cref="Overlay.MusicGlowSource.SetVisualizer"/>.
/// </summary>
internal sealed class VisualizerFeed
{
    /// <summary>Spectrum bins for the analyzer's largest window (16384 samples, its validation limit).</summary>
    public const int MaxSpectrumBins = 16384 / 2 + 1;

    /// <summary>The analyzer's history length (<see cref="CoreFactory.CreateAnalyzer"/>: 240 entries).</summary>
    public const int HistoryEntries = 240;

    // Preallocated here, on the UI thread, for the largest window: the overlay thread then never allocates for
    // snapshots (about 100 KB in all, only while a visualizer is open).
    private readonly TripleBuffer<AnalyzerSnapshot> snapshots = new(
        new AnalyzerSnapshot(MaxSpectrumBins, HistoryEntries),
        new AnalyzerSnapshot(MaxSpectrumBins, HistoryEntries),
        new AnalyzerSnapshot(MaxSpectrumBins, HistoryEntries));

    private TuningRequest? pendingTuning; // written by the UI, taken by the overlay thread; the newest wins
    private int lastRequest;              // UI thread only

    /// <summary>
    /// UI thread: asks the overlay thread to apply <paramref name="tuning"/> on its next frame. A newer request
    /// replaces one it hasn't taken yet. The outcome comes back in <see cref="AnalyzerSnapshot.TuningRequest"/> and
    /// <see cref="AnalyzerSnapshot.TuningError"/>. Sensitivity is not taken from it: <see cref="Settings.Sensitivity"/>
    /// stays its only source (H-007).
    /// </summary>
    /// <param name="tuning">The whole tuning to apply.</param>
    /// <returns>The request's number, to match against the snapshots.</returns>
    public int PostTuning(AudioTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        int id = ++lastRequest;
        Volatile.Write(ref pendingTuning, new TuningRequest(tuning, id));
        return id;
    }

    /// <summary>UI thread: the newest snapshot, if one arrived since the last call. It stays valid until the next one.</summary>
    /// <param name="snapshot">The newest snapshot, or the last one taken (<see cref="AnalyzerSnapshot.Sequence"/> 0: none yet).</param>
    /// <returns>True when <paramref name="snapshot"/> is new.</returns>
    public bool TryTake(out AnalyzerSnapshot snapshot) => snapshots.TryTake(out snapshot);

    /// <summary>Overlay thread: the snapshot to fill next.</summary>
    internal AnalyzerSnapshot Back => snapshots.Back;

    /// <summary>Overlay thread: hands the filled snapshot to the UI.</summary>
    internal void Publish() => snapshots.Publish();

    /// <summary>Overlay thread: the newest tuning request, once; null when there is none.</summary>
    internal TuningRequest? TakeTuning() => Interlocked.Exchange(ref pendingTuning, null);

    /// <summary>
    /// Overlay thread: the last request it handled, 0 for none. Kept here rather than in <see cref="AnalyzerTap"/>, so
    /// it lasts as long as the window that numbers the requests, also while the window is minimized and detached.
    /// </summary>
    internal int HandledRequest { get; set; }

    /// <summary>Overlay thread: why <see cref="HandledRequest"/> was refused, or null when it was applied.</summary>
    internal string? TuningError { get; set; }
}

/// <summary>A tuning the visualizer asked for.</summary>
/// <param name="Tuning">The tuning.</param>
/// <param name="Id">Its number, counting from 1 per feed.</param>
internal sealed record TuningRequest(AudioTuning Tuning, int Id);
