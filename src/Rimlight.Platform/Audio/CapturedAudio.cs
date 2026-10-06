namespace Rimlight.Platform.Audio;

/// <summary>
/// One capture session's audio: mono float samples from the default render device, plus what it is. The render
/// thread drains it once per frame with <see cref="Read"/> (single consumer). Obtain it from
/// <see cref="LoopbackCapture.Current"/>.
/// </summary>
public sealed class CapturedAudio
{
    private readonly AudioRingBuffer ring;

    internal CapturedAudio(AudioRingBuffer ring, int sampleRate, string deviceName, string format)
    {
        this.ring = ring;
        SampleRate = sampleRate;
        DeviceName = deviceName;
        Format = format;
    }

    /// <summary>Sample rate of the captured device, in Hz (pass it to <c>IAudioAnalyzer.Process</c>).</summary>
    public int SampleRate { get; }

    /// <summary>The device's friendly name, e.g. "Speakers (Realtek(R) Audio)".</summary>
    public string DeviceName { get; }

    /// <summary>The shared-mode mix format, e.g. "48000 Hz, 2 ch, 32-bit float".</summary>
    public string Format { get; }

    /// <summary>Ring capacity in samples (at least 2 s of audio); a destination this large never skips samples.</summary>
    public int Capacity => ring.Capacity;

    /// <summary>Samples dropped because the reader fell more than <see cref="Capacity"/> behind.</summary>
    public long DroppedSamples => ring.Dropped;

    /// <summary>
    /// Copies every sample captured since the previous call into <paramref name="destination"/>, oldest first, and
    /// returns how many. When more are waiting than fit, the oldest are skipped. Call from one thread only (the
    /// render thread); never blocks or allocates.
    /// </summary>
    /// <param name="destination">Receives the samples.</param>
    /// <returns>The number of samples written.</returns>
    public int Read(Span<float> destination) => ring.Read(destination);
}
