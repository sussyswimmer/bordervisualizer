namespace Rimlight.Core;

/// <summary>Reusable diagnostic buffers owned by the analyzer; readers must coordinate with its render thread.</summary>
public sealed class AnalyzerDiagnostics
{
    /// <summary>Creates buffers once, for the selected window and history capacity.</summary>
    /// <param name="spectrumLength">Number of magnitude bins, normally N/2 + 1.</param>
    /// <param name="historyCapacity">Maximum diagnostic history samples.</param>
    public AnalyzerDiagnostics(int spectrumLength = 1025, int historyCapacity = 240)
    {
        Spectrum = new float[spectrumLength];
        FluxHistory = new float[historyCapacity];
        ThresholdHistory = new float[historyCapacity];
    }

    /// <summary>Preallocated magnitude spectrum, ordered from DC to Nyquist.</summary>
    public float[] Spectrum { get; }
    /// <summary>Preallocated chronological flux display history, oldest first.</summary>
    public float[] FluxHistory { get; }
    /// <summary>Preallocated chronological threshold display history, oldest first.</summary>
    public float[] ThresholdHistory { get; }
    /// <summary>Estimated tempo in beats per minute; zero when unknown.</summary>
    public float EstimatedBpm { get; set; }
    /// <summary>Total detected beats since reset.</summary>
    public int BeatCount { get; set; }
}
