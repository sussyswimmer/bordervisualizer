using Rimlight.App.Overlay;
using Rimlight.AudioTools;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;

namespace Rimlight.Platform.Tests;

/// <summary>What reaches the loopback stream while it is open.</summary>
internal enum AudioProgram
{
    /// <summary>A 124 BPM full mix (kick, bass, hats, a melody), as 10 ms packets.</summary>
    Music,

    /// <summary>Packets of digital silence, as a paused player in some apps sends.</summary>
    Silence,

    /// <summary>No packets at all, as WASAPI loopback when nothing plays.</summary>
    NoPackets,
}

/// <summary>
/// The app's real <see cref="MusicGlowSource"/> (real analyzer, light engine and blender from CoreFactory) with
/// stand-ins for the two Windows threads around it: the loopback capture thread, which opens the stream a moment after
/// the source asks for it, publishes a <see cref="CapturedAudio"/> and writes 10 ms packets into its ring, and the media
/// session reader, which publishes album palettes. Single-threaded and deterministic: time is whatever the caller says.
/// </summary>
internal sealed class GlowRig : IDisposable
{
    public const int SampleRate = 48000;
    private const int PacketSamples = SampleRate / 100;
    private const double PacketSeconds = 0.01;

    // 31 beats at 124 BPM are exactly 15 s, so the loop keeps the beat across its seam.
    private static readonly Lazy<float[]> MusicLoop = new(() => SyntheticTrack.Render(
        new SyntheticTrackOptions { SampleRate = SampleRate, Bpm = 124, Seconds = 15, Seed = 7 }.WithFullMix()));

    private readonly double openDelaySeconds;
    private readonly float[] packet = new float[PacketSamples];
    private readonly float[] delivered = new float[1 << 18];
    private int deliveredCount;
    private CapturedAudio? seen;
    private AudioRingBuffer? ring;
    private bool requested;
    private double openAt = double.NaN;
    private double audioTime;
    private int loopPosition;

    /// <summary>Creates the source as AppController does.</summary>
    /// <param name="settings">The settings at start.</param>
    /// <param name="openDelaySeconds">How long the capture thread takes to open a stream once asked (WASAPI setup).</param>
    public GlowRig(Settings settings, double openDelaySeconds = 0.05)
    {
        this.openDelaySeconds = openDelaySeconds;
        Media = new NowPlayingService(CoreFactory.CreatePaletteExtractor());
        Capture = new LoopbackCapture();
        Source = new MusicGlowSource(settings, Capture, Media);
    }

    public LoopbackCapture Capture { get; }

    public NowPlayingService Media { get; }

    public MusicGlowSource Source { get; }

    /// <summary>The overlay's gradient buffer, which the source refills when the colors change.</summary>
    public float[] Gradient { get; } = new float[256];

    /// <summary>What the stream carries while it is open.</summary>
    public AudioProgram Program { get; set; } = AudioProgram.Music;

    /// <summary>Whether a loopback stream is open (the capture thread has published audio).</summary>
    public bool CaptureOpen => ring is not null;

    /// <summary>How many times a stream was opened.</summary>
    public int Opens { get; private set; }

    /// <summary>Publishes a new album palette, as the media session reader does after a track change.</summary>
    public void PublishAlbum(Palette? palette) => Media.PublishForTesting(palette);

    /// <summary>What the last <see cref="MusicGlowSource.NextFrame"/> allocated on this thread, in bytes.</summary>
    public long LastFrameBytes { get; private set; }

    /// <summary>The samples the capture wrote just before the last frame: exactly what that frame drained.</summary>
    public ReadOnlySpan<float> Delivered => delivered.AsSpan(0, deliveredCount);

    /// <summary>Whether the last frame found a different stream (opened, reopened or closed) than the frame before.</summary>
    public bool StreamChanged { get; private set; }

    /// <summary>
    /// One overlay frame at <paramref name="now"/>: the capture thread catches up to it, then the source runs.
    /// </summary>
    public OverlayFrame Frame(double now, float dtSeconds, Settings settings, bool paused)
    {
        deliveredCount = 0;
        RunCapture(now);
        StreamChanged = !ReferenceEquals(Capture.Current, seen);
        seen = Capture.Current;
        long before = GC.GetAllocatedBytesForCurrentThread();
        OverlayFrame frame = Source.NextFrame(dtSeconds, settings, paused, Gradient);
        LastFrameBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        FollowRequest(now);
        return frame;
    }

    /// <summary>
    /// The capture thread up to <paramref name="now"/>: opens the stream once its setup time has passed, then writes
    /// every 10 ms packet that has arrived. Allocates only when a stream opens.
    /// </summary>
    public void RunCapture(double now)
    {
        if (!requested) return;
        if (ring is null)
        {
            if (now < openAt) return;
            ring = new AudioRingBuffer(AudioRingBuffer.CapacityFor(SampleRate, 2));
            Capture.PublishForTesting(new CapturedAudio(ring, SampleRate, "Test speakers", "48000 Hz, 2 ch, 32-bit float"));
            audioTime = openAt;
            Opens++;
        }
        while (audioTime + PacketSeconds <= now + 1e-9)
        {
            audioTime += PacketSeconds;
            switch (Program)
            {
                case AudioProgram.Music:
                    float[] music = MusicLoop.Value;
                    for (int i = 0; i < PacketSamples; i++)
                    {
                        packet[i] = music[loopPosition];
                        loopPosition = (loopPosition + 1) % music.Length;
                    }
                    Deliver();
                    break;
                case AudioProgram.Silence:
                    Array.Clear(packet);
                    Deliver();
                    break;
            }
        }
    }

    // One packet into the ring, and into the record of what the next frame drains.
    private void Deliver()
    {
        ring!.Write(packet);
        if (deliveredCount + packet.Length > delivered.Length) return; // only a test running the capture on its own
        packet.CopyTo(delivered, deliveredCount);
        deliveredCount += packet.Length;
    }

    public void Dispose()
    {
        Capture.Dispose();
        Media.Dispose();
    }

    // The source asks for audio (or stops) inside NextFrame; the capture thread opens the stream after its setup time
    // and closes it at once.
    private void FollowRequest(double now)
    {
        if (Capture.IsActive == requested) return;
        requested = Capture.IsActive;
        if (requested)
        {
            openAt = now + openDelaySeconds;
            return;
        }
        ring = null;
        Capture.PublishForTesting(null);
    }
}
