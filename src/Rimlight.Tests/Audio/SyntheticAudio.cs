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

    // Feeds a buffer through an analyzer like the render loop does and returns beat times (seconds, frame end).
    // Frame times jitter by ±jitter. Audio arrives continuously, or in whole packets of packetSeconds (WASAPI
    // delivers 10–20 ms packets; Bluetooth and some drivers deliver larger ones), so frames can be empty.
    public static List<float> Run(Rimlight.Core.IAudioAnalyzer analyzer, float[] samples, int sampleRate, float fps = 60, float jitter = 0, int seed = 4, float packetSeconds = 0)
    {
        var random = new Random(seed);
        var beats = new List<float>();
        int packet = packetSeconds > 0 ? Math.Max(1, (int)MathF.Round(packetSeconds * sampleRate)) : 1;
        int position = 0;
        double time = 0, end = samples.Length / (double)sampleRate;
        float previousBeat = 0;
        while (time < end)
        {
            float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
            time += dt;
            long arrived = Math.Min(samples.Length, (long)(time * sampleRate) / packet * packet);
            int count = (int)Math.Max(0, arrived - position);
            var features = analyzer.Process(samples.AsSpan(position, count), sampleRate, dt);
            position += count;
            if (features.Beat >= 0.999f && previousBeat < 0.999f) beats.Add((float)time);
            previousBeat = features.Beat;
        }
        return beats;
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
