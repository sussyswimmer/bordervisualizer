using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// One copy of what the analyzer knows, for the debug visualizer (doc 03 §4). The overlay thread fills it and hands it
/// over through <see cref="VisualizerFeed"/>; from then on it belongs to the UI thread, which only reads it. The UI must
/// never read <see cref="AnalyzerDiagnostics"/> itself, because the render thread rewrites those buffers every frame
/// (H-004, Lane B notes). Arrays are reused: only the first <see cref="SpectrumLength"/> or
/// <see cref="HistoryLength"/> entries are valid.
/// </summary>
internal sealed class AnalyzerSnapshot
{
    /// <summary>Creates a snapshot with room for the given sizes; <see cref="EnsureCapacity"/> grows it later.</summary>
    /// <param name="spectrumCapacity">Spectrum bins: WindowSize / 2 + 1.</param>
    /// <param name="historyCapacity">Entries per history: the analyzer's (240).</param>
    public AnalyzerSnapshot(int spectrumCapacity, int historyCapacity)
    {
        Spectrum = new float[spectrumCapacity];
        Flux = new float[historyCapacity];
        Threshold = new float[historyCapacity];
        Level = new float[historyCapacity];
        Bass = new float[historyCapacity];
        Beat = new float[historyCapacity];
        FrameSeconds = new float[historyCapacity];
    }

    /// <summary>Counts snapshots, from 1; 0 means nothing was written yet.</summary>
    public long Sequence { get; set; }

    /// <summary>
    /// <see cref="AnalyzerDiagnostics.Spectrum"/>: DC to Nyquist in amplitude units (a full-scale sine centered on a
    /// bin reads about 1). Bin k is at k × <see cref="AnalysisSampleRate"/> / <see cref="WindowSize"/> Hz.
    /// </summary>
    public float[] Spectrum { get; private set; }

    /// <summary>Valid bins in <see cref="Spectrum"/>.</summary>
    public int SpectrumLength { get; set; }

    /// <summary>The FFT window the spectrum came from: (bins − 1) × 2.</summary>
    public int WindowSize => Math.Max(2, (SpectrumLength - 1) * 2);

    /// <summary>
    /// The histories, oldest first, one entry per analyzer call (doc 03 §4 plots): raw flux and the effective beat
    /// threshold (same units), Level, Bass and Beat, and each call's time step, so the time axis follows the real
    /// call cadence.
    /// </summary>
    public float[] Flux { get; private set; }

    /// <inheritdoc cref="Flux"/>
    public float[] Threshold { get; private set; }

    /// <inheritdoc cref="Flux"/>
    public float[] Level { get; private set; }

    /// <inheritdoc cref="Flux"/>
    public float[] Bass { get; private set; }

    /// <inheritdoc cref="Flux"/>
    public float[] Beat { get; private set; }

    /// <inheritdoc cref="Flux"/>
    public float[] FrameSeconds { get; private set; }

    /// <summary>Valid entries in each history.</summary>
    public int HistoryLength { get; set; }

    /// <summary>The newest frame's features.</summary>
    public AudioFeatures Features { get; set; }

    /// <summary><see cref="AnalyzerDiagnostics.EstimatedBpm"/>: 0 when unknown.</summary>
    public float EstimatedBpm { get; set; }

    /// <summary><see cref="AnalyzerDiagnostics.BeatCount"/>: beats since the last reset (a device change resets it).</summary>
    public int BeatCount { get; set; }

    /// <summary>The captured device's sample rate in Hz, or 0 while no device is captured.</summary>
    public int CaptureSampleRate { get; set; }

    /// <summary><see cref="CoreFactory.AnalysisSampleRate"/>: the rate the spectrum is computed at.</summary>
    public int AnalysisSampleRate { get; set; }

    /// <summary>The captured device's name, or null while none is captured.</summary>
    public string? DeviceName { get; set; }

    /// <summary>The captured device's mix format, e.g. "48000 Hz, 2 ch, 32-bit float", or null.</summary>
    public string? Format { get; set; }

    /// <summary>Samples the capture dropped because the overlay thread fell 2 s behind.</summary>
    public long DroppedSamples { get; set; }

    /// <summary>The analyzer's tuning in effect.</summary>
    public AudioTuning Tuning { get; set; } = new();

    /// <summary>The last tuning request the overlay thread handled (<see cref="VisualizerFeed.PostTuning"/>), 0 for none.</summary>
    public int TuningRequest { get; set; }

    /// <summary>Why that request was refused (the analyzer kept the previous tuning), or null when it was applied.</summary>
    public string? TuningError { get; set; }

    /// <summary>Whether the glow shows demo mode's synthetic beat instead of the music.</summary>
    public bool Demo { get; set; }

    /// <summary>
    /// Makes room for a spectrum and histories of these sizes. Arrays only grow, so this allocates at most once per
    /// buffer after the analyzer's diagnostics outgrow it: a larger <see cref="AudioTuning.WindowSize"/>, which
    /// allocates inside the analyzer on that frame too (<see cref="CoreFactory.CreateAnalyzer"/>).
    /// </summary>
    public void EnsureCapacity(int spectrumLength, int historyLength)
    {
        if (Spectrum.Length < spectrumLength) Spectrum = new float[spectrumLength];
        if (Flux.Length >= historyLength) return;
        Flux = new float[historyLength];
        Threshold = new float[historyLength];
        Level = new float[historyLength];
        Bass = new float[historyLength];
        Beat = new float[historyLength];
        FrameSeconds = new float[historyLength];
    }
}
