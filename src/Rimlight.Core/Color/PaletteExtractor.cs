using System.Buffers;

namespace Rimlight.Core.Color;

/// <summary>The real <see cref="IPaletteExtractor"/> (doc 05 §2): k-means in Oklab, a scored Primary and Secondary, glow-ify.</summary>
/// <remarks>
/// <para><b>Input.</b> Tightly packed BGRA8 with straight (non-premultiplied) color, any size. Pixels with alpha below
/// <see cref="MinAlpha"/> are skipped and the rest count equally. Images over <see cref="MaxSamples"/> pixels are
/// sampled on a regular grid (every 2nd, 3rd, … pixel in both directions), so a 512×512 image is used whole and
/// larger ones cost no more than it.</para>
/// <para><b>Clusters.</b> k-means with k = <see cref="ClusterCount"/> in Oklab, k-means++ seeded from the fixed
/// <see cref="Seed"/>, at most <see cref="MaxIterations"/> rounds. Clusters closer than <see cref="MergeDistance"/>
/// (Oklab distance between centroids, directly or through a chain of such clusters) are then merged into one group
/// with the summed population and the population-weighted centroid: k-means splits a textured or shaded area into
/// several clusters, and the area should score as the one region it is. Each group scores
/// share^<see cref="PopulationExponent"/> × (<see cref="ChromaBias"/> + chroma) × <see cref="LightnessFitness"/>(L),
/// where share is the group's fraction of the used pixels. Primary is the best score. Secondary is the best score
/// among groups at least <see cref="SecondaryMinDistance"/> (Oklab distance) from Primary; without one, Secondary
/// is Primary turned <see cref="DerivedHueShift"/>° in hue and <see cref="DerivedLightnessShift"/> lighter. Both are
/// then glow-ified (<see cref="Glow"/>).</para>
/// <para><b>No art</b> (doc 05 §1: a player's generic app icon instead of a cover). The result is null when the
/// palette is nearly grayscale, meaning the Primary and Secondary sources are both below
/// <see cref="NoArtMaxChroma"/> chroma, <i>and</i> the image is mostly one flat color, meaning at least
/// <see cref="MinFlatShare"/> of the used pixels lie within <see cref="FlatTolerance"/> (Oklab distance) of the
/// largest k-means cluster's centroid (before merging, so near shades don't pull the flat color off-center). A
/// grayscale photo or gradient is not flat, so it still gives a soft-white palette, and a colorful flat image is not
/// grayscale, so it still gives its color. An image with no pixel at alpha <see cref="MinAlpha"/> or above is also
/// no art.</para>
/// <para>The same input always gives the same output. The instance holds no state (scratch buffers come from
/// <see cref="ArrayPool{T}.Shared"/> per call), so concurrent calls are safe. The pool keeps returned buffers per
/// thread, so the first call on each thread allocates its scratch (about 85 KB at 64 × 64) and later calls on that
/// thread allocate only the small result arrays.</para>
/// </remarks>
internal sealed class PaletteExtractor : IPaletteExtractor
{
    /// <summary>Pixels with lower alpha are ignored.</summary>
    public const byte MinAlpha = 128;

    /// <summary>Above this many pixels the image is sampled on a grid (512 × 512).</summary>
    public const int MaxSamples = 512 * 512;

    /// <summary>k for k-means.</summary>
    public const int ClusterCount = 5;

    /// <summary>The cap on k-means rounds.</summary>
    public const int MaxIterations = 12;

    /// <summary>The fixed k-means++ seed that makes extraction deterministic.</summary>
    public const ulong Seed = 0xC0FFEE;

    /// <summary>The exponent on a cluster's pixel share in its score: bigger areas win, with diminishing returns.</summary>
    public const float PopulationExponent = 0.6f;

    /// <summary>Added to chroma in the score, so neutral clusters still score by size and lightness.</summary>
    public const float ChromaBias = 0.25f;

    /// <summary>k-means clusters whose centroids are closer than this (Oklab distance) are merged before scoring.</summary>
    public const float MergeDistance = 0.05f;

    /// <summary>The smallest Oklab distance from Primary for a group to become Secondary.</summary>
    public const float SecondaryMinDistance = 0.12f;

    /// <summary>The hue rotation, in degrees, of a derived Secondary.</summary>
    public const float DerivedHueShift = 35f;

    /// <summary>The lightness added to a derived Secondary.</summary>
    public const float DerivedLightnessShift = 0.08f;

