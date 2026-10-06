namespace Rimlight.AudioTools;

/// <summary>
/// A small seeded generator (SplitMix64) whose sequence is fixed by this code, unlike <see cref="Random"/>, whose
/// seeded algorithm is an implementation detail of the runtime. Generated tracks and frame jitter therefore stay the
/// same across .NET versions. Not for security.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong state;

    /// <summary>Creates a generator for a seed; equal seeds give equal sequences.</summary>
    /// <param name="seed">Any value, including 0.</param>
    public DeterministicRandom(ulong seed) => state = seed;

    /// <summary>Returns the next 64 random bits.</summary>
    /// <returns>A uniformly distributed 64-bit value.</returns>
    public ulong NextUInt64() => Mix(state += Golden);

    /// <summary>
    /// Returns the value a generator seeded with <paramref name="seed"/> would return as its
    /// (<paramref name="index"/> + 1)-th call, without the calls before it, for random access into a stream.
    /// </summary>
    /// <param name="seed">The stream's seed.</param>
    /// <param name="index">The position in the stream, from 0.</param>
    /// <returns>A uniformly distributed 64-bit value.</returns>
    public static ulong Hash(ulong seed, ulong index) => Mix(seed + (index + 1) * Golden);

    /// <summary>Maps 64 random bits to a uniform value in [-1, 1).</summary>
    /// <param name="bits">Random bits, e.g. from <see cref="Hash"/>.</param>
    /// <returns>A value in [-1, 1).</returns>
    public static double ToSigned(ulong bits) => (bits >> 11) * (2.0 / (1UL << 53)) - 1;

    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Returns a uniform value in [0, 1) with 53 random bits.</summary>
    /// <returns>A value in [0, 1).</returns>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Returns a uniform value in [-1, 1).</summary>
    /// <returns>A value in [-1, 1).</returns>
    public double NextSigned() => ToSigned(NextUInt64());

    /// <summary>Returns a uniform integer in [0, maxExclusive).</summary>
    /// <param name="maxExclusive">The exclusive upper bound; must be positive.</param>
    /// <returns>A value in [0, maxExclusive).</returns>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(NextDouble() * maxExclusive);
    }
}
