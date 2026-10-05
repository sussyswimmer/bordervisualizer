namespace Rimlight.Core.Audio;

internal sealed class AutoGain
{
    private bool initialized;
    public float Peak { get; private set; }
    public float FloorDb { get; private set; }

    public float Update(float magnitude, float dtSeconds, AudioTuning tuning)
    {
        float valueDb = ToDb(magnitude, tuning.MagnitudeEpsilon);
        if (!initialized)
        {
            // Leading exact digital silence must not establish an absolute floor;
            // seed from the first signal so scaled inputs start with scaled state.
            if (magnitude <= 0) return 0;
            Peak = magnitude;
            FloorDb = valueDb;
            initialized = true;
        }
        else
        {
            // Decay linear amplitude, never a negative dB value.
            Peak = MathF.Max(magnitude, Peak * MathF.Exp(-MathF.Log(2) * dtSeconds / tuning.PeakHalfLifeSeconds));
            FloorDb = MathF.Min(valueDb, FloorDb + tuning.FloorRiseDbPerSecond * dtSeconds);
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
