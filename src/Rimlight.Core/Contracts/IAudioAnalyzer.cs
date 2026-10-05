namespace Rimlight.Core;

/// <summary>Render-thread audio analysis with no allocations after construction.</summary>
public interface IAudioAnalyzer
{
    /// <summary>Analyzes the newest mono samples once per render frame without allocating.</summary>
    /// <param name="newSamples">Newest mono samples; any length, including empty.</param>
    /// <param name="sampleRate">Input sample rate in Hz.</param>
    /// <param name="dtSeconds">Elapsed frame time in seconds.</param>
    /// <returns>The current audio features.</returns>
    AudioFeatures Process(ReadOnlySpan<float> newSamples, int sampleRate, float dtSeconds);
    /// <summary>Clears analysis history, for example after a device change.</summary>
    void Reset();
    /// <summary>Live-tunable parameters, replaced by the render thread.</summary>
    AudioTuning Tuning { get; set; }
    /// <summary>Preallocated diagnostics for the debug visualizer.</summary>
    AnalyzerDiagnostics Diagnostics { get; }
}
