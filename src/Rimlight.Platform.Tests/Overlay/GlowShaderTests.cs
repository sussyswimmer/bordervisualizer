using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// Glow.hlsl against its C# mirrors (soft edge review). The shader can't compile or run here, so its source is read from
// the embedded resource GpuDevice compiles: GlowConstants is uploaded as raw bytes into its cbuffer, and GlowProfile
// (which the Settings preview and GlowProfileTests run) must compute what step 2 draws. Editing one side without the
// other fails these.
public sealed class GlowShaderTests
{
    private const string ResourceName = "Rimlight.Platform.Overlay.Glow.hlsl";

    [Fact]
    public void GlowConstantsSitWhereTheShadersCbufferReadsThem()
    {
        // Lay the cbuffer out by HLSL's packing rules (a vector never straddles a 16-byte register; every element of an
        // array starts a register of its own) and compare each field's name and byte offset with the struct's.
        // ScreenPx and MeanColor are vectors in HLSL, two and three floats in C#; the Flow movement's Ripples[i] is the
        // GlowRipple RippleI, whose x, y, z and w are its Origin, Radius, Strength and Pad.
        var expected = new List<(string Name, int Offset)>();
        int offset = 0;
        foreach ((string type, string name, int count) in CbufferFields(ShaderSource()))
        {
            int components = type switch
            {
                "float" => 1,
                "float2" => 2,
                "float3" => 3,
                "float4" => 4,
                _ => throw new InvalidOperationException($"Unexpected cbuffer type {type} for {name}"),
            };
            if (count > 0)
            {
                Assert.True((name, components) == ("Ripples", 4), $"No C# mapping for the cbuffer's {type} {name}[{count}]");
                for (int i = 0; i < count; i++)
                {
                    offset = (offset + 15) / 16 * 16;
                    expected.Add(($"Ripple{i}", offset));
                    offset += 16;
                }
                continue;
            }
            if (offset / 16 != (offset + 4 * components - 1) / 16)
                offset = (offset + 15) / 16 * 16;
            string[] fields = (name, components) switch
            {
                ("ScreenPx", 2) => ["ScreenWidthPx", "ScreenHeightPx"],
                ("MeanColor", 3) => ["MeanRed", "MeanGreen", "MeanBlue"],
                (_, 1) => [name],
                _ => throw new InvalidOperationException($"No C# mapping for the cbuffer's {type} {name}"),
            };
            foreach (string field in fields)
            {
                expected.Add((field, offset));
                offset += 4;
            }
        }

        var actual = typeof(GlowConstants).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => (f.Name, (int)Marshal.OffsetOf<GlowConstants>(f.Name)))
            .OrderBy(f => f.Item2)
            .ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(GlowConstants.SizeInBytes, (offset + 15) / 16 * 16);
        Assert.Equal(GlowConstants.SizeInBytes, Marshal.SizeOf<GlowConstants>());
        Assert.Equal(160, GlowConstants.SizeInBytes);

