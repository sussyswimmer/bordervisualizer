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

    // Feeds a buffer through an analyzer in render-frame-sized chunks and returns beat times (seconds).
    public static List<float> Run(Rimlight.Core.IAudioAnalyzer analyzer, float[] samples, int sampleRate, float fps = 60, float jitter = 0, int seed = 4)
    {
        var random = new Random(seed);
        var beats = new List<float>();
        int position = 0;
        float time = 0, previousBeat = 0;
        while (position < samples.Length)
        {
            float dt = 1 / fps * (1 + jitter * (float)(random.NextDouble() * 2 - 1));
            int count = Math.Min(samples.Length - position, (int)MathF.Round(dt * sampleRate));
            var features = analyzer.Process(samples.AsSpan(position, count), sampleRate, dt);
            position += count;
            time += dt;
            if (features.Beat >= 0.999f && previousBeat < 0.999f) beats.Add(time);
            previousBeat = features.Beat;
        }
        return beats;
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
