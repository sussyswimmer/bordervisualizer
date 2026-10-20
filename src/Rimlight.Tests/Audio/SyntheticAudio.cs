namespace Rimlight.Tests.Audio;

// Procedural test signals (no recorded music). Deterministic for a given seed.
internal static class SyntheticAudio
{
    // Doc 03 §5: decaying 60 Hz kick bursts at a given BPM plus white noise.
    public static float[] KickTrack(int sampleRate, float seconds, float bpm, float kickAmplitude, float noiseAmplitude, int seed = 1)
    {
        var random = new Random(seed);
        var samples = new float[(int)(sampleRate * seconds)];
        float interval = 60f / bpm;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)sampleRate;
            float sinceKick = t % interval;
            float kick = kickAmplitude * MathF.Exp(-sinceKick / 0.06f) * MathF.Sin(2 * MathF.PI * 60 * sinceKick);
            samples[i] = kick + noiseAmplitude * (float)(random.NextDouble() * 2 - 1);
        }
        return samples;
    }

    // A busier, music-like mix: kick on every beat, a bass line changing note each beat, off-beat hats,
    // a sustained pad and vocal-ish vibrato tones. Only the kick should drive bass-band onsets.
    public static float[] MusicLike(int sampleRate, float seconds, float bpm, int seed = 2)
    {
        var random = new Random(seed);
        var samples = new float[(int)(sampleRate * seconds)];
        float interval = 60f / bpm;
        float[] bassNotes = [55f, 55f, 65.4f, 49f];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)sampleRate;
            int beatIndex = (int)(t / interval);
            float sinceBeat = t - beatIndex * interval;
            float kick = 0.7f * MathF.Exp(-sinceBeat / 0.05f) * MathF.Sin(2 * MathF.PI * (50 + 60 * MathF.Exp(-sinceBeat / 0.02f)) * sinceBeat);
            float note = bassNotes[beatIndex % bassNotes.Length];
            float bass = 0.25f * MathF.Min(1, sinceBeat / 0.03f) * MathF.Sin(2 * MathF.PI * note * t);
            float sinceHat = (t + interval / 2) % interval;
            float hat = 0.08f * MathF.Exp(-sinceHat / 0.015f) * (float)(random.NextDouble() * 2 - 1);
            float pad = 0.06f * (MathF.Sin(2 * MathF.PI * 330 * t) + MathF.Sin(2 * MathF.PI * 440 * t) + MathF.Sin(2 * MathF.PI * 554 * t));
            float vocal = 0.08f * MathF.Sin(2 * MathF.PI * (880 + 12 * MathF.Sin(2 * MathF.PI * 5.5f * t)) * t) * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * 0.25f * t));
            samples[i] = kick + bass + hat + pad + vocal;
        }
        return samples;
    }

    public static float[] WhiteNoise(int sampleRate, float seconds, float amplitude, int seed = 3)
    {
        var random = new Random(seed);
        var samples = new float[(int)(sampleRate * seconds)];
        for (int i = 0; i < samples.Length; i++) samples[i] = amplitude * (float)(random.NextDouble() * 2 - 1);
        return samples;
    }

    // Brown noise at an RMS of rms: its power falls 6 dB an octave above cornerHz (a leaky integrator of white noise, as
    // noise generators make it). With swellSeconds it swells and ebbs like waves on a shore, between 0.1 and 1 of that
    // level over that period.
    public static float[] BrownNoise(int sampleRate, float seconds, float rms, float cornerHz, int seed = 15, float swellSeconds = 0)
    {
        var random = new Random(seed);
        var samples = new float[(int)(sampleRate * seconds)];
        double keep = Math.Exp(-2 * Math.PI * cornerHz / sampleRate), y = 0, squares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            y = keep * y + (1 - keep) * (random.NextDouble() * 2 - 1);
            samples[i] = (float)y;
            squares += y * y;
        }
        return Scaled(samples, rms, squares, sampleRate, swellSeconds);
    }

    // Pink noise at an RMS of rms: power falling 3 dB an octave from about 10 Hz up (Paul Kellet's filter of white noise).
    public static float[] PinkNoise(int sampleRate, float seconds, float rms, int seed = 16)
    {
        var random = new Random(seed);
        var samples = new float[(int)(sampleRate * seconds)];
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0, squares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double white = random.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + white * 0.0555179;
            b1 = 0.99332 * b1 + white * 0.0750759;
            b2 = 0.96900 * b2 + white * 0.1538520;
            b3 = 0.86650 * b3 + white * 0.3104856;
            b4 = 0.55000 * b4 + white * 0.5329522;
            b5 = -0.7616 * b5 - white * 0.0168980;
            double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362;
            b6 = white * 0.115926;
            samples[i] = (float)pink;
            squares += pink * pink;
        }
        return Scaled(samples, rms, squares, sampleRate, 0);
    }

    private static float[] Scaled(float[] samples, float rms, double squares, int sampleRate, float swellSeconds)
    {
        float scale = rms / (float)Math.Sqrt(squares / samples.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            float swell = swellSeconds > 0 ? 0.55f + 0.45f * MathF.Sin(2 * MathF.PI * i / sampleRate / swellSeconds) : 1;
            samples[i] *= scale * swell;
        }
        return samples;
    }

    // Feeds a buffer through an analyzer like the render loop does and returns beat times (seconds, frame end).
    // Frame times jitter by ±jitter. Audio arrives continuously, or in whole packets of packetSeconds (WASAPI
    // delivers 10–20 ms packets; Bluetooth and some drivers deliver larger ones), so frames can be empty.
    // afterFrame, if given, runs after each frame with the frame's end time, e.g. to sample the diagnostics.
    // A beat is a frame on which Diagnostics.BeatCount rose (the detector fired). Until Maxwell's feedback round 1 this
    // read Beat jumping to 1; Beat is now each beat's grade (kick-like, how drum-driven the music is, accented, loud
    // within the song), so detection and the pulse are checked apart: pulses, if given, collects the frames on which
    // Beat rose to StrongPulse or more (see StrongPulseRate).
    public static List<float> Run(Rimlight.Core.IAudioAnalyzer analyzer, float[] samples, int sampleRate, float fps = 60, float jitter = 0, int seed = 4, float packetSeconds = 0, Action<double>? afterFrame = null, List<float>? pulses = null)
    {
        var random = new Random(seed);
        var beats = new List<float>();
        int packet = packetSeconds > 0 ? Math.Max(1, (int)MathF.Round(packetSeconds * sampleRate)) : 1;
        int position = 0, beatCount = analyzer.Diagnostics.BeatCount;
        double time = 0, end = samples.Length / (double)sampleRate;
        float previousBeat = 0;
        while (time < end)
        {
            float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
            time += dt;
            long arrived = Math.Min(samples.Length, (long)(time * sampleRate) / packet * packet);
            int count = (int)Math.Max(0, arrived - position);
            float beat = analyzer.Process(samples.AsSpan(position, count), sampleRate, dt).Beat;
            position += count;
            int fired = analyzer.Diagnostics.BeatCount;
            if (fired > beatCount) beats.Add((float)time);
            beatCount = fired;
            if (beat >= StrongPulse && previousBeat < StrongPulse) pulses?.Add((float)time);
            previousBeat = beat;
            afterFrame?.Invoke(time);
        }
        return beats;
    }

    // A strong pulse: what a kick in a groove gives (MusicDynamicsTests uses the same bar).
    public const float StrongPulse = 0.7f;

    // The share of the detected beats at or after skipSeconds whose pulse rose to StrongPulse within maxDelay. The groove
    // takes about 2 s to establish itself (the first second only listens and the drive needs two kicks: BeatCharacter),
    // so skipSeconds is 3 s or more.
    public static float StrongPulseRate(IReadOnlyList<float> beats, IReadOnlyList<float> pulses, float skipSeconds, float maxDelay = 0.1f)
    {
        int detected = 0, strong = 0;
        foreach (float beat in beats)
        {
            if (beat < skipSeconds) continue;
            detected++;
            if (pulses.Any(p => p >= beat && p <= beat + maxDelay)) strong++;
        }
        return detected == 0 ? 0 : strong / (float)detected;
    }

    // Fraction of the kicks at or after skipSeconds that were followed by a detected beat within maxDelay.
    public static float HitRate(IReadOnlyList<float> beats, float bpm, float seconds, float skipSeconds, float maxDelay = 0.1f)
    {
        float interval = 60f / bpm;
        int kicks = 0, hits = 0;
        for (float kick = MathF.Ceiling(skipSeconds / interval) * interval; kick + maxDelay < seconds; kick += interval)
        {
            kicks++;
            if (beats.Any(t => t >= kick && t <= kick + maxDelay)) hits++;
        }
        return kicks == 0 ? 0 : hits / (float)kicks;
    }

    // Beats at or after skipSeconds that don't follow a kick within maxDelay.
    public static int FalseBeats(IReadOnlyList<float> beats, float bpm, float skipSeconds, float maxDelay = 0.1f)
    {
        float interval = 60f / bpm;
        return beats.Count(t => t >= skipSeconds && (t % interval) > maxDelay);
    }

    public static float BpmFromBeats(IReadOnlyList<float> beats, float skipSeconds)
    {
        var intervals = new List<float>();
        for (int i = 1; i < beats.Count; i++)
            if (beats[i - 1] >= skipSeconds) intervals.Add(beats[i] - beats[i - 1]);
        if (intervals.Count == 0) return 0;
        intervals.Sort();
        return 60f / intervals[intervals.Count / 2];
    }
}
