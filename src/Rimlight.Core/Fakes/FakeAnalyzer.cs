namespace Rimlight.Core.Fakes;

internal sealed class FakeAnalyzer(AudioTuning? tuning) : IAudioAnalyzer
{
    private float phase;
    public AudioTuning Tuning { get; set; } = tuning ?? new();
    public AnalyzerDiagnostics Diagnostics { get; } = new() { EstimatedBpm = 120f };

    public AudioFeatures Process(ReadOnlySpan<float> newSamples, int sampleRate, float dtSeconds)
    {
        float cycles = phase + MathF.Max(0f, dtSeconds) * 2f;
        int beats = (int)cycles;
        phase = cycles - beats;
        Diagnostics.BeatCount += beats;
        float pulse = MathF.Exp(-phase * 0.5f / 0.120f);
        return new AudioFeatures(0.35f + 0.65f * pulse, pulse, pulse, false);
    }

    public void Reset()
    {
        phase = 0f;
        Diagnostics.BeatCount = 0;
        Array.Clear(Diagnostics.Spectrum);
        Array.Clear(Diagnostics.FluxHistory);
        Array.Clear(Diagnostics.ThresholdHistory);
    }
}