        // A ripple fills its register as the shader reads it: x origin, y radius, z strength.
        var ripple = typeof(GlowRipple).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => (f.Name, (int)Marshal.OffsetOf<GlowRipple>(f.Name)))
            .OrderBy(f => f.Item2)
            .ToList();
        Assert.Equal(new[] { ("Origin", 0), ("Radius", 4), ("Strength", 8), ("Pad", 12) }, ripple);
        Assert.Equal(16, Marshal.SizeOf<GlowRipple>());
    }

    [Fact]
    public void TheShaderDrawsGlowProfilesFormulas()
    {
        string shader = ShaderSource();
        Assert.Contains("float glowDistance = max(-SdRoundBox(p - halfSize, halfSize, GlowRadiusPx), 0);", shader);
        Assert.Contains($"float spread = max(SpreadPx * (1 + {F(GlowProfile.BeatPush)} * Pulse), {F(GlowProfile.MinSpreadPx)});", shader);
        Assert.Contains($"float shoulder = ShoulderPx * (1 + {F(GlowProfile.BeatPush)} * Pulse);", shader);
        // GlowProfile.Glow: GlowPeak exp(-EasedDistance / spread), the distance eased through the shoulder's hyperbola.
        Assert.Contains($"float glow = {F(GlowProfile.GlowPeak)} * exp(-(sqrt(glowDistance * glowDistance + shoulder * shoulder) - shoulder) / spread);", shader);
        // The gain arrives rolled off (GlowProfile.Gain), so nothing clips the profile per pixel.
        Assert.Contains("float a = glow * Gain * Visibility;", shader);
        Assert.Contains("[branch] if (a < 0.5 / 255)", shader);
        // Round 1's band and its second field are gone: one light, one field.
        Assert.DoesNotContain("Band", shader);
    }

    [Fact]
    public void TheShaderDrawsTheLineStyleAndTheNotchAsGlowProfileDoes()
    {
        // GlowProfile.Alpha: the Line style behind LinePx > 0, the notch behind NotchHeightPx > 0, so the Glow style
        // on a screen without a notch runs exactly the code above.
        string shader = ShaderSource();
        Assert.Contains("[branch] if (LinePx > 0)", shader);
        Assert.Contains("[branch] if (NotchHeightPx > 0)", shader);
        // GlowProfile.Depth with per-corner radii: the top corners' in the top half.
        Assert.Contains("float lineRadius = p.y < halfSize.y ? TopCornerRadiusPx : CornerRadiusPx;", shader);
        Assert.Contains("float lineDistance = max(-SdRoundBox(p - halfSize, halfSize, lineRadius), 0);", shader);
        // GlowProfile.Line (LinePeak is 1, so the shader leaves it out), Halo and LineAndHalo.
        Assert.Equal(1f, GlowProfile.LinePeak);
        Assert.Contains($"float lineCoverage = 1 - smoothstep(LinePx - {F(GlowProfile.LineSoftnessPx)}, " +
            $"LinePx + {F(GlowProfile.LineSoftnessPx)}, lineDistance);", shader);
        Assert.Contains($"float halo = {F(GlowProfile.HaloPeak)} * " +
            "exp(-(sqrt(glowDistance * glowDistance + shoulder * shoulder) - shoulder) / spread);", shader);
        Assert.Contains("glow = 1 - (1 - lineCoverage) * (1 - halo);", shader);
        // GlowProfile.NotchDistance, SmoothMin and AroundNotch, applied to both depths.
        Assert.Contains("float2 notchHalf = float2(0.5 * (NotchRightPx - NotchLeftPx), NotchHeightPx);", shader);
        Assert.Contains("float radius = min(NotchRadiusPx, min(notchHalf.x, notchHalf.y));", shader);
        Assert.Contains("return max(SdRoundBox(p - float2(0.5 * (NotchLeftPx + NotchRightPx), 0), notchHalf, " +
            "radius), 0);", shader);
        Assert.Contains("return min(a, b) - h * h * k / 4;", shader);
        Assert.Contains("return max(SmoothMin(depth, notchDistance, NotchRadiusPx), 0);", shader);
        Assert.Contains("glowDistance = AroundNotch(glowDistance, notchDistance);", shader);
        Assert.Contains("lineDistance = AroundNotch(lineDistance, notchDistance);", shader);
    }

    [Fact]
    public void TheShaderMovesTheLightAsGlowProfileDoes()
    {
        // The Flow movement (H-033): GlowProfile.Comets, RippleAt, Flow and Motion, with the same constants, on the
        // colours' own perimeter coordinate, behind FlowAmount > 0 so the Pulse movement and Idle Glow run the code
        // above unchanged.
        string shader = ShaderSource();
        Assert.Equal(GlowProfile.CometCount, int.Parse(Constant(shader, "int", "CometCount"), CultureInfo.InvariantCulture));
        Assert.Equal(GlowProfile.CometHead, FloatConstant(shader, "CometHead"));
        Assert.Equal(GlowProfile.CometTail, FloatConstant(shader, "CometTail"));
        Assert.Equal(GlowProfile.FlowFloor, FloatConstant(shader, "FlowFloor"));
        Assert.Equal(GlowProfile.RippleFront, FloatConstant(shader, "RippleFront"));
        Assert.Equal(GlowProfile.RippleTrail, FloatConstant(shader, "RippleTrail"));
        Assert.Equal(GlowProfile.FlowMean, FloatConstant(shader, "FlowMean"));

        // Comets: the larger exponent of each comet's head and tail, round the loop.
        Assert.Contains("float head = (float)k / CometCount - CometPhase;", shader);
        Assert.Contains("float behind = frac(t - head) / CometTail;", shader);
        Assert.Contains("float ahead = frac(head - t) / CometHead;", shader);
        Assert.Contains("brightest = max(brightest, max(-behind, -ahead * ahead));", shader);
        Assert.Contains("return exp(brightest);", shader);
        // RippleAt: nothing without a strength, else a Gaussian ahead of each front and a trail behind it.
        Assert.Contains("return x - floor(x + 0.5);", shader);
        Assert.Contains("[branch] if (ripple.z > 0)", shader);
        Assert.Contains("float x = abs(Wrap(t - ripple.x)) - ripple.y;", shader);
        Assert.Contains("float exponent = x >= 0 ? -(x / RippleFront) * (x / RippleFront) : x / RippleTrail;", shader);
        Assert.Contains("light = ripple.z * exp(exponent);", shader);
        // Flow and Motion: the ripples over the comets like light through two filters, the blend deep inside, the
        // amount.
        Assert.Contains("float m = FlowFloor + (1 - FlowFloor) * Comets(t);", shader);
        Assert.Contains("m = 1 - (1 - m) * (1 - RippleAt(t, Ripples[i]));", shader);
        Assert.Contains("m += (FlowMean - m) * deep;", shader);
        Assert.Contains("return 1 + (m - 1) * FlowAmount;", shader);
        // PSMain: one perimeter coordinate for the colour and the motion, the colour's blend reused, the motion behind
        // its branch and a second early out, all before the gradient is sampled.
        Assert.Contains("float t = PerimeterT(p, ScreenPx, ColorRadiusPx);", shader);
        Assert.Contains("float deep = smoothstep(0.5 * ColorRadiusPx, ColorRadiusPx, depth);", shader);
        Assert.Contains("[branch] if (FlowAmount > 0)", shader);
        Assert.Contains("a *= Motion(t, deep);", shader);
        Assert.Contains("float u = frac(t + Phase);", shader);
        Assert.Contains("rgb = lerp(rgb, MeanColor, deep);", shader);
        Assert.Single(Regex.Matches(shader, @"PerimeterT\(p,"));
        int motion = shader.IndexOf("a *= Motion(t, deep);", StringComparison.Ordinal);
        Assert.True(motion < shader.IndexOf("Gradient.SampleLevel", StringComparison.Ordinal));
        Assert.Contains("[branch] if (a < 0.5 / 255)", shader[motion..]);
    }

    [Fact]
    public void TheShaderIsAscii()
    {
        // D3DCompile reads the source as bytes; a stray non-ASCII character in a comment has broken shaders before.
        Assert.All(ShaderSource(), c => Assert.True(c < 128, $"Glow.hlsl has the non-ASCII character U+{(int)c:X4}"));
    }

    internal static string ShaderSource()
    {
        using Stream stream = typeof(GlowConstants).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // The (type, name, array length or 0) of each field of `cbuffer Light`, in order.
    private static IEnumerable<(string Type, string Name, int Count)> CbufferFields(string shader)
    {
        Match block = Regex.Match(shader, @"cbuffer\s+Light\b[^{]*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, "Glow.hlsl has no cbuffer Light");
        foreach (string raw in block.Groups["body"].Value.Split('\n'))
        {
            int comment = raw.IndexOf("//", StringComparison.Ordinal);
            string line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            Match field = Regex.Match(line, @"^(?<type>\w+)\s+(?<name>\w+)\s*(\[\s*(?<count>\d+)\s*\])?\s*;$");
            Assert.True(field.Success, $"Unexpected cbuffer line: {line}");
            int count = field.Groups["count"].Success ? int.Parse(field.Groups["count"].Value, CultureInfo.InvariantCulture) : 0;
            yield return (field.Groups["type"].Value, field.Groups["name"].Value, count);
        }
    }

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

    // The literal of `static const <type> <name> = <literal>;`.
    private static string Constant(string shader, string type, string name)
    {
        Match constant = Regex.Match(shader, $@"static\s+const\s+{type}\s+{name}\s*=\s*(?<value>[^;]+);");
        Assert.True(constant.Success, $"Glow.hlsl has no static const {type} {name}");
        return constant.Groups["value"].Value.Trim();
    }

    private static float FloatConstant(string shader, string name) =>
        float.Parse(Constant(shader, "float", name), CultureInfo.InvariantCulture);
}
