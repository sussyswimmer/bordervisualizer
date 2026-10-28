using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Rimlight.Core;
using Rimlight.Native.Overlay;
using Rimlight.Platform.Overlay;

namespace Rimlight.Native.Tests;

// The golden table the Mac's glow math is checked against (10-MACOS §1.4): for a set of light states (the Flow
// movement's included), screens (with their notch, in points, as the Swift app sends it in RlSurface) and edge styles,
// the constants the loop gives (MacOverlayLoop.ConstantsFor, which rl_glow_constants calls; all 40 floats) and the
// alpha GlowProfile.Alpha gives at sample points, as Glow.hlsl computes it. The Swift tests evaluate glow_math.h
// (which Glow.metal includes) on the same inputs and must match. If GlowProfile or GlowConstants changes, this test fails until the table is regenerated with
// RIMLIGHT_UPDATE_GOLDEN=1, and the Swift tests then fail until the Metal side follows: the profile can't change on one
// side alone.
public sealed class GlowGoldenTests
{
    private const double Tolerance = 1e-6;

    // A notched 14-inch MacBook Pro: the notch spans 659..853 points across, 32 points down.
    private static readonly Notch Notch14 = new(659, 853, 32);

    private static readonly (string Name, int W, int H, float Scale, float Corner, LightState State, EdgeStyle Style,
        Notch Notch)[] Cases =
    [
        ("defaults-1080p", 1920, 1080, 1, 0, State(spread: 0.45f, thickness: 6, intensity: 0.8f),
            EdgeStyle.Glow, default),
        // The notched 14-inch MacBook Pro's screen size drawn without its notch (the case predates the notch); the
        // notch itself is in glow-retina-notch-14in and line-retina-notch-14in below.
        ("retina-notch-14in", 3024, 1964, 2, 0,
            State(spread: 0.45f, thickness: 6, intensity: 0.8f, pulse: 0.6f, phase: 0.3f), EdgeStyle.Glow, default),
        ("corner-radius-1440p", 2560, 1440, 1, 12, State(spread: 0.6f, thickness: 12, intensity: 1, pulse: 1),
            EdgeStyle.Glow, default),
        ("wide-shoulder-narrow-glow", 1280, 720, 1, 0, State(spread: 0.1f, thickness: 40, intensity: 0.85f),
            EdgeStyle.Glow, default),
        ("hairline", 800, 600, 1, 0, State(spread: 0, thickness: 0, intensity: 0.55f, visibility: 0.5f),
            EdgeStyle.Glow, default),
        ("half-scale-retina", 1512, 982, 1, 8,
            State(spread: 0.8f, thickness: 4, intensity: 0.6f, pulse: 0.2f, phase: 0.95f), EdgeStyle.Glow, default),
        // The Line style and the notch (Maxwell's request): the bright line with its halo, and the light round the
        // notch, whose screen has rounded top corners (corner radius 0: they come from NotchedDisplayTopCornerDip).
        ("line-defaults-1080p", 1920, 1080, 1, 0, State(spread: 0.45f, thickness: 6, intensity: 0.8f),
            EdgeStyle.Line, default),
        ("line-retina-notch-14in", 3024, 1964, 2, 0,
            State(spread: 0.45f, thickness: 6, intensity: 0.8f, pulse: 0.6f, phase: 0.3f), EdgeStyle.Line, Notch14),
        ("glow-retina-notch-14in", 3024, 1964, 2, 0,
            State(spread: 0.45f, thickness: 6, intensity: 0.8f, pulse: 0.6f, phase: 0.3f), EdgeStyle.Glow, Notch14),
        ("line-thick-corner", 2560, 1440, 1, 12, State(spread: 0.6f, thickness: 12, intensity: 1, pulse: 1),
            EdgeStyle.Line, default),
        // The Flow movement (Maxwell's request): comets and the beats' ripples travelling round the edge. The line
        // round the notch at the full amount, with one ripple running either side of the notch and one just landed
        // (radius 0); the soft glow at a partial amount, with a ripple that starts just before the top-left corner, so
        // it runs across the perimeter coordinate's wrap, one further on and a faint wide one.
        ("flow-line-retina-notch-14in", 3024, 1964, 2, 0,
            State(spread: 0.45f, thickness: 6, intensity: 0.8f, pulse: 0.6f, phase: 0.3f) with
            {
                Flow = new FlowMotion(1, 0.3f, new Ripple(0.15f, 0.04f, 0.9f), default, new Ripple(0.62f, 0, 0.5f),
                    default),
            },
            EdgeStyle.Line, Notch14),
        ("flow-glow-1080p-half", 1920, 1080, 1, 0,
            State(spread: 0.45f, thickness: 6, intensity: 0.8f, pulse: 0.3f, phase: 0.6f) with
            {
                Flow = new FlowMotion(0.6f, 0.85f, new Ripple(0.99f, 0.03f, 0.7f), new Ripple(0.4f, 0.12f, 0.3f),
                    new Ripple(0.75f, 0.2f, 0.05f), default),
            },
            EdgeStyle.Glow, default),
    ];

