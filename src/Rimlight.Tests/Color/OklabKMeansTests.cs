using Rimlight.Core.Color;
using Xunit;

namespace Rimlight.Tests.Color;

// C4: deterministic k-means++ / Lloyd in Oklab (doc 05 §2 step 2).
public sealed class OklabKMeansTests
{
    private static readonly Oklab[] BlobCenters =
    [
        new(0.30f, 0.10f, 0.05f), new(0.50f, -0.10f, 0.10f), new(0.70f, 0.00f, -0.15f),
        new(0.85f, 0.05f, 0.15f), new(0.60f, 0.20f, -0.05f),
    ];

    [Fact]
    public void FindsFiveSeparatedBlobs()
    {
        // 5 blobs of different sizes, each a ±0.01 cube of points: k-means++ must seed one center per blob.
        var points = new List<Oklab>();
        for (int blob = 0; blob < BlobCenters.Length; blob++)
            for (int i = 0; i < 100 * (blob + 1); i++)
                points.Add(new Oklab(
                    BlobCenters[blob].L + (Noise.Hash(i, blob, 1) - 0.5f) * 0.02f,
                    BlobCenters[blob].A + (Noise.Hash(i, blob, 2) - 0.5f) * 0.02f,
                    BlobCenters[blob].B + (Noise.Hash(i, blob, 3) - 0.5f) * 0.02f));

        var (count, centers, populations, _) = Run(points.ToArray(), 5);
        Assert.Equal(5, count);
        for (int blob = 0; blob < BlobCenters.Length; blob++)
        {
            int c = OklabKMeans.Nearest(BlobCenters[blob], centers);
            Assert.True(Oklab.Distance(centers[c], BlobCenters[blob]) < 0.005f, $"blob {blob}");
            Assert.Equal(100 * (blob + 1), populations[c]);
        }
    }

    [Fact]
    public void FewerDistinctColorsThanKGiveFewerClusters()
    {
        var red = new Oklab(0.63f, 0.22f, 0.13f);
        var (count, centers, populations, assignments) = Run(Enumerable.Repeat(red, 50).ToArray(), 5);
        Assert.Equal(1, count);
        Assert.Equal(red, centers[0]);
        Assert.Equal(50, populations[0]);
        Assert.All(assignments, a => Assert.Equal(0, a));

        var blue = new Oklab(0.45f, -0.03f, -0.31f);
        var two = Enumerable.Repeat(red, 30).Concat(Enumerable.Repeat(blue, 20)).ToArray();
        (count, centers, populations, _) = Run(two, 5);
        Assert.Equal(2, count);
        Assert.Equal(30, populations[OklabKMeans.Nearest(red, centers)]);
        Assert.Equal(20, populations[OklabKMeans.Nearest(blue, centers)]);
    }

    [Fact]
    public void SameInputAndSeedGiveTheSameClusters()
    {
        var points = new Oklab[4096];
        for (int i = 0; i < points.Length; i++)
            points[i] = new Oklab(Noise.Hash(i, 0, 5), Noise.Hash(i, 0, 6) * 0.4f - 0.2f, Noise.Hash(i, 0, 7) * 0.4f - 0.2f);
        var first = Run(points, 5);
        var second = Run(points, 5);
        Assert.Equal(first.Centers, second.Centers);
        Assert.Equal(first.Populations, second.Populations);
        Assert.Equal(first.Assignments, second.Assignments);
    }

    [Fact]
    public void AssignmentsMatchPopulationsAndNearestCenters()
    {
        var points = new Oklab[1000];
        for (int i = 0; i < points.Length; i++)
            points[i] = new Oklab(Noise.Hash(i, 1, 5), Noise.Hash(i, 1, 6) * 0.4f - 0.2f, Noise.Hash(i, 1, 7) * 0.4f - 0.2f);
        var (count, centers, populations, assignments) = Run(points, 5, maxIterations: 100);
        Assert.Equal(5, count);
        for (int c = 0; c < count; c++) Assert.Equal(populations[c], assignments.Count(a => a == c));
        // Converged (well under 100 rounds): every point sits with its nearest center, which is its cluster mean.
        for (int i = 0; i < points.Length; i++) Assert.Equal(OklabKMeans.Nearest(points[i], centers), assignments[i]);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        var points = new Oklab[10];
        Assert.Throws<ArgumentException>(() => OklabKMeans.Cluster([], 5, 12, 1, new Oklab[5], new int[5], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => OklabKMeans.Cluster(points, 0, 12, 1, new Oklab[5], new int[5], new int[10], new float[10]));
        Assert.Throws<ArgumentOutOfRangeException>(() => OklabKMeans.Cluster(points, 65, 12, 1, new Oklab[65], new int[65], new int[10], new float[10]));
        Assert.Throws<ArgumentException>(() => OklabKMeans.Cluster(points, 5, 12, 1, new Oklab[4], new int[5], new int[10], new float[10]));
        Assert.Throws<ArgumentException>(() => OklabKMeans.Cluster(points, 5, 12, 1, new Oklab[5], new int[5], new int[9], new float[10]));
    }

    private static (int Count, Oklab[] Centers, int[] Populations, int[] Assignments) Run(Oklab[] points, int k, int maxIterations = 12)
    {
        var centers = new Oklab[k];
        var populations = new int[k];
        var assignments = new int[points.Length];
        int count = OklabKMeans.Cluster(points, k, maxIterations, 0xC0FFEE, centers, populations, assignments, new float[points.Length]);
        return (count, centers[..count], populations[..count], assignments);
    }
}
