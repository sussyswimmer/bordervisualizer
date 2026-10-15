using System.Collections.ObjectModel;
using System.Globalization;
using Rimlight.Core;
using Rimlight.Core.SettingsStorage;
using Xunit;

namespace Rimlight.Tests.SettingsStorage;

// C7: the five doc 06 §1 presets, with the H-009 decision (Override on, ColorMode kept, Minimal → Idle Glow).
public sealed class PresetsTests
{
    private static readonly string[] Appearance =
    [
        nameof(Settings.PrimaryHex), nameof(Settings.SecondaryHex), nameof(Settings.PrimaryRatio),
        nameof(Settings.CoreThicknessDip), nameof(Settings.Glow), nameof(Settings.Brightness),
    ];

    public static TheoryData<string> Names => ["Aurora", "Sunset", "Neon", "Ember", "Minimal"];

    private static Settings[] Inputs() =>
    [
        new Settings(),
        SettingsTestData.FullyCustom(),
        SettingsTestData.FullyCustom() with { ColorMode = ColorMode.AlbumArt, OverrideAlbumColor = false, Animation = AnimationMode.Off },
    ];

    [Fact]
    public void FivePresetsInMenuOrder()
    {
        Assert.Equal(new[] { "Aurora", "Sunset", "Neon", "Ember", "Minimal" }, Presets.All.Select(p => p.Name));
    }