    /// <summary>At or below this lightness a cluster is near-black and gets only <see cref="FitnessFloor"/>.</summary>
    public const float NearBlackLightness = 0.25f;

    /// <summary>Lightness fitness is 1 from here …</summary>
    public const float FullFitnessLow = 0.40f;

    /// <summary>… to here.</summary>
    public const float FullFitnessHigh = 0.80f;

    /// <summary>At or above this lightness a cluster is near-white and gets only <see cref="FitnessFloor"/>.</summary>
    public const float NearWhiteLightness = 0.92f;

    /// <summary>The fitness of near-black and near-white clusters: low, but above 0 so a black-and-white image still ranks by size.</summary>
    public const float FitnessFloor = 0.05f;

    /// <summary>No art needs both palette sources below this chroma (the same level <see cref="Glow"/> treats as grayscale).</summary>
    public const float NoArtMaxChroma = Glow.NeutralChroma;

    /// <summary>The Oklab distance from the largest cluster's centroid within which a pixel counts as that flat color.</summary>
    public const float FlatTolerance = 0.03f;

    /// <summary>No art needs at least this share of the used pixels to be the one flat color.</summary>
    public const float MinFlatShare = 0.6f;

    /// <inheritdoc />
    public Palette? Extract(ReadOnlySpan<byte> bgra, int width, int height, string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        PaletteAnalysis? analysis = Analyze(bgra, width, height);
        if (analysis is null || analysis.IsNoArt) return null;
        return new Palette(Glow.ToLight(analysis.PrimarySource), Glow.ToLight(analysis.SecondarySource), trackId);
    }

