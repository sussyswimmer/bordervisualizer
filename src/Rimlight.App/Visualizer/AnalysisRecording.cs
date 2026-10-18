using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// One overlay frame of an <see cref="AnalysisRecording"/>: what the analyzer computed from the music and what the light
/// did with it. Numbers only: one value per signal, never audio samples or a spectrum (privacy, doc 01).
/// </summary>
/// <param name="Time">
/// Seconds since the recording started, as the analyzer counts time: the sum of the time steps so far, this frame's
/// included (wav-analyze's frame time).
/// </param>
/// <param name="Dt">This frame's time step in seconds, the one the analyzer and the light engine got (0 for a bad one).</param>
/// <param name="Samples">How many new mono samples the analyzer got this frame: 0 between capture packets. A count, not the samples.</param>
/// <param name="Features">What the analyzer returned.</param>
/// <param name="BeatCount">
/// <see cref="AnalyzerDiagnostics.BeatCount"/> after this frame: a beat fired where it rises, whatever its grade. It
/// starts over at 0 when the analyzer resets (a device change, a new FFT window).
/// </param>
/// <param name="Flux">This frame's bass flux: the newest <see cref="AnalyzerDiagnostics.FluxHistory"/> entry.</param>
/// <param name="Threshold">The beat threshold after this frame: the newest <see cref="AnalyzerDiagnostics.ThresholdHistory"/> entry.</param>
/// <param name="Bpm"><see cref="AnalyzerDiagnostics.EstimatedBpm"/>: 0 while unknown.</param>
/// <param name="Intensity">
/// The light state the frame returned to the overlay (<see cref="LightState.Intensity"/>), which draws it on every
/// monitor; an overlay paused on its own monitor fades it further there.
/// </param>
/// <param name="Pulse">The light state's <see cref="LightState.Pulse"/>.</param>
/// <param name="Spread">The light state's <see cref="LightState.Spread"/>.</param>
/// <param name="Visibility">The light state's <see cref="LightState.Visibility"/>.</param>
/// <param name="Phase">The light state's <see cref="LightState.Phase"/>.</param>
/// <param name="FollowsMusic">
/// Whether the light engine got this audio: music sync with the glow on, not paused and not the demo. Otherwise it
/// heard silence (or the demo's beat), and the light doesn't follow the analyzer's numbers.
/// </param>
internal readonly record struct RecordedFrame(
    double Time,
    float Dt,
    int Samples,
    AudioFeatures Features,
    int BeatCount,
    float Flux,
    float Threshold,
    float Bpm,
    float Intensity,
    float Pulse,
    float Spread,
    float Visibility,
    float Phase,
    bool FollowsMusic);

/// <summary>Why an <see cref="AnalysisRecording"/> stopped taking frames.</summary>
internal enum RecordingEnd
{
    /// <summary>Still recording.</summary>
    None,

    /// <summary>It reached its length: 2 minutes (<see cref="AnalysisRecording.MaxSeconds"/>).</summary>
    TimeLimit,

    /// <summary>Its buffer filled before that: the overlay ran above 240 frames a second.</summary>
    BufferFull,

    /// <summary>The visualizer asked it to stop (<see cref="AnalysisRecording.Stop"/>: Stop, or the window closed).</summary>
    Stopped,

    /// <summary>The overlay thread let go of it: the visualizer detached, or a newer recording took its place.</summary>
    Dropped,
}

/// <summary>
/// "Record 2 min" in the debug visualizer (doc 03 §4): what the analyzer computed and what the light did, one
/// <see cref="RecordedFrame"/> per overlay frame, so a tuning round can see what the app made of a real song on a real
/// PC. The UI thread creates it with its whole buffer and hands it over with <see cref="VisualizerFeed.StartRecording"/>;
/// the overlay thread's <see cref="AnalyzerTap"/> then appends every frame (<see cref="Append"/>) until 2 minutes are in,
/// the buffer is full or the UI asks it to <see cref="Stop"/>. It never wraps: a full recording keeps its first frames
/// and takes no more. <see cref="AnalysisRecordingCsv"/> turns it into a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Privacy (doc 01): never audio.</b> A frame holds the time and one number per signal: Level, Bass, Beat, the bass
/// flux and the beat threshold, the beat count, the tempo and the light state, plus how many samples arrived. No
/// samples, no spectrum, nothing the sound could be rebuilt from. The spectrum stays out on purpose: magnitude spectra
/// at the frame rate are enough to resynthesize recognizable audio.
/// </para>
/// <para>
/// <b>Threads, without locks (doc 02).</b> The overlay thread is the only writer. It fills the next frame, then
/// publishes the new <see cref="Count"/> with a release write; a published frame is never written again. So the UI may
/// read every frame below the count it read (an acquire read) at any time, also while the recording runs, and stopping
/// needs no handshake: the UI sets the stop flag and saves the frames published so far. A frame the overlay thread
/// was filling meanwhile lies at or past that count and is ignored. The whole buffer is allocated here, by the UI
/// thread, so the overlay thread never allocates for it (about 2 MB for 2 minutes at 240 frames a second).
/// </para>
/// </remarks>
internal sealed class AnalysisRecording
{
    /// <summary>"Record 2 min": a recording ends once it holds this many seconds.</summary>
    public const double MaxSeconds = 120;

