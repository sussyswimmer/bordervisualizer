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
        // Lay the cbuffer out by HLSL's packing rule (a vector never straddles a 16-byte register) and compare each
        // field's name and byte offset with the struct's. ScreenPx and MeanColor are vectors in HLSL, two and three
        // floats in C#.
        var expected = new List<(string Name, int Offset)>();
        int offset = 0;
        foreach ((string type, string name) in CbufferFields(ShaderSource()))
        {
            int components = type switch
            {
                "float" => 1,
                "float2" => 2,
                "float3" => 3,
                "float4" => 4,
                _ => throw new InvalidOperationException($"Unexpected cbuffer type {type} for {name}"),
            };
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

    internal static string ShaderSource()
    {
        using Stream stream = typeof(GlowConstants).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // The (type, name) of each field of `cbuffer Light`, in order.
    private static IEnumerable<(string Type, string Name)> CbufferFields(string shader)
    {
        Match block = Regex.Match(shader, @"cbuffer\s+Light\b[^{]*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, "Glow.hlsl has no cbuffer Light");
        foreach (string raw in block.Groups["body"].Value.Split('\n'))
        {
            int comment = raw.IndexOf("//", StringComparison.Ordinal);
            string line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            Match field = Regex.Match(line, @"^(?<type>\w+)\s+(?<name>\w+)\s*;$");
            Assert.True(field.Success, $"Unexpected cbuffer line: {line}");
            yield return (field.Groups["type"].Value, field.Groups["name"].Value);
        }
    }

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);
}
