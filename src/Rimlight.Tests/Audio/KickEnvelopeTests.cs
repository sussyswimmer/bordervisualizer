using Rimlight.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 2: the time-domain kick band that grades a beat on the step it fires (KickEnvelope). Signals
// are procedural: a kick (a sine sweeping 120 → 50 Hz, decaying) over steady tones from 300 Hz to 2.5 kHz.
public sealed class KickEnvelopeTests(ITestOutputHelper output)
{
    private const int Rate = 48000;

    // Music above the bass band: four steady tones from 300 Hz to 2.5 kHz, each at `amplitude`.
    private static float[] Music(float seconds, float amplitude, int rate = Rate)
    {
        var x = new float[(int)(seconds * rate)];
        double[] tones = [300, 523.25, 1000, 2500];
        for (int i = 0; i < x.Length; i++)
            foreach (double hz in tones) x[i] += (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate));
        return x;
    }

    // A kick at sample `at`: a sine falling from 120 Hz to 50 Hz over its first few tens of milliseconds, decaying with
    // a time constant of 0.15 s.
    private static void AddKick(float[] x, int at, float amplitude, int rate = Rate)
    {
        double phase = 0;
        for (int i = at; i < Math.Min(x.Length, at + rate / 2); i++)
        {
            double t = (i - at) / (double)rate;
            phase += 2 * Math.PI * (50 + 70 * Math.Exp(-t / 0.01)) / rate;
            x[i] += (float)(amplitude * Math.Sin(phase) * Math.Exp(-t / 0.15));
        }
    }

    // The evidence once the samples up to `end` have been processed, in 10 ms packets as the analyzer's steps arrive.
    private static KickEvidence Until(KickEnvelope envelope, float[] x, ref int position, int end)
    {
        int packet = Rate / 100;
        while (position < end)
        {
            int count = Math.Min(packet, end - position);
            envelope.Process(x.AsSpan(position, count));
            position += count;
        }
        return envelope.Evidence;
    }

    private static (KickEvidence Before, KickEvidence Attack) KickOverMusic(float gain, int rate = Rate)
    {
        float[] x = Music(1.5f, 0.05f * gain, rate);
        AddKick(x, rate, 0.5f * gain, rate);
        var envelope = new KickEnvelope(rate);
        int position = 0;
        KickEvidence before = Until(envelope, x, ref position, rate);
        KickEvidence attack = Until(envelope, x, ref position, rate + rate / 50);   // 20 ms in, when a beat fires
        return (before, attack);
    }

    [Fact]
    public void AKickStandsOutOfTheMusicAboveIt()
    {
        // Before the kick the band holds only what the tones leak into it, steadily: no rise. 20 ms into the kick it has
        // risen far over its own level and well over the music above the bass band.
        var (before, attack) = KickOverMusic(1);
        output.WriteLine($"before: {before.ProminenceDb:F1} dB / rise {before.RiseDb:F1} dB; attack: {attack.ProminenceDb:F1} dB / rise {attack.RiseDb:F1} dB, peak {attack.SubPeak:F3}");
        Assert.True(before.ProminenceDb < BeatDetector.KickEvidenceDb - 20, $"{before.ProminenceDb}");
        Assert.True(before.RiseDb < 3, $"{before.RiseDb}");
        Assert.True(attack.ProminenceDb > BeatCharacter.EvidenceAtDb + 10, $"{attack.ProminenceDb}");
        Assert.True(attack.RiseDb > BeatCharacter.RiseAtDb + 10, $"{attack.RiseDb}");
        Assert.InRange(attack.SubPeak, 0.2f, 0.5f);
    }

    [Fact]
    public void TheEvidenceDoesNotDependOnTheVolume()
    {
        // Both readings are ratios: the same music 60 dB down reads the same, and only the kick's amplitude scales.
        var (loudBefore, loud) = KickOverMusic(1);
        var (quietBefore, quiet) = KickOverMusic(0.001f);
        Assert.Equal(loud.ProminenceDb, quiet.ProminenceDb, 2);
        Assert.Equal(loud.RiseDb, quiet.RiseDb, 2);
        Assert.Equal(loudBefore.ProminenceDb, quietBefore.ProminenceDb, 1);
        Assert.Equal(0.001, quiet.SubPeak / loud.SubPeak, 5);
    }

    [Fact]
    public void TheBandsAreTheSameAtFortyFourPointOneKilohertz()
    {
        var (_, at48) = KickOverMusic(1, 48000);
        var (_, at44) = KickOverMusic(1, 44100);
        output.WriteLine($"48 kHz {at48.ProminenceDb:F2} dB, 44.1 kHz {at44.ProminenceDb:F2} dB");
        Assert.Equal(at48.ProminenceDb, at44.ProminenceDb, 0);
        Assert.InRange(at44.RiseDb, at48.RiseDb - 3, at48.RiseDb + 3);
    }

    [Fact]
    public void WhiteNoiseShowsNoKick()
    {
        // White noise puts about 0.3 % of its power in 30–100 Hz: the kick band sits about 25 dB under the music above it,
        // so it never reaches BeatDetector.KickEvidenceDb (and the beat detector's looser median guard never applies).
        var random = new Random(7);
        float[] noise = new float[5 * Rate];
        for (int i = 0; i < noise.Length; i++) noise[i] = (float)(random.NextDouble() - 0.5);
        var envelope = new KickEnvelope(Rate);
        float largest = float.NegativeInfinity;
        for (int position = 0; position + 480 <= noise.Length; position += 480)
        {
            envelope.Process(noise.AsSpan(position, 480));
            if (position >= Rate / 2) largest = MathF.Max(largest, envelope.Evidence.ProminenceDb);
        }
        output.WriteLine($"white noise: largest prominence {largest:F1} dB");
        Assert.True(largest < BeatDetector.KickEvidenceDb - 6, $"{largest:F1}");
    }

    [Fact]
    public void BadSamplesCountAsSilenceAndLongSilenceForgetsEverything()
    {
        var envelope = new KickEnvelope(Rate);
        envelope.Process([float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f, -1e30f, 0.5f]);
        KickEvidence bad = envelope.Evidence;
        Assert.True(float.IsFinite(bad.ProminenceDb) && float.IsFinite(bad.RiseDb) && float.IsFinite(bad.SubPeak));

        // After a kick, 20 s of digital silence flush every filter and power to exactly 0: no subnormal numbers, and the
        // reading is a fresh instance's.
        float[] x = Music(1.5f, 0.05f);
        AddKick(x, Rate, 0.5f);
        int position = 0;
        Until(envelope, x, ref position, x.Length);
        float[] silence = new float[20 * Rate];
        position = 0;
        Assert.Equal(new KickEnvelope(Rate).Evidence, Until(envelope, silence, ref position, silence.Length));

        // Reset does the same at once.
        position = 0;
        Until(envelope, x, ref position, x.Length);
        envelope.Reset();
        Assert.Equal(new KickEnvelope(Rate).Evidence, envelope.Evidence);
    }

    [Fact]
    public void ProcessDoesNotAllocate()
    {
        float[] x = Music(2, 0.05f);
        for (int k = 0; k < 4; k++) AddKick(x, k * Rate / 2, 0.5f);
        var envelope = new KickEnvelope(Rate);
        envelope.Process(x.AsSpan(0, 480));
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        for (int position = 480; position + 480 <= x.Length; position += 480)
        {
            envelope.Process(x.AsSpan(position, 480));
            sum += envelope.Evidence.ProminenceDb;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(float.IsFinite(sum));
    }
}
