using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Rimlight.Platform.Audio;

/// <summary>
/// Captures what the default render device plays (WASAPI loopback, never the microphone) as mono floats into a
/// lock-free ring the render thread drains (doc 03 §1, doc 02). Restarts on a default-device change, and with backoff
/// (0.5 s → 5 s) when the device fails or disappears.
/// </summary>
public sealed class LoopbackCapture : IDisposable
{
    private const double RingSeconds = 2;            // doc 03 §1: capacity 2 s of audio
    private const int BufferMilliseconds = 40;        // WASAPI buffer; NAudio polls it every ~20 ms
    private const int DeviceChangeSettleMs = 250;     // the default device changes once per role; restart once
    private const double FirstBackoffSeconds = 0.5;
    private const double MaxBackoffSeconds = 5;
    private const double HealthySessionSeconds = 5;   // a session this long resets the backoff

    private readonly Thread thread;
    private readonly AutoResetEvent wake = new(false);
    private volatile bool stopping;
    private int restartRequested;
    private CapturedAudio? current;

    /// <summary>Creates the capture. Call <see cref="Start"/> to begin.</summary>
    public LoopbackCapture() =>
        thread = new Thread(Supervise) { Name = "Rimlight audio capture", IsBackground = true };

    /// <summary>
    /// The audio of the current default render device, or null while there is none (no device, or restarting).
    /// A new instance appears after every restart; compare references to notice a device change.
    /// </summary>
    public CapturedAudio? Current => Volatile.Read(ref current);

    /// <summary>Starts capturing on a background thread. Errors are retried there; this never throws for them.</summary>
    public void Start() => thread.Start();

