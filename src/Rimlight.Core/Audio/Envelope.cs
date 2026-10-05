namespace Rimlight.Core.Audio;

internal sealed class Envelope
{
    public float Value { get; private set; }

    public float Update(float target, float dtSeconds, float attackSeconds, float releaseSeconds)
    {
        float tau = target > Value ? attackSeconds : releaseSeconds;
        Value += (target - Value) * (1 - MathF.Exp(-dtSeconds / tau));
        return Value;
    }

    public void Reset(float value = 0) => Value = value;
}
