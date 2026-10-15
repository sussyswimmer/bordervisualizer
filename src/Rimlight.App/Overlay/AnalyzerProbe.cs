using Rimlight.Core;
using Rimlight.Platform.Audio;

namespace Rimlight.App.Overlay;

/// <summary>
/// The debug visualizer's line into the overlay thread's analyzer (doc 03 §4). The analyzer and its diagnostics belong
/// to the render thread (H-004), so nothing here reads them from the UI thread:
/// <list type="bullet">
/// <item>Diagnostics travel in two preallocated <see cref="AnalyzerSnapshot"/> buffers that change hands: the UI lends
/// one (<see cref="TakeSnapshot"/> gives back the one it drew last), the render thread fills it on its next frame and
/// hands it back. Each buffer is owned by one thread at a time, so no copy is ever read while it is written, and the
/// render thread copies at most once per UI tick (about 30 times a second). Nothing is allocated per frame.</item>
/// <item>Tuning changes travel the other way as an immutable <see cref="AudioTuning"/> in one slot (the newest wins).
/// The render thread assigns it to the analyzer before its next <c>Process</c>: the setter validates (and a
/// <see cref="AudioTuning.WindowSize"/> change reallocates), so it must run there. The result comes back as a
/// <see cref="TuningOutcome"/>.</item>
/// </list>
/// The render thread also keeps Level, Bass, Beat and the frame time of each <c>Process</c> call, which the analyzer's
/// own diagnostics don't record, in rings aligned with its 240-entry flux history. One probe serves the window for its
/// whole life, so a tuning request or outcome in flight survives a minimize; <see cref="Restart"/> starts the history
/// over instead.
/// </summary>
internal sealed class AnalyzerProbe
{
    /// <summary>History entries kept: one per <c>Process</c> call, like <see cref="AnalyzerDiagnostics.FluxHistory"/>.</summary>
    public const int HistoryLength = 240;

    // Render thread only.
    private readonly float[] level = new float[HistoryLength];
    private readonly float[] bass = new float[HistoryLength];
    private readonly float[] beat = new float[HistoryLength];
    private readonly float[] dt = new float[HistoryLength];
    private int newest = -1;
    private int count;
    private long fills;

    // Handed between the threads (see the summary).
    private AnalyzerSnapshot? request;     // lent by the UI, taken by the render thread
    private AnalyzerSnapshot? result;      // filled by the render thread, taken by the UI
    private AudioTuning? pendingTuning;    // written by the UI, taken by the render thread
    private TuningOutcome? outcome;        // written by the render thread, taken by the UI
    private int restart;                   // set by the UI, taken by the render thread

    // UI thread only: the buffer the UI drew last, lent again on the next take.
    private AnalyzerSnapshot held = new();

    /// <summary>Creates the probe with its two buffers, the first lent to the render thread already.</summary>
    public AnalyzerProbe() => request = new AnalyzerSnapshot();

    /// <summary>
    /// UI thread: the newest diagnostics if the render thread filled a buffer since the last call, else null (keep
    /// showing the previous one). The returned snapshot stays valid until the next call, which lends it out again.
    /// </summary>
    public AnalyzerSnapshot? TakeSnapshot()
    {
        AnalyzerSnapshot? fresh = Interlocked.Exchange(ref result, null);
        if (fresh is null) return null;
        // The render thread took the lent buffer to produce this one, so the slot is empty: lend the old one.
        Volatile.Write(ref request, held);
        held = fresh;
        return fresh;
    }

    /// <summary>
    /// UI thread: asks the render thread to use <paramref name="tuning"/>, replacing a request it hasn't taken yet.
    /// Its <see cref="AudioTuning.Sensitivity"/> is ignored: that comes from the settings (H-007). Wake the overlay
    /// afterwards so a stopped loop notices.
    /// </summary>
    public void RequestTuning(AudioTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        Volatile.Write(ref pendingTuning, tuning);
    }

    /// <summary>UI thread: what became of the last tuning request, once; null when nothing new.</summary>
    public TuningOutcome? TakeOutcome() => Interlocked.Exchange(ref outcome, null);

