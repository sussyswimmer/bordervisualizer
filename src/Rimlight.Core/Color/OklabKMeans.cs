namespace Rimlight.Core.Color;

/// <summary>Deterministic k-means in Oklab (doc 05 §2 step 2): k-means++ seeding from a fixed seed, then Lloyd iterations.</summary>
/// <remarks>
/// The same points, k, iteration cap and seed always give the same clusters. Identical points can't be split, so
/// fewer than k clusters come back when the input has fewer than k distinct colors. Stateless: thread-safe.
/// </remarks>
internal static class OklabKMeans
{
    /// <summary>The largest supported k (the centroid sums live on the stack).</summary>
    public const int MaxK = 64;

    /// <summary>Clusters <paramref name="points"/> into at most <paramref name="k"/> groups.</summary>
    /// <param name="points">The colors to cluster; at least one.</param>
    /// <param name="k">The maximum number of clusters.</param>
    /// <param name="maxIterations">The maximum number of assign-and-update rounds; stops earlier once no point moves.</param>
    /// <param name="seed">Seed for the k-means++ choices.</param>
    /// <param name="centers">Receives the cluster centroids; length at least k.</param>
    /// <param name="populations">Receives the number of points in each cluster; length at least k.</param>
    /// <param name="assignments">Receives each point's cluster index; same length as <paramref name="points"/>.</param>
    /// <param name="scratch">Scratch space for squared distances; same length as <paramref name="points"/>.</param>
    /// <returns>The number of clusters found, 1..k. A cluster can end up empty (population 0) in rare layouts.</returns>
    public static int Cluster(ReadOnlySpan<Oklab> points, int k, int maxIterations, ulong seed,
        Span<Oklab> centers, Span<int> populations, Span<int> assignments, Span<float> scratch)
    {
        if (points.IsEmpty) throw new ArgumentException("At least one point is required.", nameof(points));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(k, MaxK);
        if (centers.Length < k || populations.Length < k) throw new ArgumentException("Too little room for k clusters.");
        if (assignments.Length != points.Length || scratch.Length != points.Length)
            throw new ArgumentException("Assignments and scratch must match the point count.");

        int count = SeedCenters(points, k, seed, centers, scratch);
        assignments.Fill(-1);
        Span<double> sums = stackalloc double[count * 3];
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            bool moved = false;
            for (int i = 0; i < points.Length; i++)
            {
                int nearest = Nearest(points[i], centers[..count]);
                if (nearest != assignments[i])
                {
                    assignments[i] = nearest;
                    moved = true;
                }
            }
            if (!moved) break; // the centroids already are the means of these assignments

            sums.Clear();
            populations[..count].Clear();
            for (int i = 0; i < points.Length; i++)
            {
                int c = assignments[i];
                sums[c * 3] += points[i].L;
                sums[c * 3 + 1] += points[i].A;
                sums[c * 3 + 2] += points[i].B;
                populations[c]++;
            }
            for (int c = 0; c < count; c++)
            {
                // An emptied cluster keeps its last center and reports population 0.
                if (populations[c] == 0) continue;
                double n = populations[c];
                centers[c] = new Oklab((float)(sums[c * 3] / n), (float)(sums[c * 3 + 1] / n), (float)(sums[c * 3 + 2] / n));
            }
        }
        return count;
    }

    /// <summary>The index of the center nearest to a color; the lowest index wins a tie.</summary>
    /// <param name="point">The color.</param>
    /// <param name="centers">Candidate centers; at least one.</param>
    /// <returns>An index into <paramref name="centers"/>.</returns>
    public static int Nearest(Oklab point, ReadOnlySpan<Oklab> centers)
    {
        int best = 0;
        float bestDistance = Oklab.DistanceSquared(point, centers[0]);
        for (int c = 1; c < centers.Length; c++)
        {
            float distance = Oklab.DistanceSquared(point, centers[c]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = c;
            }
        }
        return best;
    }

    // k-means++: the first center is a uniformly random point, each further one a point drawn with probability
    // proportional to its squared distance from the nearest center chosen so far. Stops early when every point
    // coincides with a center (fewer distinct colors than k).
    private static int SeedCenters(ReadOnlySpan<Oklab> points, int k, ulong seed, Span<Oklab> centers, Span<float> nearestSquared)
    {
        var random = new SplitMix64(seed);
        centers[0] = points[(int)(random.NextDouble() * points.Length)];
        for (int i = 0; i < points.Length; i++) nearestSquared[i] = Oklab.DistanceSquared(points[i], centers[0]);

        int count = 1;
        while (count < k)
        {
            double total = 0;
            for (int i = 0; i < points.Length; i++) total += nearestSquared[i];
            if (total <= 0) break;

            double target = random.NextDouble() * total, cumulative = 0;
            int pick = -1;
            for (int i = 0; i < points.Length; i++)
            {
                if (nearestSquared[i] <= 0) continue;
                pick = i; // rounding can leave the target just past the last sum: the last candidate then wins
                cumulative += nearestSquared[i];
                if (cumulative > target) break;
            }

            centers[count] = points[pick];
            for (int i = 0; i < points.Length; i++)
                nearestSquared[i] = MathF.Min(nearestSquared[i], Oklab.DistanceSquared(points[i], centers[count]));
            count++;
        }
        return count;
    }

    // SplitMix64 (Steele, Lea and Flood): a tiny generator whose output is fixed by its definition, unlike
    // System.Random, whose seeded sequence is an implementation detail.
    private struct SplitMix64(ulong seed)
    {
        private ulong state = seed;

        // Uniform in [0, 1) from the top 53 bits.
        public double NextDouble()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return ((z ^ (z >> 31)) >> 11) * (1.0 / (1UL << 53));
        }
    }
}