    /// <summary>
    /// The highest frame rate at which 2 minutes fit in <see cref="DefaultCapacity"/>. Faster, the buffer fills first:
    /// Native frames on a 360 Hz display stop after 80 s.
    /// </summary>
    public const int MaxFramesPerSecond = 240;

    /// <summary>Frames in a default recording: 2 minutes at 240 frames a second (28,800 frames).</summary>
    public const int DefaultCapacity = (int)(MaxSeconds * MaxFramesPerSecond);

    private readonly RecordedFrame[] frames;
    private int count;         // frames published; written by the overlay thread only, with a release write
    private int stopRequested; // set by the UI thread
    private int end;           // a RecordingEnd; written by the overlay thread only, after its last count
    private double seconds;    // the time recorded so far; overlay thread only

    /// <summary>Creates a recording with its whole buffer. UI thread (any thread but the overlay's).</summary>
    /// <param name="capacity">The most frames it holds.</param>
    /// <param name="maxSeconds">Its length: it ends with the frame that reaches this many seconds.</param>
    public AnalysisRecording(int capacity = DefaultCapacity, double maxSeconds = MaxSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!(maxSeconds > 0) || double.IsInfinity(maxSeconds)) throw new ArgumentOutOfRangeException(nameof(maxSeconds), "Expected a positive length in seconds.");
        frames = new RecordedFrame[capacity];
        MaxDuration = maxSeconds;
    }

    /// <summary>The most frames it holds.</summary>
    public int Capacity => frames.Length;

    /// <summary>Its length in seconds: <see cref="MaxSeconds"/> unless created with another.</summary>
    public double MaxDuration { get; }

    /// <summary>Any thread: the frames published so far. Every frame below it is complete and stays as it is.</summary>
    public int Count => Volatile.Read(ref count);

    /// <summary>Any thread: why the overlay thread stopped taking frames; <see cref="RecordingEnd.None"/> while it goes on.</summary>
    public RecordingEnd End => (RecordingEnd)Volatile.Read(ref end);

    /// <summary>Any thread: whether <see cref="Stop"/> was called.</summary>
    public bool StopRequested => Volatile.Read(ref stopRequested) != 0;

    /// <summary>Any thread: the recorded time, the newest published frame's <see cref="RecordedFrame.Time"/>.</summary>
    public double Seconds
    {
        get
        {
            int published = Count;
            return published > 0 ? frames[published - 1].Time : 0;
        }
    }

    /// <summary>
    /// Any thread: the first <paramref name="published"/> frames, read-only. Pass a <see cref="Count"/> read before,
    /// so the frames are all complete; they never change afterwards.
    /// </summary>
    /// <param name="published">How many frames, at most the current <see cref="Count"/>.</param>
    public ReadOnlyMemory<RecordedFrame> Frames(int published)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(published);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(published, Count);
        return frames.AsMemory(0, published);
    }

    /// <summary>
    /// UI thread: asks the overlay thread to take no more frames; it ends on its next frame. The frames published so
    /// far (<see cref="Count"/>) can be saved at once.
    /// </summary>
    public void Stop() => Volatile.Write(ref stopRequested, 1);

    /// <summary>
    /// Overlay thread, once per frame: appends the frame, after the analyzer and the light engine ran. Allocates
    /// nothing. Returns true while the recording goes on, and false once it is over: then the frame that filled it is
    /// in, or, after a stop or an earlier end, nothing was written. The caller lets go of it then.
    /// </summary>
    /// <param name="dtSeconds">The frame's time step; a NaN, infinite or negative one counts as 0.</param>
    /// <param name="samples">New mono samples the analyzer got this frame.</param>
    /// <param name="features">What the analyzer returned.</param>
    /// <param name="beatCount">The analyzer's beat count after this frame.</param>
    /// <param name="flux">The newest flux entry.</param>
    /// <param name="threshold">The newest threshold entry.</param>
    /// <param name="bpm">The analyzer's tempo estimate.</param>
    /// <param name="light">The light state the frame returns.</param>
    /// <param name="followsMusic">Whether the light engine got this audio.</param>
    public bool Append(float dtSeconds, int samples, in AudioFeatures features, int beatCount, float flux, float threshold,
        float bpm, in LightState light, bool followsMusic)
    {
        if (end != (int)RecordingEnd.None) return false; // only this thread writes it
        if (Volatile.Read(ref stopRequested) != 0)
        {
            Finish(RecordingEnd.Stopped);
            return false;
        }
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        seconds += dt;
        int index = count;
        frames[index] = new RecordedFrame(seconds, dt, samples, features, beatCount, flux, threshold, bpm,
            light.Intensity, light.Pulse, light.Spread, light.Visibility, light.Phase, followsMusic);
        Volatile.Write(ref count, index + 1); // publishes the frame: the UI may read it from now on
        if (seconds >= MaxDuration) Finish(RecordingEnd.TimeLimit);
        else if (index + 1 == frames.Length) Finish(RecordingEnd.BufferFull);
        return end == (int)RecordingEnd.None;
    }

    /// <summary>
    /// Overlay thread: lets go of a recording that hasn't ended (the visualizer detached, or a newer recording took its
    /// place). What it holds stays readable.
    /// </summary>
    public void Drop()
    {
        if (end == (int)RecordingEnd.None) Finish(RecordingEnd.Dropped);
    }

    // After the last count, so a UI that sees the end also sees every frame.
    private void Finish(RecordingEnd reason) => Volatile.Write(ref end, (int)reason);
}