    /// <summary>
    /// UI thread: the history starts over at the next recorded frame (the window is shown again after a minimize, and
    /// its plots shouldn't join what they showed before). Pending tuning and its outcome are kept.
    /// </summary>
    public void Restart() => Volatile.Write(ref restart, 1);

    /// <summary>
    /// Render thread, before <c>Process</c>: applies a pending tuning request. Validation errors (and any other
    /// failure of the setter) leave the analyzer as it was and are reported, never thrown at the frame.
    /// </summary>
    /// <param name="analyzer">The overlay's analyzer.</param>
    /// <param name="sensitivity">The settings' sensitivity, which the analyzer keeps (H-007).</param>
    public void ApplyPendingTuning(IAudioAnalyzer analyzer, float sensitivity)
    {
        AudioTuning? requested = Interlocked.Exchange(ref pendingTuning, null);
        if (requested is null) return;
        string? error = null;
        try
        {
            // Allocates only when a request comes, never per frame.
            analyzer.Tuning = requested.Sensitivity == sensitivity ? requested : requested with { Sensitivity = sensitivity };
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        Volatile.Write(ref outcome, new TuningOutcome(requested, analyzer.Tuning, error));
    }

    /// <summary>
    /// Render thread, after each <c>Process</c>: records this call's features and frame time, and fills the lent buffer
    /// if the UI is waiting for one. Allocates nothing.
    /// </summary>
    /// <param name="features">What <c>Process</c> returned.</param>
    /// <param name="dtSeconds">The frame time passed to it.</param>
    /// <param name="analyzer">The analyzer, whose <see cref="IAudioAnalyzer.Diagnostics"/> is read now (it may be replaced).</param>
    /// <param name="sampleRate">The sample rate passed to <c>Process</c>.</param>
    /// <param name="audio">The capture session drained this frame, or null for none.</param>
    /// <param name="listening">Whether the loopback stream is open.</param>
    /// <param name="demo">Whether demo mode drives the glow instead of this audio.</param>
    public void Record(in AudioFeatures features, float dtSeconds, IAudioAnalyzer analyzer, int sampleRate, CapturedAudio? audio,
        bool listening, bool demo)
    {
        // A plain read first: a restart is rare, and most frames need no interlocked operation.
        if (Volatile.Read(ref restart) != 0 && Interlocked.Exchange(ref restart, 0) != 0)
        {
            newest = -1;
            count = 0;
        }
        newest = newest + 1 == HistoryLength ? 0 : newest + 1;
        level[newest] = features.Level;
        bass[newest] = features.Bass;
        beat[newest] = features.Beat;
        dt[newest] = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        if (count < HistoryLength) count++;

        // A plain read first: most frames find no lent buffer, and need no interlocked operation.
        if (Volatile.Read(ref request) is null) return;
        AnalyzerSnapshot? target = Interlocked.Exchange(ref request, null);
        if (target is null) return;
        Fill(target, in features, analyzer, sampleRate, audio, listening, demo);
        Volatile.Write(ref result, target);
    }

    private void Fill(AnalyzerSnapshot s, in AudioFeatures features, IAudioAnalyzer analyzer, int sampleRate, CapturedAudio? audio,
        bool listening, bool demo)
    {
        AnalyzerDiagnostics diagnostics = analyzer.Diagnostics;
        s.Sequence = ++fills;
        s.Features = features;
        s.Tuning = analyzer.Tuning;
        s.Audio = audio;
        s.AnalysisSampleRate = sampleRate > 0 ? CoreFactory.AnalysisSampleRate(sampleRate) : 0;
        s.Listening = listening;
        s.Demo = demo;
        s.EstimatedBpm = diagnostics.EstimatedBpm;
        s.BeatCount = diagnostics.BeatCount;

        int bins = Math.Min(diagnostics.Spectrum.Length, s.Spectrum.Length);
        diagnostics.Spectrum.AsSpan(0, bins).CopyTo(s.Spectrum);
        s.SpectrumLength = bins;
        CopyNewest(diagnostics.FluxHistory, s.Flux);
        CopyNewest(diagnostics.ThresholdHistory, s.Threshold);

        // The rings, oldest first and aligned at the end like the analyzer's histories.
        s.HistoryCount = count;
        Unroll(level, s.Level);
        Unroll(bass, s.Bass);
        Unroll(beat, s.Beat);
        Unroll(dt, s.Dt);
    }

    // The newest entries of an analyzer history, right-aligned in `destination`; zeros in front when it is shorter.
    private static void CopyNewest(float[] source, float[] destination)
    {
        int n = Math.Min(source.Length, destination.Length);
        Array.Clear(destination, 0, destination.Length - n);
        Array.Copy(source, source.Length - n, destination, destination.Length - n, n);
    }

    private void Unroll(float[] ring, float[] destination)
    {
        // ring[newest] is the newest; the HistoryLength - 1 before it (wrapping) are older.
        int oldest = newest + 1 == HistoryLength ? 0 : newest + 1;
        int tail = HistoryLength - oldest;
        Array.Copy(ring, oldest, destination, 0, tail);
        Array.Copy(ring, 0, destination, tail, oldest);
    }
}

/// <summary>
/// One copy of the analyzer's diagnostics and recent features, filled on the render thread by
/// <see cref="AnalyzerProbe"/>. Arrays are allocated once, at the largest size, so filling never allocates.
/// Histories are oldest first and end with the newest entry; entries before the last <see cref="HistoryCount"/>
/// predate the probe and are not valid.
/// </summary>
internal sealed class AnalyzerSnapshot
{
    /// <summary>Bins of the largest spectrum the analyzer allows (WindowSize 16384).</summary>
    public const int MaxSpectrumLength = 16384 / 2 + 1;