    [Fact]
    public void CatalogCantBeChanged()
    {
        // Not an array, whose elements could be replaced through IList.
        Assert.IsType<ReadOnlyCollection<(string Name, Func<Settings, Settings> Apply)>>(Presets.All);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void PresetSetsAppearanceAndOverrideAndKeepsEverythingElse(string name)
    {
        foreach (Settings input in Inputs())
        {
            Settings result = Apply(name, input);
            Assert.NotSame(input, result);
            Assert.True(result.OverrideAlbumColor);
            Assert.Equal(input.ColorMode, result.ColorMode);

            string[] allowed = [.. Appearance, nameof(Settings.OverrideAlbumColor), .. name == "Minimal" ? [nameof(Settings.Animation)] : Array.Empty<string>()];
            string[] unexpected = SettingsTestData.ChangedFields(input, result).Except(allowed).ToArray();
            Assert.True(unexpected.Length == 0, $"{name} changed {string.Join(", ", unexpected)}");
            if (name != "Minimal") Assert.Equal(input.Animation, result.Animation);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void PresetReturnsValidatedSettings(string name)
    {
        foreach (Settings input in Inputs().Append(SettingsTestData.Messy()))
        {
            Settings result = Apply(name, input);
            Assert.Same(result, SettingsValidator.Validate(result));
        }

        Settings repaired = Apply(name, SettingsTestData.Messy());
        Assert.Equal(60, repaired.FpsCap);
        Assert.Equal("Ctrl+Alt+L", repaired.ToggleHotkey);
        Assert.Equal(new[] { "A" }, repaired.CustomMonitorIds);
        Assert.Equal(0.25f, repaired.Sensitivity);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void ApplyingTwiceChangesNothingMore(string name)
    {
        Settings once = Apply(name, SettingsTestData.FullyCustom());
        Assert.Equal(once, Apply(name, once)); // record equality: the monitor list instance is kept
    }

    [Fact]
    public void LastPresetDecidesTheLookWhateverCameBefore()
    {
        Settings start = SettingsTestData.FullyCustom() with { Animation = AnimationMode.Off };
        foreach ((string first, Func<Settings, Settings> applyFirst) in Presets.All)
        {
            foreach ((string second, Func<Settings, Settings> applySecond) in Presets.All)
            {
                Settings direct = applySecond(start);
                Settings chained = applySecond(applyFirst(start));
                string[] differences = SettingsTestData.ChangedFields(direct, chained);
                // Only Minimal's Idle Glow can linger: Animation is motion, which the other presets don't touch.
                string[] expected = first == "Minimal" && second != "Minimal" ? [nameof(Settings.Animation)] : [];
                Assert.True(expected.SequenceEqual(differences), $"{first} → {second}: {string.Join(", ", differences)}");
            }
        }
    }

    [Fact]
    public void NullInputThrows()
    {
        foreach ((_, Func<Settings, Settings> apply) in Presets.All)
            Assert.Throws<ArgumentNullException>(() => apply(null!));
    }

    [Fact]
    public void AuroraIsTealIntoVioletWithGlowPointSix()
    {
        Settings s = Apply("Aurora", new Settings());
        Assert.Equal(0.6f, s.Glow);
        AssertHue(s.PrimaryHex, 160, 185, "teal");
        AssertHue(s.SecondaryHex, 260, 285, "violet");
        AssertVivid(s);
    }

    [Fact]
    public void SunsetIsOrangeIntoPink()
    {
        Settings s = Apply("Sunset", new Settings());
        AssertHue(s.PrimaryHex, 18, 38, "orange");
        AssertHue(s.SecondaryHex, 320, 345, "pink");
        AssertVivid(s);
    }

    [Fact]
    public void NeonIsMagentaAndCyanWithAThickCore()
    {
        Settings s = Apply("Neon", new Settings());
        AssertHue(s.PrimaryHex, 295, 320, "magenta");
        AssertHue(s.SecondaryHex, 180, 195, "cyan");
        AssertVivid(s);
        Assert.True(s.CoreThicknessDip >= 2 * new Settings().CoreThicknessDip, $"core {s.CoreThicknessDip} DIP isn't thick");
        Assert.Equal(1f, s.Brightness);
    }

    [Fact]
    public void EmberIsRedIntoAmberAndSlow()
    {
        Settings s = Apply("Ember", new Settings());
        AssertHue(s.PrimaryHex, 0, 15, "red");
        AssertHue(s.SecondaryHex, 35, 48, "amber");
        AssertVivid(s);
        // "Slow" within appearance fields: a soft wide glow, dimmer and thinner than the default, so beats swell.
        var d = new Settings();
        Assert.True(s.Glow >= 0.75f && s.Glow > d.Glow);
        Assert.True(s.Brightness <= 0.65f && s.Brightness < d.Brightness);
        Assert.True(s.CoreThicknessDip < d.CoreThicknessDip);
    }

    [Fact]
    public void MinimalIsASoftWhiteHairlineInIdleGlow()
    {
        Settings s = Apply("Minimal", new Settings());
        Assert.Equal(AnimationMode.IdleGlow, s.Animation);
        foreach (string hex in new[] { s.PrimaryHex, s.SecondaryHex })
        {
            (_, float saturation, float value) = Hsv(hex);
            Assert.True(saturation <= 0.08f && value >= 0.9f, $"{hex} isn't a soft white");
        }

        Assert.True(s.CoreThicknessDip <= 1.5f, "not a hairline");
        Assert.True(s.Glow <= 0.25f);
        Assert.True(s.Brightness <= 0.6f, "not soft");
    }

    [Fact]
    public void EveryPresetLooksDifferent()
    {
        string[] swatches = Presets.All.Select(p => p.Apply(new Settings())).Select(s => s.PrimaryHex + s.SecondaryHex).ToArray();
        Assert.Equal(swatches.Length, swatches.Distinct().Count());
    }

    private static Settings Apply(string name, Settings settings) => Presets.All.Single(p => p.Name == name).Apply(settings);

    private static void AssertVivid(Settings s)
    {
        foreach (string hex in new[] { s.PrimaryHex, s.SecondaryHex })
        {
            (_, float saturation, float value) = Hsv(hex);
            Assert.True(saturation >= 0.6f && value >= 0.85f, $"{hex} is too dull for a glow");
        }
    }

    private static void AssertHue(string hex, float min, float max, string family)
    {
        float hue = Hsv(hex).Hue;
        Assert.True(hue >= min && hue <= max, $"{hex} has hue {hue:F0}°, not {family} ({min}–{max}°)");
    }

    private static (float Hue, float Saturation, float Value) Hsv(string hex)
    {
        float r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255f;
        float g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255f;
        float b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float chroma = max - min;
        float hue = chroma == 0 ? 0
            : max == r ? 60 * ((g - b) / chroma % 6)
            : max == g ? 60 * ((b - r) / chroma + 2)
            : 60 * ((r - g) / chroma + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : chroma / max, max);
    }
}
