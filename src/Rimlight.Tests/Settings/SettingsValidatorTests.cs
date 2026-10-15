using Rimlight.Core;
using Rimlight.Core.SettingsStorage;
using Xunit;

namespace Rimlight.Tests.SettingsStorage;

// C7: doc 06 §1 ranges and the H-009 rules for the free-form fields.
public sealed class SettingsValidatorTests
{
    [Fact]
    public void ValidSettingsComeBackAsTheSameInstance()
    {
        var defaults = new Settings();
        Settings custom = SettingsTestData.FullyCustom();
        Assert.Same(defaults, SettingsValidator.Validate(defaults));
        Assert.Same(custom, SettingsValidator.Validate(custom));
    }

    [Fact]
    public void FullyCustomFixtureDiffersFromTheDefaultsInEveryFieldButVersion()
    {
        // Guards the round-trip tests: a field left at its default there would not prove it is saved and read.
        string[] changed = SettingsTestData.ChangedFields(new Settings(), SettingsTestData.FullyCustom());
        string[] expected = SettingsTestData.Properties.Select(p => p.Name).Where(n => n != nameof(Settings.Version)).Order().ToArray();
        Assert.Equal(expected, changed);
    }

    [Theory]
    [InlineData(nameof(Settings.PrimaryRatio), 0.1f, 0.9f, 0.6f)]
    [InlineData(nameof(Settings.CoreThicknessDip), 0f, 40f, 6f)]
    [InlineData(nameof(Settings.Glow), 0f, 1f, 0.45f)]
    [InlineData(nameof(Settings.Brightness), 0.1f, 1f, 0.8f)]
    [InlineData(nameof(Settings.Sensitivity), 0.25f, 2f, 1f)]
    [InlineData(nameof(Settings.CornerRadiusDip), 0f, 40f, 0f)]
    public void NumbersAreClampedAndNonFiniteValuesTakeTheDefault(string field, float min, float max, float fallback)
    {
        Assert.Equal(fallback, Get(new Settings(), field));
        float mid = (min + max) / 2;
        Assert.Equal(mid, Get(SettingsValidator.Validate(With(field, mid)), field));
        Assert.Equal(min, Get(SettingsValidator.Validate(With(field, min)), field));
        Assert.Equal(max, Get(SettingsValidator.Validate(With(field, max)), field));
        Assert.Equal(min, Get(SettingsValidator.Validate(With(field, min - 0.01f)), field));
        Assert.Equal(min, Get(SettingsValidator.Validate(With(field, -1e30f)), field));
        Assert.Equal(min, Get(SettingsValidator.Validate(With(field, float.MinValue)), field));
        Assert.Equal(max, Get(SettingsValidator.Validate(With(field, max + 0.01f)), field));
        Assert.Equal(max, Get(SettingsValidator.Validate(With(field, 1e30f)), field));
        Assert.Equal(max, Get(SettingsValidator.Validate(With(field, float.MaxValue)), field));
        Assert.Equal(fallback, Get(SettingsValidator.Validate(With(field, float.NaN)), field));
        Assert.Equal(fallback, Get(SettingsValidator.Validate(With(field, float.PositiveInfinity)), field));
        Assert.Equal(fallback, Get(SettingsValidator.Validate(With(field, float.NegativeInfinity)), field));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void SupportedFpsCapsAreKept(int fps)
    {
        Assert.Equal(fps, SettingsValidator.Validate(new Settings { FpsCap = fps }).FpsCap);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(29)]
    [InlineData(45)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(90)]
    [InlineData(144)]
    [InlineData(240)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void OtherFpsCapsBecomeSixty(int fps)
    {
        Assert.Equal(60, SettingsValidator.Validate(new Settings { FpsCap = fps }).FpsCap);
    }

    [Fact]
    public void UndefinedEnumValuesTakeTheDefault()
    {
        Settings valid = SettingsValidator.Validate(new Settings
        {
            Animation = (AnimationMode)99,
            WhenSilent = (SilentBehavior)(-1),
            ColorMode = (ColorMode)2,
            Monitors = (MonitorSelection)3,
            OnBattery = (BatteryBehavior)(-3),
        });
        Assert.Equal(AnimationMode.MusicSync, valid.Animation);
        Assert.Equal(SilentBehavior.IdleGlow, valid.WhenSilent);
        Assert.Equal(ColorMode.AlbumArt, valid.ColorMode);
        Assert.Equal(MonitorSelection.All, valid.Monitors);
        Assert.Equal(BatteryBehavior.Reduce, valid.OnBattery);
    }

    [Fact]
    public void EveryDefinedEnumValueIsKept()
    {
        foreach (AnimationMode value in Enum.GetValues<AnimationMode>())
            Assert.Equal(value, SettingsValidator.Validate(new Settings { Animation = value }).Animation);
        foreach (SilentBehavior value in Enum.GetValues<SilentBehavior>())
            Assert.Equal(value, SettingsValidator.Validate(new Settings { WhenSilent = value }).WhenSilent);
        foreach (ColorMode value in Enum.GetValues<ColorMode>())
            Assert.Equal(value, SettingsValidator.Validate(new Settings { ColorMode = value }).ColorMode);
        foreach (MonitorSelection value in Enum.GetValues<MonitorSelection>())
            Assert.Equal(value, SettingsValidator.Validate(new Settings { Monitors = value }).Monitors);
        foreach (BatteryBehavior value in Enum.GetValues<BatteryBehavior>())
            Assert.Equal(value, SettingsValidator.Validate(new Settings { OnBattery = value }).OnBattery);
    }

    [Theory]
    [InlineData("#7C5CFF", "#7C5CFF")]
    [InlineData("#7c5cff", "#7C5CFF")]
    [InlineData("#7c5CfF", "#7C5CFF")]
    [InlineData("  #12ab34\t", "#12AB34")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#ABC", "#AABBCC")]
    [InlineData("#000", "#000000")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void ColorsAreNormalizedToUpperCaseSixDigitHex(string input, string expected)
    {
        Settings valid = SettingsValidator.Validate(new Settings { PrimaryHex = input, SecondaryHex = input });
        Assert.Equal(expected, valid.PrimaryHex);
        Assert.Equal(expected, valid.SecondaryHex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("7C5CFF")]
    [InlineData("##7C5CFF")]
    [InlineData("#7C5CF")]
    [InlineData("#7C5CFF0")]
    [InlineData("#7C5CFF80")]
    [InlineData("#ABCD")]
    [InlineData("#GGGGGG")]
    [InlineData("#12 456")]
    [InlineData("#\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("violet")]
    [InlineData(null)]
    public void InvalidColorsTakeTheirOwnDefault(string? input)
    {
        Settings valid = SettingsValidator.Validate(new Settings { PrimaryHex = input!, SecondaryHex = input! });
        Assert.Equal("#7C5CFF", valid.PrimaryHex);
        Assert.Equal("#22D3EE", valid.SecondaryHex);
    }

    [Fact]
    public void NullStringsAndListsTakeTheDefault()
    {
        Settings valid = SettingsValidator.Validate(new Settings { ToggleHotkey = null!, CustomMonitorIds = null! });
        Assert.Equal("Ctrl+Alt+L", valid.ToggleHotkey);
        Assert.NotNull(valid.CustomMonitorIds);
        Assert.Empty(valid.CustomMonitorIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Ctrl+Alt+L")]
    [InlineData("ctrl+alt+l")]
    [InlineData("  Ctrl + Alt + L  ")]
    [InlineData("Ctrl+Alt+Shift+Win+F13")]
    [InlineData("Banana+Q")]
    [InlineData("Strg+Alt+L")]
    [InlineData("+")]
    [InlineData("\u266B")]
    public void HotkeyIsOpaqueAndOnlyNullIsReplaced(string hotkey)
    {
        // H-009 item 1: Platform parses the hotkey and reports failures; Core never rejects or rewrites it.
        Assert.Same(hotkey, SettingsValidator.Validate(new Settings { ToggleHotkey = hotkey }).ToggleHotkey);
    }

    [Fact]
    public void MonitorIdsDropOnlyNullAndEmptyEntries()
    {
        // H-009 item 2: opaque DeviceIDs, compared ordinally; unplugged monitors stay selected.
        const string unplugged = @"\\?\DISPLAY#GSM5B09#4&1a2b3c4d&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        Settings valid = SettingsValidator.Validate(new Settings
        {
            CustomMonitorIds = [null!, "A", "", " ", "a", "A", unplugged, ""],
        });
        Assert.Equal(new[] { "A", " ", "a", "A", unplugged }, valid.CustomMonitorIds);
    }

    [Fact]
    public void CleanMonitorListIsKeptAsIs()
    {
        IReadOnlyList<string> ids = ["DISPLAY1", "DISPLAY2"];
        Assert.Same(ids, SettingsValidator.Validate(new Settings { CustomMonitorIds = ids }).CustomMonitorIds);
    }

    [Fact]
    public void CleanedMonitorListCantBeChangedThroughACast()
    {
        Settings valid = SettingsValidator.Validate(new Settings { CustomMonitorIds = ["", "A"] });
        Assert.Throws<NotSupportedException>(() => ((IList<string>)valid.CustomMonitorIds).Add("B"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void VersionBecomesTheCurrentSchemaVersion(int version)
    {
        Assert.Equal(SettingsMigrator.CurrentVersion, SettingsValidator.Validate(new Settings { Version = version }).Version);
    }

    [Fact]
    public void MessySettingsAreRepairedFieldByField()
    {
        Settings messy = SettingsTestData.Messy() with { Enabled = false, Sensitivity = 1.5f };
        Settings valid = SettingsValidator.Validate(messy);
        Assert.Equal(AnimationMode.MusicSync, valid.Animation);
        Assert.Equal("#7C5CFF", valid.PrimaryHex);
        Assert.Equal("#22D3EE", valid.SecondaryHex);
        Assert.Equal(0.6f, valid.PrimaryRatio);
        Assert.Equal(0f, valid.CoreThicknessDip);
        Assert.Equal(0.45f, valid.Glow);
        Assert.Equal(1f, valid.Brightness);
        Assert.Equal(new[] { "A" }, valid.CustomMonitorIds);
        Assert.Equal(60, valid.FpsCap);
        Assert.Equal("Ctrl+Alt+L", valid.ToggleHotkey);
        // Valid fields survive next to invalid ones.
        Assert.False(valid.Enabled);
        Assert.Equal(1.5f, valid.Sensitivity);
        // Idempotent.
        Assert.Same(valid, SettingsValidator.Validate(valid));
    }

    [Fact]
    public void NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsValidator.Validate(null!));
    }

    private static Settings With(string field, float value) => field switch
    {
        nameof(Settings.PrimaryRatio) => new Settings { PrimaryRatio = value },
        nameof(Settings.CoreThicknessDip) => new Settings { CoreThicknessDip = value },
        nameof(Settings.Glow) => new Settings { Glow = value },
        nameof(Settings.Brightness) => new Settings { Brightness = value },
        nameof(Settings.Sensitivity) => new Settings { Sensitivity = value },
        nameof(Settings.CornerRadiusDip) => new Settings { CornerRadiusDip = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static float Get(Settings settings, string field) =>
        (float)typeof(Settings).GetProperty(field)!.GetValue(settings)!;
}