    private static readonly Rgb Mean = new(0.3f, 0.2f, 0.6f);

    [Fact]
    public void TheMacGoldenTableIsCurrent()
    {
        string path = Path.Combine(RepoRoot(), "mac", "Tests", "RimlightKitTests", "Fixtures", "glow-golden.json");
        string json = Build();
        if (Environment.GetEnvironmentVariable("RIMLIGHT_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, json);
            return;
        }
        Assert.True(File.Exists(path), $"Missing {path}: run the tests once with RIMLIGHT_UPDATE_GOLDEN=1.");
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(path));
        using JsonDocument actual = JsonDocument.Parse(json);
        AssertSame(expected.RootElement, actual.RootElement, "$");
    }

    private static string Build()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"note\": \"Generated by src/Rimlight.Native.Tests/GlowGoldenTests.cs from GlowProfile and GlowConstants. Do not edit.\",\n  \"cases\": [\n");
        for (int c = 0; c < Cases.Length; c++)
        {
            var (name, w, h, scale, corner, state, style, notch) = Cases[c];
            var surface = new SurfaceInfo
            {
                WidthPx = w, HeightPx = h, PixelsPerPoint = scale,
                NotchLeftPt = notch.LeftPt, NotchRightPt = notch.RightPt, NotchHeightPt = notch.HeightPt,
            };
            GlowConstants k = MacOverlayLoop.ConstantsFor(state, surface, corner, style, Mean);
            float[] constants =
            [
                k.ScreenWidthPx, k.ScreenHeightPx, k.CornerRadiusPx, k.ShoulderPx, k.SpreadPx, k.Gain, k.Pulse, k.Phase,
                k.Visibility, k.ColorRadiusPx, k.GlowRadiusPx, k.LinePx, k.MeanRed, k.MeanGreen, k.MeanBlue,
                k.TopCornerRadiusPx, k.NotchLeftPx, k.NotchRightPx, k.NotchHeightPx, k.NotchRadiusPx,
                k.FlowAmount, k.CometPhase, k.FlowPad0, k.FlowPad1,
                k.Ripple0.Origin, k.Ripple0.Radius, k.Ripple0.Strength, k.Ripple0.Pad,
                k.Ripple1.Origin, k.Ripple1.Radius, k.Ripple1.Strength, k.Ripple1.Pad,
                k.Ripple2.Origin, k.Ripple2.Radius, k.Ripple2.Strength, k.Ripple2.Pad,
                k.Ripple3.Origin, k.Ripple3.Radius, k.Ripple3.Strength, k.Ripple3.Pad,
            ];
            bool line = style == EdgeStyle.Line;
            bool flow = k.FlowAmount > 0;
            sb.Append("    {\n");
            sb.Append($"      \"name\": \"{name}\",\n");
            sb.Append($"      \"edgeStyle\": \"{style}\",\n");
            sb.Append($"      \"surface\": {{ \"widthPx\": {w}, \"heightPx\": {h}, \"pixelsPerPoint\": {F(scale)}, \"cornerRadiusDip\": {F(corner)}, \"notchLeftPt\": {F(notch.LeftPt)}, \"notchRightPt\": {F(notch.RightPt)}, \"notchHeightPt\": {F(notch.HeightPt)} }},\n");
            // Only the Flow cases' state has the Flow movement, so the other cases' entries are as they were before it.
            string flowState = flow ? $", \"flow\": {Flow(state.Flow)}" : "";
            sb.Append($"      \"state\": {{ \"intensity\": {F(state.Intensity)}, \"spread\": {F(state.Spread)}, \"coreThicknessDip\": {F(state.CoreThicknessDip)}, \"phase\": {F(state.Phase)}, \"pulse\": {F(state.Pulse)}, \"visibility\": {F(state.Visibility)}, \"ratio\": {F(state.Ratio)}{flowState} }},\n");
            sb.Append($"      \"mean\": [{F(Mean.R)}, {F(Mean.G)}, {F(Mean.B)}],\n");
            sb.Append($"      \"constants\": [{string.Join(", ", constants.Select(F))}],\n");
            sb.Append("      \"samples\": [\n");
            var points = Points(w, h, k.GlowRadiusPx).Concat(MorePoints(k)).Concat(FlowPoints(k)).ToArray();
            for (int i = 0; i < points.Length; i++)
            {
                (float x, float y) = points[i];
                // The depths GlowProfile.Alpha takes the alpha at: the light's field (round the notch), and in the
                // Line style the screen's own shape (round the notch too). Only the Line style's samples have the
                // second, so the Glow cases' samples are as they were before it.
                float depth = GlowProfile.Depth(x, y, w, h, k.GlowRadiusPx);
                float lineDepth = GlowProfile.Depth(x, y, w, h, k.TopCornerRadiusPx, k.CornerRadiusPx);
                if (k.NotchHeightPx > 0)
                {
                    float toNotch = GlowProfile.NotchDistance(x, y, k.NotchLeftPx, k.NotchRightPx, k.NotchHeightPx,
                        k.NotchRadiusPx);
                    depth = GlowProfile.AroundNotch(depth, toNotch, k.NotchRadiusPx);
                    lineDepth = GlowProfile.AroundNotch(lineDepth, toNotch, k.NotchRadiusPx);
                }
                float alpha = GlowProfile.Alpha(k, x, y);
                string lineField = line ? $", \"lineDepth\": {F(lineDepth)}" : "";
                // The Flow cases' samples also have the perimeter coordinate the motion is taken at and the motion
                // itself (GlowProfile.Motion), which the alpha is multiplied by.
                float t = GlowProfile.PerimeterT(x, y, w, h, k.ColorRadiusPx);
                string flowFields = flow ? $", \"t\": {F(t)}, \"motion\": {F(GlowProfile.Motion(k, x, y))}" : "";
                sb.Append($"        {{ \"x\": {F(x)}, \"y\": {F(y)}, \"depth\": {F(depth)}{lineField}{flowFields}, \"alpha\": {F(alpha)} }}");
                sb.Append(i + 1 < points.Length ? ",\n" : "\n");
            }
            sb.Append("      ]\n    }");
            sb.Append(c + 1 < Cases.Length ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    // Pixel centres: in from the middle of the top and left edges, along the diagonal from the top-left corner, in the
    // corner's arc and at the centre.
    private static IEnumerable<(float X, float Y)> Points(int w, int h, float glowRadius)
    {
        float[] depths = [0.5f, 1.5f, 2.5f, 4.5f, 8.5f, 16.5f, 32.5f, 64.5f, 128.5f];
        foreach (float d in depths) yield return (MathF.Floor(w / 2f) + 0.5f, d);
        foreach (float d in depths) yield return (d, MathF.Floor(h / 2f) + 0.5f);
        foreach (float d in depths) yield return (d, d);
        yield return (glowRadius * 0.3f + 0.5f, glowRadius * 0.7f + 0.5f);
        yield return (w - 0.5f, h - 0.5f);
        yield return (w / 2f + 0.5f, h / 2f + 0.5f);
    }

    // The Line style's and the notch's own points, none for the Glow style without a notch (whose samples stay as they
    // were): across the line's anti-aliased edge at the left edge's middle (L - 1, L, L + 1 px in) and the bottom's,
    // along the diagonals of a top and a bottom corner (their radii differ on a notched screen), and round the notch:
    // inside it, beside it, below it, at its lower corners and in the "ears" where it meets the top edge.
    private static IEnumerable<(float X, float Y)> MorePoints(GlowConstants k)
    {
        float w = k.ScreenWidthPx, h = k.ScreenHeightPx;
        if (k.LinePx > 0)
        {
            float l = k.LinePx;
            foreach (float d in new[] { l - 1, l, l + 1 })
            {
                yield return (d, MathF.Floor(h / 2f) + 0.5f);
                yield return (MathF.Floor(w / 4f) + 0.5f, h - d);
            }
            foreach (float share in new[] { 0.25f, 0.29f, 0.35f })
            {
                float top = k.TopCornerRadiusPx * share + 0.5f, bottom = k.CornerRadiusPx * share + 0.5f;
                yield return (top, top);
                yield return (w - top, top);
                yield return (bottom, h - bottom);
            }
        }
        if (k.NotchHeightPx > 0)
        {
            float left = k.NotchLeftPx, right = k.NotchRightPx, depth = k.NotchHeightPx, r = k.NotchRadiusPx;
            float middle = MathF.Floor((left + right) / 2f) + 0.5f, half = MathF.Floor(depth / 2f) + 0.5f;
            yield return (middle, 0.5f);                 // inside the notch
            yield return (middle, half);
            yield return (left + 0.5f, depth - 0.5f);    // in its box, just outside a rounded lower corner
            foreach (float d in new[] { 0.5f, 2.5f, 8.5f, 32.5f })
            {
                yield return (left - d, half);           // beside it
                yield return (right + d, half);
                yield return (middle, depth + d);        // below it
            }
            yield return (left - 0.3f * r, depth + 0.3f * r); // round its lower corners
            yield return (right + r, depth + r);
            foreach (float d in new[] { 0.5f, 0.25f * r, 0.5f * r, r, 2 * r })
                yield return (left - d, 0.5f);           // the ears, along the top edge
            yield return (left - 0.5f * r, 0.5f * r);
            yield return (right + 0.25f * r, 0.25f * r);
            yield return (right + 0.5f * r, 1.5f);
        }
    }

    // The Flow movement's own points, none without a Flow amount (the other cases' samples stay as they were): 32 round
    // the perimeter half a pixel in, evenly spaced in t; either side of each comet's head and along its tail; across
    // each live ripple's two fronts; and inward from the bottom and right edges, through the blend to the mean deep
    // inside (half the colour radius to all of it) and past it.
    private static IEnumerable<(float X, float Y)> FlowPoints(GlowConstants k)
    {
        if (!(k.FlowAmount > 0)) yield break;
        float w = k.ScreenWidthPx, h = k.ScreenHeightPx, r = k.ColorRadiusPx;
        var targets = new List<float>();
        for (int i = 0; i < 32; i++) targets.Add((i + 0.5f) / 32);
        for (int i = 0; i < GlowProfile.CometCount; i++)
        {
            float head = (float)i / GlowProfile.CometCount - k.CometPhase;
            foreach (float offset in new[] { -0.02f, -0.01f, -0.005f, 0, 0.003f, 0.01f, 0.06f, 0.12f })
                targets.Add(head + offset); // ahead of the head (smaller t), then along its tail
        }
        foreach (GlowRipple ripple in new[] { k.Ripple0, k.Ripple1, k.Ripple2, k.Ripple3 })
        {
            if (!(ripple.Strength > 0)) continue;
            foreach (float side in new[] { -1f, 1f })
            {
                foreach (float offset in new[] { -0.03f, -0.01f, 0, 0.005f, 0.015f, 0.03f })
                    targets.Add(ripple.Origin + side * (ripple.Radius + offset)); // behind the front, then ahead
            }
        }
        (float X, float Y, float T)[] ring = Ring(w, h, r);
        var seen = new HashSet<(float, float)>();
        foreach (float target in targets)
        {
            var nearest = ring.MinBy(p => Apart(p.T, target));
            if (seen.Add((nearest.X, nearest.Y))) yield return (nearest.X, nearest.Y);
        }
        foreach (float d in new[] { 1.5f, 4.5f, 16.5f, 0.5f * r, 0.6f * r, 0.75f * r, 0.9f * r, r, 1.2f * r })
        {
            yield return (MathF.Floor(0.37f * w) + 0.5f, h - d);
            yield return (w - d, MathF.Floor(0.6f * h) + 0.5f);
        }
    }

    // The pixel centres half a pixel in from the edge of a w x h surface, clockwise from the top-left corner, with
    // their perimeter coordinates on the colour path of radius r.
    private static (float X, float Y, float T)[] Ring(float w, float h, float r)
    {
        var points = new List<(float X, float Y)>();
        for (float x = 0.5f; x < w; x++) points.Add((x, 0.5f));
        for (float y = 1.5f; y < h; y++) points.Add((w - 0.5f, y));
        for (float x = w - 1.5f; x > 0; x--) points.Add((x, h - 0.5f));
        for (float y = h - 1.5f; y > 1; y--) points.Add((0.5f, y));
        return points.Select(p => (p.X, p.Y, GlowProfile.PerimeterT(p.X, p.Y, w, h, r))).ToArray();
    }

    // How far apart two perimeter coordinates are, the shorter way round.
    private static float Apart(float a, float b)
    {
        float d = MathF.Abs(a - b) % 1;
        return MathF.Min(d, 1 - d);
    }

    // The light state's Flow movement as the golden table records it.
    private static string Flow(FlowMotion f)
    {
        IEnumerable<string> ripples = new[] { f.Ripple0, f.Ripple1, f.Ripple2, f.Ripple3 }.Select(r =>
            $"{{ \"origin\": {F(r.Origin)}, \"radius\": {F(r.Radius)}, \"strength\": {F(r.Strength)} }}");
        return $"{{ \"amount\": {F(f.Amount)}, \"cometPhase\": {F(f.CometPhase)}, \"ripples\": [{string.Join(", ", ripples)}] }}";
    }

    private static LightState State(float spread, float thickness, float intensity, float pulse = 0, float phase = 0,
        float visibility = 1) =>
        new(new Rgb(0.8f, 0.2f, 0.9f), new Rgb(0.1f, 0.8f, 0.9f), 0.6f, intensity, spread, thickness, phase, pulse,
            visibility);

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    // A camera notch in points from the screen's top-left (RlSurface's notch); default is none.
    private readonly record struct Notch(float LeftPt, float RightPt, float HeightPt);

    private static void AssertSame(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: {expected.ValueKind} vs {actual.ValueKind}");
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty p in expected.EnumerateObject())
                {
                    Assert.True(actual.TryGetProperty(p.Name, out JsonElement a), $"{path}.{p.Name} missing");
                    AssertSame(p.Value, a, $"{path}.{p.Name}");
                }
                Assert.Equal(expected.EnumerateObject().Count(), actual.EnumerateObject().Count());
                break;
            case JsonValueKind.Array:
                Assert.True(expected.GetArrayLength() == actual.GetArrayLength(), $"{path}: length");
                for (int i = 0; i < expected.GetArrayLength(); i++) AssertSame(expected[i], actual[i], $"{path}[{i}]");
                break;
            case JsonValueKind.Number:
                double e = expected.GetDouble(), v = actual.GetDouble();
                Assert.True(Math.Abs(e - v) <= Tolerance * Math.Max(1, Math.Abs(e)),
                    $"{path}: expected {e}, got {v}. GlowProfile or GlowConstants changed: regenerate with RIMLIGHT_UPDATE_GOLDEN=1 and update glow_math.h.");
                break;
            default:
                Assert.Equal(expected.ToString(), actual.ToString());
                break;
        }
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