    /// <summary>Runs the extraction and returns every intermediate result (for tests and diagnostics).</summary>
    /// <param name="bgra">Tightly packed BGRA8 pixels.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>The analysis, or null when no pixel is opaque enough.</returns>
    /// <exception cref="ArgumentException">A size is not positive, or the buffer is not exactly width × height × 4 bytes.</exception>
    public static PaletteAnalysis? Analyze(ReadOnlySpan<byte> bgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long pixels = (long)width * height; // can't overflow; × 4 could, so compare in pixels
        if (pixels > int.MaxValue / 4 || bgra.Length != pixels * 4)
            throw new ArgumentException($"Expected {pixels} × 4 bytes for {width}×{height} BGRA8 pixels, got {bgra.Length}.", nameof(bgra));

        int step = SampleStep(width, height);
        int capacity = (width + step - 1) / step * ((height + step - 1) / step);
        Oklab[] points = ArrayPool<Oklab>.Shared.Rent(capacity);
        int[]? assignments = null;
        float[]? scratch = null;
        try
        {
            int count = CollectOpaque(bgra, width, height, step, points);
            if (count == 0) return null;
            assignments = ArrayPool<int>.Shared.Rent(count);
            scratch = ArrayPool<float>.Shared.Rent(count);
            return Analyze(points.AsSpan(0, count), assignments.AsSpan(0, count), scratch.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<Oklab>.Shared.Return(points);
            if (assignments is not null) ArrayPool<int>.Shared.Return(assignments);
            if (scratch is not null) ArrayPool<float>.Shared.Return(scratch);
        }
    }

    /// <summary>Doc 05 §2 step 3's lightness term: <see cref="FitnessFloor"/> up to <see cref="NearBlackLightness"/>,
    /// a smooth rise to 1 at <see cref="FullFitnessLow"/>, 1 through <see cref="FullFitnessHigh"/>, and a smooth fall back
    /// to the floor at <see cref="NearWhiteLightness"/>.</summary>
    /// <param name="lightness">Oklab L.</param>
    /// <returns>The fitness, <see cref="FitnessFloor"/>..1.</returns>
    public static float LightnessFitness(float lightness)
    {
        float rise = SmoothStep(NearBlackLightness, FullFitnessLow, lightness);
        float fall = 1 - SmoothStep(FullFitnessHigh, NearWhiteLightness, lightness);
        return FitnessFloor + (1 - FitnessFloor) * rise * fall;
    }

    /// <summary>Doc 05 §2 step 3: share^0.6 × (0.25 + chroma) × lightnessFitness.</summary>
    /// <param name="share">The cluster's fraction of the used pixels, 0..1.</param>
    /// <param name="centroid">The cluster centroid.</param>
    /// <returns>The score; higher is better.</returns>
    public static float Score(float share, Oklab centroid) =>
        MathF.Pow(share, PopulationExponent) * (ChromaBias + centroid.Chroma) * LightnessFitness(centroid.L);

    /// <summary>Doc 05 §2 step 5's fallback Secondary: Primary turned +35° in OkLCh hue and 0.08 lighter.</summary>
    /// <param name="primary">The Primary source color.</param>
    /// <returns>The derived Secondary source color, before glow-ify.</returns>
    public static Oklab DeriveSecondary(Oklab primary)
    {
        OkLch lch = primary.ToLch();
        float hue = lch.H + DerivedHueShift;
        return new OkLch(lch.L + DerivedLightnessShift, lch.C, hue >= 360f ? hue - 360f : hue).ToOklab();
    }

    /// <summary>The grid step that keeps the number of sampled pixels at or below <see cref="MaxSamples"/>.</summary>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <returns>1 for images up to 512 × 512 pixels; otherwise the smallest step that fits.</returns>
    public static int SampleStep(int width, int height)
    {
        int step = 1;
        while ((long)((width + step - 1) / step) * ((height + step - 1) / step) > MaxSamples) step++;
        return step;
    }

    private static int CollectOpaque(ReadOnlySpan<byte> bgra, int width, int height, int step, Span<Oklab> points)
    {
        int count = 0;
        int previous = -1; // the last converted BGR value: runs of one color (flat artwork) convert once
        Oklab previousColor = default;
        for (int y = 0; y < height; y += step)
        {
            ReadOnlySpan<byte> row = bgra.Slice(y * width * 4, width * 4);
            for (int x = 0; x < width; x += step)
            {
                int o = x * 4;
                if (row[o + 3] < MinAlpha) continue;
                int bgr = row[o] | row[o + 1] << 8 | row[o + 2] << 16;
                if (bgr != previous)
                {
                    previousColor = Oklab.FromLinearSrgb(Srgb.ToLinear(row[o + 2]), Srgb.ToLinear(row[o + 1]), Srgb.ToLinear(row[o]));
                    previous = bgr;
                }
                points[count++] = previousColor;
            }
        }
        return count;
    }

    private static PaletteAnalysis Analyze(ReadOnlySpan<Oklab> points, Span<int> assignments, Span<float> scratch)
    {
        var clusterCenters = new Oklab[ClusterCount];
        var clusterPopulations = new int[ClusterCount];
        int clusters = OklabKMeans.Cluster(points, ClusterCount, MaxIterations, Seed, clusterCenters, clusterPopulations, assignments, scratch);
        Array.Resize(ref clusterCenters, clusters);
        Array.Resize(ref clusterPopulations, clusters);
        var clusterShares = new float[clusters];
        for (int c = 0; c < clusters; c++) clusterShares[c] = clusterPopulations[c] / (float)points.Length;

        // Score the merged groups, not the raw clusters: a textured region split four ways would otherwise lose to a
        // flat minority (doc 05 §4's 70/30 case with any grain on the blue).
        Span<int> groupOf = stackalloc int[clusters];
        int groups = GroupNearClusters(clusterCenters, clusterPopulations, groupOf);
        Span<double> sums = stackalloc double[groups * 3];
        Span<int> populations = stackalloc int[groups];
        sums.Clear();
        populations.Clear();
        for (int c = 0; c < clusters; c++)
        {
            int g = groupOf[c];
            if (g < 0) continue;
            int n = clusterPopulations[c];
            sums[g * 3] += (double)clusterCenters[c].L * n;
            sums[g * 3 + 1] += (double)clusterCenters[c].A * n;
            sums[g * 3 + 2] += (double)clusterCenters[c].B * n;
            populations[g] += n;
        }

        var centers = new Oklab[groups];
        var shares = new float[groups];
        var scores = new float[groups];
        int primary = 0;
        for (int g = 0; g < groups; g++)
        {
            double n = populations[g];
            centers[g] = new Oklab((float)(sums[g * 3] / n), (float)(sums[g * 3 + 1] / n), (float)(sums[g * 3 + 2] / n));
            shares[g] = populations[g] / (float)points.Length;
            scores[g] = Score(shares[g], centers[g]);
            if (scores[g] > scores[primary]) primary = g;
        }

        int secondary = -1;
        for (int g = 0; g < groups; g++)
        {
            if (g == primary) continue;
            if (Oklab.Distance(centers[g], centers[primary]) < SecondaryMinDistance) continue;
            if (secondary < 0 || scores[g] > scores[secondary]) secondary = g;
        }

        Oklab primarySource = centers[primary];
        Oklab secondarySource = secondary >= 0 ? centers[secondary] : DeriveSecondary(primarySource);

        int dominant = 0;
        for (int c = 1; c < clusters; c++) if (clusterPopulations[c] > clusterPopulations[dominant]) dominant = c;
        float toleranceSquared = FlatTolerance * FlatTolerance;
        int flat = 0;
        foreach (Oklab point in points) if (Oklab.DistanceSquared(point, clusterCenters[dominant]) <= toleranceSquared) flat++;
        float flatShare = flat / (float)points.Length;

        bool grayscale = primarySource.Chroma < NoArtMaxChroma && secondarySource.Chroma < NoArtMaxChroma;
        return new PaletteAnalysis(centers, shares, scores, primary, secondary, primarySource, secondarySource,
            flatShare, grayscale && flatShare >= MinFlatShare, points.Length, clusterCenters, clusterShares);
    }

    /// <summary>Single-linkage grouping: clusters closer than <see cref="MergeDistance"/>, directly or through a chain
    /// of such clusters, share a group. Empty clusters join none.</summary>
    /// <param name="centers">The k-means centroids.</param>
    /// <param name="populations">The k-means populations.</param>
    /// <param name="groupOf">Receives each cluster's group, numbered in order of each group's first cluster, or −1 for
    /// an empty cluster.</param>
    /// <returns>The number of groups, at least 1 (some cluster always holds a pixel).</returns>
    private static int GroupNearClusters(ReadOnlySpan<Oklab> centers, ReadOnlySpan<int> populations, Span<int> groupOf)
    {
        int clusters = centers.Length;
        for (int c = 0; c < clusters; c++) groupOf[c] = populations[c] > 0 ? c : -1;
        // Each label is its group's lowest cluster index. Relabeling the whole group on every merge makes one pass
        // over the pairs enough: a merge never splits a group, so every close pair ends up sharing a label.
        for (int i = 0; i < clusters; i++)
        {
            for (int j = i + 1; j < clusters; j++)
            {
                int a = groupOf[i], b = groupOf[j];
                if (a < 0 || b < 0 || a == b || Oklab.Distance(centers[i], centers[j]) >= MergeDistance) continue;
                int keep = Math.Min(a, b), drop = Math.Max(a, b);
                for (int c = 0; c < clusters; c++) if (groupOf[c] == drop) groupOf[c] = keep;
            }
        }

        Span<int> number = stackalloc int[clusters];
        number.Fill(-1);
        int groups = 0;
        for (int c = 0; c < clusters; c++)
        {
            if (groupOf[c] < 0) continue;
            ref int n = ref number[groupOf[c]];
            if (n < 0) n = groups++;
            groupOf[c] = n;
        }
        return groups;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>Everything <see cref="PaletteExtractor.Analyze(ReadOnlySpan{byte}, int, int)"/> decided, before glow-ify.</summary>
/// <param name="Centers">The scored groups' centroids in Oklab: k-means clusters with near ones merged
/// (<see cref="PaletteExtractor.MergeDistance"/>). Every group holds at least one pixel.</param>
/// <param name="Shares">Each group's fraction of the used pixels.</param>
/// <param name="Scores">Each group's score.</param>
/// <param name="PrimaryIndex">The Primary group.</param>
/// <param name="SecondaryIndex">The Secondary group, or −1 when Secondary was derived from Primary.</param>
/// <param name="PrimarySource">The Primary color before glow-ify.</param>
/// <param name="SecondarySource">The Secondary color before glow-ify (a group centroid or the derived color).</param>
/// <param name="FlatShare">The share of used pixels within the flat tolerance of the largest k-means cluster's centroid.</param>
/// <param name="IsNoArt">Whether the image counts as no art (grayscale palette and mostly one flat color).</param>
/// <param name="SampleCount">The number of pixels used (opaque enough, on the sampling grid).</param>
/// <param name="ClusterCenters">The raw k-means centroids, before merging.</param>
/// <param name="ClusterShares">Each raw k-means cluster's fraction of the used pixels (0 for an empty cluster).</param>
internal sealed record PaletteAnalysis(
    Oklab[] Centers, float[] Shares, float[] Scores, int PrimaryIndex, int SecondaryIndex,
    Oklab PrimarySource, Oklab SecondarySource, float FlatShare, bool IsNoArt, int SampleCount,
    Oklab[] ClusterCenters, float[] ClusterShares);
