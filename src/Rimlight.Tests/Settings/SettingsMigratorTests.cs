using System.Text.Json;
using Rimlight.Core;
using Rimlight.Core.SettingsStorage;
using Xunit;

namespace Rimlight.Tests.SettingsStorage;

// C7: the version-migration scaffold. Version 1 is the only real schema, so these tests build pretend histories.
public sealed class SettingsMigratorTests : IDisposable
{
    private readonly TempDirectory dir = new();

    public void Dispose() => dir.Dispose();

    [Fact]
    public void ThisBuildWritesVersionOneWithNoSteps()
    {
        Assert.Equal(1, SettingsMigrator.CurrentVersion);
        Assert.Equal(SettingsMigrator.CurrentVersion, SettingsMigrator.Default.LatestVersion);
        Assert.Equal(SettingsMigrator.CurrentVersion, new Settings().Version);
    }

    [Fact]
    public void StepTableMustCoverEveryVersionInOrder()
    {
        static MigrationStep Step(int from) => new(from, _ => { });
        Assert.NotNull(new SettingsMigrator(3, [Step(1), Step(2)]));
        Assert.Throws<ArgumentException>(() => new SettingsMigrator(3, [Step(1)]));                     // gap
        Assert.Throws<ArgumentException>(() => new SettingsMigrator(3, [Step(2), Step(1)]));            // order
        Assert.Throws<ArgumentException>(() => new SettingsMigrator(3, [Step(1), Step(1)]));            // duplicate
        Assert.Throws<ArgumentException>(() => new SettingsMigrator(2, [Step(1), Step(2)]));            // extra
        Assert.Throws<ArgumentException>(() => new SettingsMigrator(2, [new MigrationStep(1, null!)])); // no body
        Assert.Throws<ArgumentOutOfRangeException>(() => new SettingsMigrator(0, []));
    }

    [Theory]
    [InlineData(-1, "1,2,3")]
    [InlineData(0, "1,2,3")]
    [InlineData(1, "1,2,3")]
    [InlineData(2, "2,3")]
    [InlineData(3, "3")]
    [InlineData(4, "")]
    [InlineData(9, "")]
    public void StepsRunInOrderFromTheFileVersion(int fileVersion, string expectedSteps)
    {
        var ran = new List<int>();
        var migrator = new SettingsMigrator(4, [new(1, _ => ran.Add(1)), new(2, _ => ran.Add(2)), new(3, _ => ran.Add(3))]);
        int version = migrator.Migrate(new Dictionary<string, JsonElement>(), fileVersion);
        Assert.Equal(expectedSteps, string.Join(",", ran));
        Assert.Equal(Math.Max(fileVersion, 4), version);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("2.0", 2)]
    [InlineData("2e0", 2)]
    [InlineData("0.2e1", 2)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("99999999999", int.MaxValue)] // from a far newer build, never "version 1"
    [InlineData("1e400", int.MaxValue)]
    [InlineData("0", 1)]
    [InlineData("-99999999999", 1)]
    [InlineData("1.5", 1)]
    [InlineData("\"2\"", 1)]
    [InlineData("true", 1)]
    public void VersionIsAnyWholeNumberLiteral(string literal, int expected)
    {
        using JsonDocument document = JsonDocument.Parse($$"""{ "Version": {{literal}} }""");
        var fields = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected, SettingsMigrator.ReadVersion(fields));
    }

    [Fact]
    public void FileFromAFarNewerBuildIsNotMigrated()
    {
        var migrator = new SettingsMigrator(2, [new MigrationStep(1, fields => fields.Remove("glow"))]);
        dir.Create().Write("""{ "version": 99999999999, "glow": 0.7 }""");
        Assert.Equal(0.7f, new JsonSettingsStore(dir.Path, migrator).Load().Glow);

        dir.Write("""{ "version": 1.0, "glow": 0.7 }""");
        Assert.Equal(0.45f, new JsonSettingsStore(dir.Path, migrator).Load().Glow); // version 1: the step ran
    }

    [Fact]
    public void StoreMigratesOlderFilesBeforeReadingThem()
    {
        // A pretend history: version 1 stored "glowPercent" (0..100); version 2 renamed it to "glow" (0..1).
        var migrator = new SettingsMigrator(2, [new MigrationStep(1, fields =>
        {
            if (fields.Remove("glowPercent", out JsonElement percent) && percent.TryGetSingle(out float value))
            {
                fields["glow"] = JsonSerializer.SerializeToElement(value / 100f);
            }
        })]);
        var store = new JsonSettingsStore(dir.Path, migrator);

        dir.Create().Write("""{ "version": 1, "GlowPercent": 70, "brightness": 0.5 }""");
        Settings loaded = store.Load();
        Assert.Equal(0.7f, loaded.Glow, 6);
        Assert.Equal(0.5f, loaded.Brightness);
        Assert.Equal(2, loaded.Version);

        // A file without a version is read as version 1, so it is migrated too.
        dir.Write("""{ "glowPercent": 30 }""");
        Assert.Equal(0.3f, store.Load().Glow, 6);

        // A current file is not touched by the step.
        dir.Write("""{ "version": 2, "glowPercent": 70, "glow": 0.2 }""");
        Assert.Equal(0.2f, store.Load().Glow);

        // Saves write the newest version.
        store.Save(loaded);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(dir.SettingsFile));
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void FailingStepMakesLoadFallBackInsteadOfThrowing()
    {
        var migrator = new SettingsMigrator(2, [new MigrationStep(1, _ => throw new InvalidOperationException("bug in a step"))]);
        dir.Create().Write("""{ "version": 1, "glow": 0.7 }""");
        Settings loaded = new JsonSettingsStore(dir.Path, migrator).Load();
        SettingsTestData.AssertSameValues(new Settings(), loaded);
        Assert.True(File.Exists(dir.BadFile));
    }
}
