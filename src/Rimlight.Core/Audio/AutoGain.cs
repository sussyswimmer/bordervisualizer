namespace Rimlight.Core.Audio;

internal sealed class AutoGain
{
    private bool initialized;
    public float Peak { get; private set; }
    public float FloorDb { get; private set; }

    // hold: the frame overlaps near-silence. The floor keeps its value so gaps, pauses and dithered silence don't
    // collapse it and pin the output near 1 afterwards. The peak keeps tracking (and decaying) as usual, so a
    // quieter track after a long pause isn't normalized against a stale loud peak.
    public float Update(float magnitude, float dtSeconds, AudioTuning tuning, bool hold = false)
    {
        if (!float.IsFinite(magnitude)) magnitude = 0;
        float valueDb = ToDb(magnitude, tuning.MagnitudeEpsilon);
        if (!initialized)
        {
            // Leading silence must not establish an absolute floor;
            // seed from the first signal so scaled inputs start with scaled state.
            if (hold || magnitude <= 0) return 0;
            Peak = magnitude;
            FloorDb = valueDb;
            initialized = true;
        }
        else
        {
            // Decay linear amplitude, never a negative dB value.
            Peak = MathF.Max(magnitude, Peak * MathF.Exp(-MathF.Log(2) * dtSeconds / tuning.PeakHalfLifeSeconds));
            if (!hold && magnitude > 0) FloorDb = MathF.Min(valueDb, FloorDb + tuning.FloorRiseDbPerSecond * dtSeconds);
        }
        float range = MathF.Max(ToDb(Peak, tuning.MagnitudeEpsilon) - FloorDb, tuning.MinimumRangeDb);
        return Math.Clamp((valueDb - FloorDb) / range, 0, 1);
    }

    public void Reset()
    {
        initialized = false;
        Peak = 0;
        FloorDb = 0;
    }

    private static float ToDb(float magnitude, float epsilon) => 20 * MathF.Log10(magnitude + epsilon);
}
