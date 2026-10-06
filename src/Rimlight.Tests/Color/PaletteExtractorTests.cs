using System.Diagnostics;
using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// C4: the real IPaletteExtractor (doc 05 §2, §4, and the §1 "no art" quirk). Every image is generated here.
public sealed class PaletteExtractorTests(ITestOutputHelper output)
{
    private static readonly Rgba8 Red = new(255, 0, 0);
    private static readonly Rgba8 Blue = new(30, 80, 220);
    private static readonly Rgba8 Orange = new(255, 140, 0);
    private static readonly Rgba8 MidGrey = new(128, 128, 128);
    private static readonly Rgba8 White = new(255, 255, 255);

    private readonly IPaletteExtractor extractor = CoreFactory.CreatePaletteExtractor();

    // ---- doc 05 §4 ----

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void SolidRedGivesRedPrimaryAndADerivedNeighborSecondary(int grain)
    {
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            int n = (int)((Noise.Hash(x, y, 1) - 0.5f) * grain);
            return new Rgba8((byte)(255 - Math.Abs(n)), (byte)Math.Max(0, n), (byte)Math.Max(0, -n));
        });
        var analysis = Analyze(image);
        Assert.Equal(-1, analysis.SecondaryIndex); // every cluster is red: Secondary is derived

        var palette = Extract(image);
        var primary = Lch(palette.Primary);
        var secondary = Lch(palette.Secondary);
        float redHue = Red.ToOklab().ToLch().H;
        output.WriteLine($"grain {grain}: primary {primary}, secondary {secondary}");
        Assert.True(OklabTests.HueDistance(primary.H, redHue) < 10, $"primary hue {primary.H}");
        Assert.True(secondary.C >= 0.1f, $"secondary is grey: {secondary}");
        Assert.True(OklabTests.HueDistance(secondary.H, analysis.PrimarySource.ToLch().H + 35) < 2, $"secondary hue {secondary.H}");
        Assert.True(secondary.L > primary.L, "the derived secondary is lighter");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 20)]
    [InlineData(true, 0)]
    [InlineData(true, 20)]
    public void SeventyPercentBlueThirtyPercentOrange(bool interleaved, int grain)
    {
        // As two regions, or finely interleaved (every pixel's neighbors differ), with or without grain.
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            int i = y * 64 + x;
            bool blue = interleaved ? (i * 7 % 10) < 7 : i < 0.7f * 4096;
            Rgba8 color = blue ? Blue : Orange;
            int n = (int)((Noise.Hash(x, y, 2) - 0.5f) * grain);
            return new Rgba8(Clamp(color.R + n), Clamp(color.G + n), Clamp(color.B + n));
        });
        var palette = Extract(image);
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, Blue.ToOklab().ToLch().H) < 10, $"primary {Lch(palette.Primary)}");
        Assert.True(OklabTests.HueDistance(Lch(palette.Secondary).H, Orange.ToOklab().ToLch().H) < 10, $"secondary {Lch(palette.Secondary)}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void BlackAndWhitePhotoGlowsSoftWhiteWithNoInventedHue(int channelNoise)
    {
        // A grayscale "photo" (R = G = B), optionally with independent per-channel noise like a re-encoded JPEG.
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            float v = 30 + 200 * Noise.Fractal(x, y, 20, 3);
            int Channel(int seed) => Clamp((int)(v + (Noise.Hash(x, y, seed) - 0.5f) * channelNoise));
            return new Rgba8((byte)Channel(4), (byte)Channel(5), (byte)Channel(6));
        });
        var palette = Extract(image); // not null: a photo is not one flat color
        foreach (var color in new[] { palette.Primary, palette.Secondary })
        {
            var lch = Lch(color);
            output.WriteLine(lch.ToString());
            Assert.True(lch.C < Glow.NeutralChroma, $"invented hue: {lch}");
            Assert.InRange(lch.L, Glow.MinLightness - 1e-4f, Glow.MaxLightness + 1e-4f);
        }
    }

    [Fact]
    public void SameInputGivesTheSameOutputAcrossCallsInstancesAndThreads()
    {
        var images = SampleArt.Names.Select(SampleArt.Create).Append(NoisyRainbow(64, 64)).ToArray();
        var expected = images.Select(i => new PaletteExtractor().Extract(i.Bgra, i.Width, i.Height, "t")).ToArray();
        for (int i = 0; i < images.Length; i++)
            Assert.Equal(expected[i], extractor.Extract(images[i].Bgra, images[i].Width, images[i].Height, "t"));

        // One shared instance, many threads at once: no shared mutable state.
        var results = new Palette?[64];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var image = images[i % images.Length];
            results[i] = extractor.Extract(image.Bgra, image.Width, image.Height, "t");
        });
        for (int i = 0; i < results.Length; i++) Assert.Equal(expected[i % images.Length], results[i]);
    }

    [Theory]
    [InlineData(255, 40, 160)]  // pink
    [InlineData(0, 220, 255)]   // cyan
    [InlineData(255, 200, 0)]   // gold
    [InlineData(80, 255, 60)]   // green
    public void NearBlackCoverWithASmallBrightLogoPicksTheLogo(byte r, byte g, byte b)
    {
        // About 2.5 % logo (an anti-aliased disc) on a near-black, slightly blue, noisy background, plus a line of
        // white text: the dark majority and the white must both lose to the logo.
        var logo = new Rgba8(r, g, b);
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            float dark = 4 + 18 * Noise.Fractal(x, y, 12, 7);
            float cover = Math.Clamp(5.5f - MathF.Sqrt((x - 40) * (x - 40) + (y - 20) * (y - 20)), 0f, 1f);
            if (y == 50 && x is >= 10 and < 50) return White;
            return new Rgba8(Clamp((int)(dark * 0.8f + (logo.R - dark * 0.8f) * cover)),
                Clamp((int)(dark * 0.9f + (logo.G - dark * 0.9f) * cover)), Clamp((int)(dark * 1.3f + (logo.B - dark * 1.3f) * cover)));
        });
        var palette = Extract(image);
        float logoHue = logo.ToOklab().ToLch().H;
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, logoHue) < 10, $"primary {Lch(palette.Primary)}, logo hue {logoHue}");
    }

    // ---- edge cases ----

    [Fact]
    public void OnePixelImageWorks()
    {
        var image = ArtImage.Solid(1, 1, Blue);
        var palette = Extract(image);
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, Blue.ToOklab().ToLch().H) < 1);
        Assert.Equal(-1, Analyze(image).SecondaryIndex);
        Assert.Null(extractor.Extract(ArtImage.Solid(1, 1, Blue.WithAlpha(0)).Bgra, 1, 1, "t"));
    }

    [Fact]
    public void FullyTransparentImageIsNoArt()
    {
        var image = ArtImage.Create(64, 64, (x, y) => new Rgba8((byte)(x * 4), (byte)(y * 4), 200, 0));
        Assert.Null(extractor.Extract(image.Bgra, 64, 64, "t"));
    }

    [Fact]
    public void AlphaBelow128IsSkippedAnd128IsUsed()
    {
        Assert.Null(extractor.Extract(ArtImage.Solid(8, 8, Red.WithAlpha(127)).Bgra, 8, 8, "t"));
        Assert.NotNull(extractor.Extract(ArtImage.Solid(8, 8, Red.WithAlpha(128)).Bgra, 8, 8, "t"));
    }

    [Fact]
    public void PartiallyTransparentPixelsAreIgnored()
    {
        // 60 % mostly-transparent red around 40 % opaque-enough blue: only the blue counts.
        var image = ArtImage.Create(32, 32, (x, y) => y < 13 || y >= 26 ? Red.WithAlpha((byte)(x * 4)) : Blue.WithAlpha((byte)(128 + x * 4)));
        var analysis = Analyze(image);
        Assert.Equal(13 * 32, analysis.SampleCount);
        Assert.Single(analysis.Centers);
        var palette = Extract(image);
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, Blue.ToOklab().ToLch().H) < 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GrayscaleGradientIsNeutralButNotNoArt(bool horizontal)
    {
        var image = ArtImage.Create(64, 64, (x, y) => { byte v = (byte)((horizontal ? x : y) * 4); return new Rgba8(v, v, v); });
        var analysis = Analyze(image);
        output.WriteLine($"flat share {analysis.FlatShare:F3}");
        Assert.False(analysis.IsNoArt);
        var palette = Extract(image);
        Assert.True(Lch(palette.Primary).C < Glow.NeutralChroma);
        Assert.True(Lch(palette.Secondary).C < Glow.NeutralChroma);
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 0, 255)]
    [InlineData(255, 0, 255, 0, 255, 255)]
    [InlineData(0, 255, 0, 255, 255, 0)]
    [InlineData(0, 0, 255, 255, 255, 0)]
    [InlineData(255, 255, 255, 0, 0, 0)]
    public void SaturatedColorsAreGamutMappedIntoZeroToOne(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        foreach (var image in new[] { ArtImage.Bands(64, 64, (new Rgba8(r1, g1, b1), 0.5f), (new Rgba8(r2, g2, b2), 0.5f)), NoisyRainbow(64, 64) })
        {
            var palette = Extract(image);
            foreach (var color in new[] { palette.Primary, palette.Secondary })
            {
                Assert.True(Glow.InGamut(color), $"{color} is outside 0..1");
                Assert.InRange(Lch(color).L, Glow.MinLightness - 1e-4f, Glow.MaxLightness + 1e-4f);
            }
        }
    }

    [Fact]
    public void SizesAreValidated()
    {
        var pixels = new byte[64 * 64 * 4];
        Assert.Throws<ArgumentException>(() => extractor.Extract(pixels.AsSpan(1), 64, 64, "t"));
        Assert.Throws<ArgumentException>(() => extractor.Extract(new byte[64 * 64 * 4 + 4], 64, 64, "t"));
        Assert.Throws<ArgumentException>(() => extractor.Extract(pixels, 64, 63, "t"));
        Assert.ThrowsAny<ArgumentException>(() => extractor.Extract(pixels, 0, 64, "t"));
        Assert.ThrowsAny<ArgumentException>(() => extractor.Extract(pixels, 64, -1, "t"));
        Assert.ThrowsAny<ArgumentException>(() => extractor.Extract([], 0, 0, "t"));
        Assert.Throws<ArgumentException>(() => extractor.Extract(pixels, int.MaxValue, int.MaxValue, "t")); // no overflow
        Assert.Throws<ArgumentNullException>(() => extractor.Extract(pixels, 64, 64, null!));
    }

    [Fact]
    public void TrackIdIsCarriedThrough()
    {
        var image = ArtImage.Solid(4, 4, Orange);
        Assert.Equal("app|artist|title", extractor.Extract(image.Bgra, 4, 4, "app|artist|title")!.SourceTrackId);
    }

    [Fact]
    public void ImagesUpTo512AreUsedWholeAndLargerOnesAreSampledOnAGrid()
    {
        Assert.Equal(1, PaletteExtractor.SampleStep(64, 64));
        Assert.Equal(1, PaletteExtractor.SampleStep(512, 512));
        Assert.Equal(1, PaletteExtractor.SampleStep(1024, 256));
        Assert.Equal(2, PaletteExtractor.SampleStep(513, 512));
        Assert.Equal(8, PaletteExtractor.SampleStep(4096, 4096));
        Assert.Equal(4, PaletteExtractor.SampleStep(1, 1_000_000));

        var full = NoisyRainbow(512, 512);
        Assert.Equal(512 * 512, Analyze(full).SampleCount);
        // A 2× nearest-neighbor upscale, sampled every 2nd pixel, sees exactly the original pixels.
        var upscaled = full.Upscale(2);
        Assert.Equal(Extract(full), Extract(upscaled));
    }

    [Fact]
    public void ExtractionCostIsModest()
    {
        foreach (int size in new[] { 64, 512 })
        {
            var image = NoisyRainbow(size, size);
            for (int i = 0; i < 3; i++) Extract(image); // warm up the JIT and the array pool
            int runs = size == 64 ? 50 : 5;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) extractor.Extract(image.Bgra, size, size, "t");
            watch.Stop();
            long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
            output.WriteLine($"{size}×{size}: {watch.Elapsed.TotalMilliseconds / runs:F2} ms, {bytes} B allocated per call (informational timing)");
            Assert.True(bytes < 1024, $"{bytes} B per call at {size}×{size}"); // scratch comes from the array pool
        }
    }

    [Fact]
    public void KMeansRunsToConvergenceOnSmoothImages()
    {
        // A uniform grey ramp and a hue wheel have no natural clusters; Lloyd's iterations (up to 12) settle them
        // into five near-equal parts (one round from the k-means++ seeds leaves 13 % to 26 %).
        foreach (var image in new[]
        {
            ArtImage.Create(64, 64, (x, y) => Rgba8.FromOklab(new Oklab(0.2f + 0.6f * (y * 64 + x) / 4095f, 0, 0))),
            ArtImage.Create(64, 64, (x, y) => Rgba8.FromOklab(new OkLch(0.65f, 0.15f, 360f * (y * 64 + x) / 4096f).ToOklab())),
        })
        {
            var analysis = Analyze(image);
            output.WriteLine(string.Join(", ", analysis.Shares.Select(s => s.ToString("F3"))));
            Assert.Equal(5, analysis.Centers.Length);
            Assert.All(analysis.Shares, share => Assert.InRange(share, 0.17f, 0.23f));
        }
    }

    // ---- score (doc 05 §2 step 3) ----

    [Fact]
    public void LightnessFitnessIsAFlatTopBumpAboveAFloor()
    {
        for (float l = 0.40f; l <= 0.80f; l += 0.01f) Assert.Equal(1f, PaletteExtractor.LightnessFitness(l), 1e-6f);
        foreach (float l in new[] { 0f, 0.1f, 0.25f, 0.92f, 0.97f, 1f })
            Assert.Equal(PaletteExtractor.FitnessFloor, PaletteExtractor.LightnessFitness(l), 1e-6f);
        for (float l = 0.25f; l < 0.40f; l += 0.01f) Assert.True(PaletteExtractor.LightnessFitness(l + 0.01f) >= PaletteExtractor.LightnessFitness(l));
        for (float l = 0.80f; l < 0.92f; l += 0.01f) Assert.True(PaletteExtractor.LightnessFitness(l + 0.01f) <= PaletteExtractor.LightnessFitness(l));
        Assert.InRange(PaletteExtractor.LightnessFitness(0.325f), 0.4f, 0.6f);
    }

    [Fact]
    public void ScoreIsShareToThePoint6TimesChromaPlusQuarterTimesFitness()
    {
        var grey = new Oklab(0.6f, 0, 0);
        Assert.Equal(0.25f, PaletteExtractor.Score(1, grey), 1e-6f);
        Assert.Equal(MathF.Pow(0.3f, 0.6f) * 0.25f, PaletteExtractor.Score(0.3f, grey), 1e-6f);
        var vivid = new OkLch(0.6f, 0.2f, 120).ToOklab();
        Assert.Equal(MathF.Pow(0.3f, 0.6f) * 0.45f, PaletteExtractor.Score(0.3f, vivid), 1e-6f);
        var dark = new OkLch(0.2f, 0.2f, 120).ToOklab();
        Assert.Equal(MathF.Pow(0.3f, 0.6f) * 0.45f * PaletteExtractor.FitnessFloor, PaletteExtractor.Score(0.3f, dark), 1e-6f);
    }

    [Fact]
    public void PopulationIsWeightedWithDiminishingReturns()
    {
        // 75 % grey against 25 % pure blue: share^0.6 lets the vivid quarter win (0.245 to 0.210); with plain share
        // the grey would (0.141 to 0.188).
        var palette = Extract(ArtImage.Bands(64, 64, (MidGrey, 0.75f), (new Rgba8(0, 0, 255), 0.25f)));
        Assert.True(Lch(palette.Primary).C > 0.1f, $"primary {Lch(palette.Primary)}");
        Assert.True(Lch(palette.Secondary).C < Glow.NeutralChroma, $"secondary {Lch(palette.Secondary)}");
    }

    [Fact]
    public void ChromaOutweighsASlightlyLargerGreyArea()
    {
        // 55 % grey against 45 % red: (0.25 + chroma) lets red win (0.315 to 0.175); without it grey would.
        var palette = Extract(ArtImage.Bands(64, 64, (MidGrey, 0.55f), (Red, 0.45f)));
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, Red.ToOklab().ToLch().H) < 5, $"primary {Lch(palette.Primary)}");
    }

    [Fact]
    public void NearWhiteBackgroundLosesToASmallColor()
    {
        // 85 % paper white around a 15 % green motif: white sits at the fitness floor.
        var green = new Rgba8(40, 170, 90);
        var palette = Extract(ArtImage.Bands(64, 64, (new Rgba8(250, 250, 248), 0.85f), (green, 0.15f)));
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, green.ToOklab().ToLch().H) < 5, $"primary {Lch(palette.Primary)}");
    }

    // ---- Secondary: the 0.12 distance rule (doc 05 §2 step 5) ----

    [Theory]
    [InlineData(0.10f, false)]
    [InlineData(0.115f, false)]
    [InlineData(0.125f, true)]
    [InlineData(0.14f, true)]
    public void SecondaryMustBeAtLeast012FromPrimary(float distance, bool nearIsSecondary)
    {
        // 50 % purple (Primary), 30 % the same purple `distance` lighter, 20 % orange. The lighter purple outscores
        // the orange (0.194 to 0.163), so it is Secondary exactly when it is at least 0.12 away from Primary;
        // otherwise orange is.
        var purple = Rgba8.FromOklab(new OkLch(0.55f, 0.15f, 300f).ToOklab());
        var primary = purple.ToOklab();
        var near = Rgba8.FromOklab(primary with { L = primary.L + distance });
        float measured = Oklab.Distance(near.ToOklab(), primary);
        Assert.True(MathF.Abs(measured - distance) < 0.003f, $"quantized distance {measured}");

        var analysis = Analyze(ArtImage.Bands(64, 64, (purple, 0.5f), (near, 0.3f), (Orange, 0.2f)));
        Assert.Equal(primary, analysis.PrimarySource);
        var expected = nearIsSecondary ? near : Orange;
        Assert.True(Oklab.Distance(analysis.SecondarySource, expected.ToOklab()) < 1e-4f,
            $"secondary {analysis.SecondarySource}, expected {expected} at distance {measured}");
    }

    [Fact]
    public void SecondaryIsDerivedWhenEveryClusterIsCloseToPrimary()
    {
        var shades = ArtImage.Create(64, 64, (x, y) => Rgba8.FromOklab(Blue.ToOklab() with { L = 0.46f + 0.07f * x / 63f }));
        var analysis = Analyze(shades);
        Assert.Equal(-1, analysis.SecondaryIndex);
        var derived = analysis.SecondarySource.ToLch();
        var source = analysis.PrimarySource.ToLch();
        Assert.Equal(source.L + 0.08f, derived.L, 1e-5f);
        Assert.Equal(source.C, derived.C, 1e-5f);
        Assert.True(OklabTests.HueDistance(derived.H, source.H + 35) < 1e-3f);
    }

    // ---- glow-ify through the extractor ----

    [Fact]
    public void DarkArtworkIsLiftedTo055()
    {
        var palette = Extract(ArtImage.Solid(16, 16, new Rgba8(0, 0, 128))); // navy, L 0.27
        Assert.Equal(Glow.MinLightness, Lch(palette.Primary).L, 1e-3f);
        Assert.Equal(Glow.MinLightness, Lch(palette.Secondary).L, 1e-3f); // derived 0.35, also lifted
    }

    [Fact]
    public void PaleArtworkIsDimmedTo085()
    {
        var palette = Extract(ArtImage.Solid(16, 16, new Rgba8(255, 250, 205))); // pale yellow, L 0.98
        Assert.Equal(Glow.MaxLightness, Lch(palette.Primary).L, 1e-3f);
    }

    [Theory]
    [InlineData(0.05f, 0.12f)]
    [InlineData(0.022f, 0.022f)]
    public void DullArtworkIsSaturatedUnlessGrayscale(float chroma, float expected)
    {
        // A dusty rose shaded from L 0.5 to 0.8 (shaded, so the faint one isn't a flat "no art" image).
        var dusty = ArtImage.Create(64, 64, (x, _) => Rgba8.FromOklab(new OkLch(0.5f + 0.3f * x / 63f, chroma, 20f).ToOklab()));
        var palette = Extract(dusty);
        Assert.Equal(expected, Lch(palette.Primary).C, 4e-3f);
    }

    // ---- "no art" (doc 05 §1): grayscale palette AND mostly one flat color ----

    [Fact]
    public void GenericGreyAppIconIsNoArt()
    {
        var icon = ArtImage.Create(64, 64, (x, y) => x is >= 24 and < 40 && y is >= 16 and < 48 ? White : new Rgba8(70, 70, 72));
        Assert.Null(extractor.Extract(icon.Bgra, 64, 64, "t"));
    }

    [Fact]
    public void ColorfulFlatImageIsNotNoArt()
    {
        var icon = ArtImage.Create(64, 64, (x, y) => x is >= 24 and < 40 && y is >= 16 and < 48 ? White : new Rgba8(200, 30, 60));
        Assert.NotNull(extractor.Extract(icon.Bgra, 64, 64, "t"));
    }

    [Theory]
    [InlineData(0.022f, true)]
    [InlineData(0.038f, false)]
    public void NoArtNeedsANearlyGrayscalePalette(float tint, bool noArt)
    {
        // A flat 80 % background with a faint blue tint, and a white glyph.
        var background = Rgba8.FromOklab(new OkLch(0.6f, tint, 250f).ToOklab());
        var analysis = Analyze(ArtImage.Bands(64, 64, (background, 0.8f), (White, 0.2f)));
        Assert.Equal(tint < PaletteExtractor.NoArtMaxChroma, analysis.PrimarySource.Chroma < PaletteExtractor.NoArtMaxChroma);
        Assert.True(analysis.FlatShare >= 0.8f - 1e-3f);
        Assert.Equal(noArt, analysis.IsNoArt);
    }

    [Theory]
    [InlineData(0.55f, false)]
    [InlineData(0.65f, true)]
    public void NoArtNeedsAtLeast60PercentOneFlatColor(float flatShare, bool noArt)
    {
        // A flat light-grey area, and a dark grayscale gradient (L 0.1..0.4, far from the flat color) for the rest.
        var flat = new Rgba8(205, 205, 205);
        int flatPixels = (int)MathF.Round(flatShare * 4096);
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            int i = y * 64 + x;
            if (i < flatPixels) return flat;
            var shade = Rgba8.FromOklab(new Oklab(0.1f + 0.3f * (i - flatPixels) / (4096f - flatPixels), 0, 0));
            return shade;
        });
        var analysis = Analyze(image);
        Assert.Equal(flatPixels / 4096f, analysis.FlatShare, 1e-6f);
        Assert.Equal(noArt, analysis.IsNoArt);
        Assert.Equal(noArt, extractor.Extract(image.Bgra, 64, 64, "t") is null);
    }

    [Theory]
    [InlineData(0.025f, true)]
    [InlineData(0.035f, false)]
    public void FlatMeansWithin003OfTheLargestCluster(float spread, bool noArt)
    {
        // The "flat" background is two greys `spread` apart (36 % and 34 %) around a 30 % white glyph. Within the
        // 0.03 tolerance they count as one flat color (70 %); beyond it, only the larger one does (36 %).
        var lighter = Rgba8.FromOklab(new Oklab(0.4f + spread, 0, 0));
        var darker = Rgba8.FromOklab(new Oklab(0.4f, 0, 0));
        float measured = Oklab.Distance(lighter.ToOklab(), darker.ToOklab());
        Assert.True(MathF.Abs(measured - spread) < 0.003f, $"quantized spread {measured}");

        var analysis = Analyze(ArtImage.Bands(64, 64, (darker, 0.36f), (lighter, 0.34f), (White, 0.30f)));
        output.WriteLine($"spread {measured:F4}: flat share {analysis.FlatShare:F3}");
        Assert.Equal(noArt, analysis.IsNoArt);
    }

    [Fact]
    public void GreyCoverWithAColoredMotifIsNotNoArt()
    {
        // 85 % flat grey stays Primary (0.227 to 0.112), but the light teal motif (0.18 away, so it is Secondary)
        // makes the palette colorful.
        var teal = Rgba8.FromOklab(new OkLch(0.75f, 0.1f, 190f).ToOklab());
        var analysis = Analyze(ArtImage.Bands(64, 64, (MidGrey, 0.85f), (teal, 0.15f)));
        Assert.True(analysis.PrimarySource.Chroma < PaletteExtractor.NoArtMaxChroma);
        Assert.True(analysis.FlatShare >= PaletteExtractor.MinFlatShare);
        Assert.False(analysis.IsNoArt);
        Assert.True(OklabTests.HueDistance(Glow.Target(analysis.SecondarySource).H, 190f) < 2);
    }

    [Fact]
    public void NoArtThresholdsAreTheDocumentedOnes()
    {
        Assert.Equal(128, PaletteExtractor.MinAlpha);
        Assert.Equal(5, PaletteExtractor.ClusterCount);
        Assert.Equal(12, PaletteExtractor.MaxIterations);
        Assert.Equal(0.12f, PaletteExtractor.SecondaryMinDistance);
        Assert.Equal(0.03f, PaletteExtractor.NoArtMaxChroma);
        Assert.Equal(0.03f, PaletteExtractor.FlatTolerance);
        Assert.Equal(0.6f, PaletteExtractor.MinFlatShare);
    }

    // ---- helpers ----

    private Palette Extract(ArtImage image)
    {
        var palette = extractor.Extract(image.Bgra, image.Width, image.Height, "track");
        Assert.NotNull(palette);
        Assert.Equal("track", palette.SourceTrackId);
        Assert.True(Glow.InGamut(palette.Primary) && Glow.InGamut(palette.Secondary), $"{palette} outside 0..1");
        return palette;
    }

    private static PaletteAnalysis Analyze(ArtImage image)
    {
        var analysis = PaletteExtractor.Analyze(image.Bgra, image.Width, image.Height);
        Assert.NotNull(analysis);
        return analysis;
    }

    private static OkLch Lch(Rgb color) => Oklab.FromLinearSrgb(color).ToLch();

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    // Every hue at full saturation with grain: the widest gamut workout, and plenty of distinct colors.
    internal static ArtImage NoisyRainbow(int width, int height) => ArtImage.Create(width, height, (x, y) =>
    {
        float hue = 360f * x / width, value = 0.5f + 0.5f * y / height;
        float Channel(float offset) => value * Math.Clamp(MathF.Abs((hue / 60f + offset) % 6f - 3f) - 1f, 0f, 1f);
        int n = (int)((Noise.Hash(x, y, 9) - 0.5f) * 16);
        return new Rgba8(Clamp((int)(255 * Channel(0)) + n), Clamp((int)(255 * Channel(4)) + n), Clamp((int)(255 * Channel(2)) + n));
    });
}