    /// <summary>Stops capturing and waits for the capture thread to finish.</summary>
    public void Dispose()
    {
        if (thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted))
        {
            wake.Dispose();
            return;
        }
        stopping = true;
        Signal();
        if (thread.Join(TimeSpan.FromSeconds(5))) wake.Dispose();
        else Trace.WriteLine("[LoopbackCapture] The capture thread did not stop within 5 s.");
    }

    private void Signal()
    {
        try
        {
            wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Stopped.
        }
    }

    private void RequestRestart()
    {
        Interlocked.Exchange(ref restartRequested, 1);
        Signal();
    }

    // Runs on a dedicated MTA thread: NAudio's WASAPI objects must not be created on the WPF (STA) thread.
    private void Supervise()
    {
        MMDeviceEnumerator? enumerator = null;
        var notifications = new DefaultDeviceNotifications(this);
        int failures = 0;
        try
        {
            enumerator = new MMDeviceEnumerator();
            enumerator.RegisterEndpointNotificationCallback(notifications);
            while (!stopping)
            {
                Interlocked.Exchange(ref restartRequested, 0);
                Session? session = null;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    session = Session.Start(enumerator, Signal);
                    Volatile.Write(ref current, session.Audio);
                    Trace.WriteLine($"[LoopbackCapture] Capturing \"{session.Audio.DeviceName}\" ({session.Audio.Format}).");
                    while (!stopping && Volatile.Read(ref restartRequested) == 0 && !session.Failed) wake.WaitOne();
                    if (session.Failed) Trace.WriteLine($"[LoopbackCapture] Capture stopped: {session.Error?.Message ?? "unknown reason"}.");
                }
                catch (Exception exception)
                {
                    Trace.WriteLine($"[LoopbackCapture] Can't capture the default render device: {exception.Message}");
                }
                finally
                {
                    Volatile.Write(ref current, null);
                    session?.Dispose();
                }
                if (stopping) break;

                double delaySeconds;
                if (Volatile.Read(ref restartRequested) != 0)
                {
                    failures = 0;
                    delaySeconds = DeviceChangeSettleMs / 1000.0;
                }
                else
                {
                    if (Stopwatch.GetElapsedTime(started).TotalSeconds >= HealthySessionSeconds) failures = 0;
                    delaySeconds = Math.Min(MaxBackoffSeconds, FirstBackoffSeconds * Math.Pow(2, failures));
                    failures++;
                }
                // A device change during the backoff restarts sooner; a stop ends the wait at once.
                Wait(delaySeconds);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[LoopbackCapture] Audio capture is unavailable: {exception}");
        }
        finally
        {
            Volatile.Write(ref current, null);
            try
            {
                enumerator?.UnregisterEndpointNotificationCallback(notifications);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[LoopbackCapture] Unregistering device notifications failed: {exception.Message}");
            }
            enumerator?.Dispose();
        }
    }

    private void Wait(double seconds)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        while (!stopping)
        {
            long remaining = deadline - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;
            wake.WaitOne(TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency));
            // After a device-change request, wait only the settle time from now.
            if (Interlocked.Exchange(ref restartRequested, 0) != 0)
                deadline = Math.Min(deadline, Stopwatch.GetTimestamp() + DeviceChangeSettleMs * Stopwatch.Frequency / 1000);
        }
    }

    // One capture of one device: WASAPI loopback → mono floats → ring. Disposing it stops the capture.
    private sealed class Session : IDisposable
    {
        private readonly MMDevice device;
        private readonly LowLatencyLoopback capture;
        private readonly CaptureFormat format;
        private readonly AudioRingBuffer ring;
        private readonly float[] mono;
        private readonly Action onStopped;
        private volatile bool failed;

        private Session(MMDevice device, LowLatencyLoopback capture, CaptureFormat format, Action onStopped)
        {
            this.device = device;
            this.capture = capture;
            this.format = format;
            this.onStopped = onStopped;
            ring = new AudioRingBuffer(AudioRingBuffer.CapacityFor(format.SampleRate, RingSeconds));
            // NAudio reuses one record buffer for the whole session; packets larger than this are converted in chunks.
            mono = new float[Math.Max(1024, format.SampleRate / 4)];
            Audio = new CapturedAudio(ring, format.SampleRate, device.FriendlyName, format.ToString());
        }

        public CapturedAudio Audio { get; }
        public bool Failed => failed;
        public Exception? Error { get; private set; }

        public static Session Start(MMDeviceEnumerator enumerator, Action onStopped)
        {
            MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            LowLatencyLoopback? capture = null;
            try
            {
                capture = new LowLatencyLoopback(device, BufferMilliseconds);
                CaptureFormat format = Describe(capture.WaveFormat)
                    ?? throw new NotSupportedException($"Unsupported mix format: {capture.WaveFormat}.");
                var session = new Session(device, capture, format, onStopped);
                capture.DataAvailable += session.OnDataAvailable;
                capture.RecordingStopped += session.OnRecordingStopped;
                capture.StartRecording();
                return session;
            }
            catch
            {
                capture?.Dispose();
                device.Dispose();
                throw;
            }
        }

        // Audio thread: no locks, no allocation, no logging (doc 02).
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            ReadOnlySpan<byte> data = e.Buffer.AsSpan(0, e.BytesRecorded);
            int frameBytes = format.BytesPerFrame;
            while (data.Length >= frameBytes)
            {
                int frames = MonoConverter.Convert(data, format, mono);
                ring.Write(mono.AsSpan(0, frames));
                data = data[(frames * frameBytes)..];
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            Error = e.Exception;
            failed = true;
            onStopped();
        }

        public void Dispose()
        {
            capture.DataAvailable -= OnDataAvailable;
            try
            {
                if (capture.CaptureState is CaptureState.Capturing or CaptureState.Starting) capture.StopRecording();
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[LoopbackCapture] Stopping the capture failed: {exception.Message}");
            }
            capture.RecordingStopped -= OnRecordingStopped;
            capture.Dispose();
            device.Dispose();
        }

        private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");       // KSDATAFORMAT_SUBTYPE_PCM
        private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71"); // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT

        // The shared-mode mix format: 32-bit float, or 16/24/32-bit PCM (doc 03 §1). Anything else isn't supported.
        private static CaptureFormat? Describe(WaveFormat waveFormat)
        {
            int channels = waveFormat.Channels;
            if (channels <= 0 || waveFormat.SampleRate <= 0) return null;
            int containerBits = waveFormat.BlockAlign * 8 / channels;
            bool isFloat = waveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                || (waveFormat is WaveFormatExtensible extensible && extensible.SubFormat == SubtypeIeeeFloat);
            bool isPcm = waveFormat.Encoding == WaveFormatEncoding.Pcm
                || (waveFormat is WaveFormatExtensible pcm && pcm.SubFormat == SubtypePcm);
            SampleEncoding? encoding = (isFloat, isPcm, containerBits) switch
            {
                (true, _, 32) => SampleEncoding.Float32,
                (_, true, 16) => SampleEncoding.Int16,
                (_, true, 24) => SampleEncoding.Int24,
                (_, true, 32) => SampleEncoding.Int32,
                _ => null,
            };
            return encoding is { } e ? new CaptureFormat(e, channels, waveFormat.SampleRate) : null;
        }
    }

    // NAudio's WasapiLoopbackCapture always uses a 100 ms buffer polled every ~50 ms, far over doc 03 §3's 10–20 ms
    // loopback budget. The same loopback stream with a 40 ms buffer is polled every ~20 ms (polling, not events:
    // loopback event callbacks aren't reliable on every Windows 10 build).
    private sealed class LowLatencyLoopback(MMDevice device, int bufferMilliseconds)
        : WasapiCapture(device, false, bufferMilliseconds)
    {
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
            AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }

    // COM callbacks arrive on an arbitrary thread and must return quickly without calling audio APIs: they only
    // request a restart, which the capture thread performs.
    private sealed class DefaultDeviceNotifications(LoopbackCapture owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) owner.RequestRestart();
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }

        public void OnDeviceRemoved(string deviceId) { }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
