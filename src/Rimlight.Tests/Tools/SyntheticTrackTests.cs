using System.Security.Cryptography;
using Rimlight.AudioTools;
using Xunit;

namespace Rimlight.Tests.Tools;

// C3: the synthetic-track generator used by wav-analyze, its tests, Rimlight.Bench and C12's soak.
public sealed class SyntheticTrackTests
{
    [Fact]
    public void SplitMixMatchesItsReferenceSequence()
    {
        // SplitMix64 (Steele, Lea, Flood 2014), seed 0: the published first outputs.
        var random = new DeterministicRandom(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, DeterministicRandom.Hash(0, 1)); // random access agrees with the stream
        Assert.InRange(DeterministicRandom.ToSigned(0), -1, -1);
        Assert.InRange(DeterministicRandom.ToSigned(ulong.MaxValue), 0.9999999, 1 - 1e-16);
    }

    [Fact]
    public void SameOptionsGiveIdenticalSamplesAndFiles()
    {
        var options = new SyntheticTrackOptions { Seconds = 3, Seed = 7 }.WithFullMix();
        float[] first = SyntheticTrack.Render(options);
        float[] second = SyntheticTrack.Render(options);
        Assert.Equal(first, second);
        Assert.Equal(Hash(first, options.SampleRate), Hash(second, options.SampleRate));
    }

    [Fact]
    public void TheSeedChangesNoiseAndMelodyButNotTheKicks()
    {
        var a = new SyntheticTrackOptions { Seconds = 4, Seed = 1 };
        float[] one = SyntheticTrack.Render(a);
        float[] other = SyntheticTrack.Render(a with { Seed = 2 });
        Assert.NotEqual(one, other);

        var kicksOnly = a with { NoiseLevel = 0, VocalLevel = 0 };
        Assert.Equal(SyntheticTrack.Render(kicksOnly), SyntheticTrack.Render(kicksOnly with { Seed = 99 }));
    }

    [Fact]
    public void StreamingInAnyChunkSizeMatchesRenderingAtOnce()
    {
        var options = new SyntheticTrackOptions { Seconds = 2, SampleRate = 44100, IntroSeconds = 0.25, OutroSeconds = 0.25 }.WithFullMix();
        float[] whole = SyntheticTrack.Render(options);
        var track = new SyntheticTrack(options);
        var streamed = new List<float>();
        float[] buffer = new float[4096];
        int[] sizes = [1, 7, 480, 4096, 333];
        for (int i = 0; ; i++)
        {
            int count = track.Read(buffer.AsSpan(0, sizes[i % sizes.Length]));
            if (count == 0) break;
            streamed.AddRange(buffer.AsSpan(0, count).ToArray());
        }
        Assert.Equal(whole, streamed);

        track.Position = 12345; // random access
        track.Read(buffer.AsSpan(0, 10));
        Assert.Equal(whole.AsSpan(12345, 10).ToArray(), buffer.AsSpan(0, 10).ToArray());
    }

    [Fact]
    public void ReadDoesNotAllocate()
    {
        // C12 streams hours of audio from it on the measured thread.
        var track = new SyntheticTrack(new SyntheticTrackOptions { Seconds = 60 }.WithFullMix());
        float[] buffer = new float[800];
        for (int i = 0; i < 100; i++) track.Read(buffer); // past the tier-up call counts
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) track.Read(buffer);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(120, 20, 0, 40)]
    [InlineData(128, 10, 1.5, 22)]   // 21.33 beats: kicks at 0 … 9.84 s
    [InlineData(174, 3, 0, 9)]       // 8.7 beats
    public void KickTimesAreOnTheBeatGrid(double bpm, double seconds, double intro, int count)
    {
        var track = new SyntheticTrack(new SyntheticTrackOptions { Bpm = bpm, Seconds = seconds, IntroSeconds = intro });
        double[] kicks = track.KickTimes();
        Assert.Equal(count, kicks.Length);
        Assert.Equal(intro, kicks[0], 9);
        for (int k = 1; k < kicks.Length; k++) Assert.Equal(60 / bpm, kicks[k] - kicks[k - 1], 9);
        Assert.Empty(new SyntheticTrack(new SyntheticTrackOptions { KickLevel = 0 }).KickTimes());
    }

    [Fact]
    public void IntroAndOutroAreDigitalSilenceAndGainScalesEverything()
    {
        var options = new SyntheticTrackOptions { Seconds = 2, IntroSeconds = 0.5, OutroSeconds = 0.5 }.WithFullMix();
        float[] samples = SyntheticTrack.Render(options);
        int rate = options.SampleRate;
        Assert.Equal(3 * rate, samples.Length);
        Assert.All(samples[..(rate / 2)], s => Assert.Equal(0f, s));
        Assert.All(samples[(int)(2.5 * rate)..], s => Assert.Equal(0f, s));
        float peak = samples.Max(MathF.Abs);
        Assert.InRange(peak, 0.5f, 1f);

        float[] quiet = SyntheticTrack.Render(options with { Gain = 0.01 });
        for (int i = 0; i < samples.Length; i++) Assert.Equal(samples[i] * 0.01f, quiet[i], 1e-6f);
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticTrack(new SyntheticTrackOptions { Bpm = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticTrack(new SyntheticTrackOptions { SampleRate = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticTrack(new SyntheticTrackOptions { Seconds = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticTrack(new SyntheticTrackOptions { NoiseLevel = -1 }));
    }

    [Fact]
    public void VocalTonesAndNoiseAloneFireNoBeats()
    {
        // The vocal-ish melody sits at 220–440 Hz with onsets off the beat; it must not reach the bass-band detector.
        var options = new SyntheticTrackOptions { Seconds = 30, KickLevel = 0, VocalLevel = 0.4, NoiseLevel = 0.05 };
        AnalysisResult result = OfflineAnalysis.Run(SyntheticTrack.Render(options), options.SampleRate, new FeedOptions { Jitter = 0.2 });
        Assert.Empty(result.BeatTimes);
        Assert.Contains(result.Frames, f => f.Features.Level > 0.3f); // ...while the light still follows the level
    }

    private static string Hash(float[] samples, int rate)
    {
        var stream = new MemoryStream();
        WavWriter.Write(stream, samples, rate, 1, WavSampleFormat.Pcm16);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