    /// <summary>Counts fills, from 1.</summary>
    public long Sequence { get; set; }

    /// <summary>The magnitude spectrum, DC through Nyquist (amplitude units, CoreFactory.CreateAnalyzer remarks).</summary>
    public float[] Spectrum { get; } = new float[MaxSpectrumLength];

    /// <summary>Valid bins in <see cref="Spectrum"/>: WindowSize / 2 + 1.</summary>
    public int SpectrumLength { get; set; }

    /// <summary>Flux per <c>Process</c> call.</summary>
    public float[] Flux { get; } = new float[AnalyzerProbe.HistoryLength];

    /// <summary>The effective beat threshold per call, in flux units.</summary>
    public float[] Threshold { get; } = new float[AnalyzerProbe.HistoryLength];

    /// <summary>Level per call.</summary>
    public float[] Level { get; } = new float[AnalyzerProbe.HistoryLength];

    /// <summary>Bass per call.</summary>
    public float[] Bass { get; } = new float[AnalyzerProbe.HistoryLength];

    /// <summary>Beat per call.</summary>
    public float[] Beat { get; } = new float[AnalyzerProbe.HistoryLength];

    /// <summary>The frame time of each call, in seconds (the plots' time axis).</summary>
    public float[] Dt { get; } = new float[AnalyzerProbe.HistoryLength];

    /// <summary>How many of the newest history entries the probe saw.</summary>
    public int HistoryCount { get; set; }

    /// <summary>The newest features.</summary>
    public AudioFeatures Features { get; set; }

    /// <summary>The analyzer's tuning now (Sensitivity included).</summary>
    public AudioTuning Tuning { get; set; } = new();

    /// <summary>The capture session, for its device name and format (immutable); null while nothing is captured.</summary>
    public CapturedAudio? Audio { get; set; }

    /// <summary>The rate the spectrum was computed at: bin k is k × this / WindowSize Hz.</summary>
    public int AnalysisSampleRate { get; set; }

    /// <summary>Estimated tempo; 0 when unknown.</summary>
    public float EstimatedBpm { get; set; }

    /// <summary>Beats since the analyzer's last reset.</summary>
    public int BeatCount { get; set; }

    /// <summary>Whether the loopback stream is open.</summary>
    public bool Listening { get; set; }

    /// <summary>Whether demo mode drives the glow.</summary>
    public bool Demo { get; set; }
}

/// <summary>What the render thread did with a tuning request.</summary>
/// <param name="Requested">The tuning the UI asked for.</param>
/// <param name="Applied">The analyzer's tuning afterwards (the previous one when rejected).</param>
/// <param name="Error">Why it was rejected, or null when applied.</param>
internal sealed record TuningOutcome(AudioTuning Requested, AudioTuning Applied, string? Error);
