using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Rimlight.Core;
using Rimlight.Core.SettingsStorage;
using Xunit;

namespace Rimlight.Tests.SettingsStorage;

// C7: settings.json (doc 02 "Settings storage", doc 06 §1). Every test uses its own temp directory.
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory dir = new();

    public void Dispose() => dir.Dispose();

    [Fact]
    public void PathIsSettingsJsonInTheFullDirectory()
    {
        var store = new JsonSettingsStore(dir.Path);
        Assert.Equal(dir.SettingsFile, store.Path);
        Assert.Equal(dir.BadFile, store.BadPath);

        var relative = new JsonSettingsStore("relative-dir");
        Assert.True(System.IO.Path.IsPathFullyQualified(relative.Path));
        Assert.EndsWith(System.IO.Path.Combine("relative-dir", "settings.json"), relative.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DirectoryIsRequired(string? directory)
    {
        Assert.ThrowsAny<ArgumentException>(() => new JsonSettingsStore(directory!));
    }

    [Fact]
    public void MissingFileGivesDefaultsAndTouchesNothing()
    {
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        SettingsTestData.AssertSameValues(new Settings(), loaded);
        Assert.False(Directory.Exists(dir.Path));

        dir.Create();
        SettingsTestData.AssertSameValues(new Settings(), new JsonSettingsStore(dir.Path).Load());
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void RoundTripKeepsEveryField()
    {
        Settings custom = SettingsTestData.FullyCustom();
        new JsonSettingsStore(dir.Path).Save(custom);
        SettingsTestData.AssertSameValues(custom, new JsonSettingsStore(dir.Path).Load());
    }

    [Fact]
    public void RoundTripThroughTheSameStore()
    {
        var store = new JsonSettingsStore(dir.Path);
        store.Save(new Settings { Glow = 0.7f });
        Assert.Equal(0.7f, store.Load().Glow);
        store.Save(new Settings { Glow = 0.2f });
        Assert.Equal(0.2f, store.Load().Glow);
    }

    [Fact]
    public void SaveCreatesMissingDirectories()
    {
        string nested = System.IO.Path.Combine(dir.Path, "a", "b");
        var store = new JsonSettingsStore(nested);
        store.Save(new Settings());
        Assert.True(File.Exists(store.Path));
    }

    [Fact]
    public void FileIsIndentedCamelCaseJsonWithEnumNames()
    {
        new JsonSettingsStore(dir.Path).Save(SettingsTestData.FullyCustom());
        byte[] bytes = File.ReadAllBytes(dir.SettingsFile);
        Assert.Equal((byte)'{', bytes[0]); // no BOM
        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\n  \"version\": 1,", text.ReplaceLineEndings("\n"));
        // Hand-editable: '+' and '&' are written as they are, not as \u escapes.
        Assert.Contains("\"toggleHotkey\": \"Win+Shift+F9\"", text);
        Assert.Contains(@"""\\\\?\\DISPLAY#DEL40F6#5&2f3a1b2c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}""", text);
        Assert.DoesNotContain(@"\u", text);
        Assert.Contains("\"toggleHotkey\": \"Ctrl+Alt+L\"", Encoding.UTF8.GetString(SettingsJson.Serialize(new Settings())));

        using JsonDocument json = JsonDocument.Parse(bytes);
        string[] names = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        string[] expected = SettingsTestData.Properties.Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToArray();
        Assert.Equal(expected, names);
        Assert.Equal("IdleGlow", json.RootElement.GetProperty("animation").GetString());
        Assert.Equal("Hide", json.RootElement.GetProperty("whenSilent").GetString());
        Assert.Equal("Manual", json.RootElement.GetProperty("colorMode").GetString());
        Assert.Equal("Custom", json.RootElement.GetProperty("monitors").GetString());
        Assert.Equal("Pause", json.RootElement.GetProperty("onBattery").GetString());
        Assert.Equal(0.9, json.RootElement.GetProperty("glow").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("customMonitorIds").ValueKind);
    }

    [Fact]
    public void SaveValidatesBeforeWriting()
    {
        new JsonSettingsStore(dir.Path).Save(SettingsTestData.Messy() with { Version = 7 });
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(dir.SettingsFile));
        JsonElement root = json.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("MusicSync", root.GetProperty("animation").GetString());
        Assert.Equal("#7C5CFF", root.GetProperty("primaryHex").GetString());
        Assert.Equal("#22D3EE", root.GetProperty("secondaryHex").GetString());
        Assert.Equal(0.6, root.GetProperty("primaryRatio").GetDouble(), 6);
        Assert.Equal(0.45, root.GetProperty("glow").GetDouble(), 6);
        Assert.Equal(1, root.GetProperty("brightness").GetDouble());
        Assert.Equal(0.25, root.GetProperty("sensitivity").GetDouble());
        Assert.Equal(60, root.GetProperty("fpsCap").GetInt32());
        Assert.Equal("Ctrl+Alt+L", root.GetProperty("toggleHotkey").GetString());
        Assert.Equal(new[] { "A" }, root.GetProperty("customMonitorIds").EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public void SaveNullThrowsAndWritesNothing()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonSettingsStore(dir.Path).Save(null!));
        Assert.False(Directory.Exists(dir.Path));
    }

    [Fact]
    public void OutOfRangeValuesInTheFileAreClamped()
    {
        dir.Create().Write("""
            {
              "version": 1,
              "glow": 7,
              "brightness": -3,
              "primaryRatio": 0.95,
              "coreThicknessDip": 1e39,
              "sensitivity": -1e400,
              "cornerRadiusDip": 41,
              "fpsCap": 144,
              "primaryHex": "#abc",
              "secondaryHex": "cyan"
            }
            """);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        Assert.Equal(1f, loaded.Glow);
        Assert.Equal(0.1f, loaded.Brightness);
        Assert.Equal(0.9f, loaded.PrimaryRatio);
        Assert.Equal(40f, loaded.CoreThicknessDip); // finite in JSON, though past float's range: clamped, not reset
        Assert.Equal(0.25f, loaded.Sensitivity);    // past double's range too
        Assert.Equal(40f, loaded.CornerRadiusDip);
        Assert.Equal(60, loaded.FpsCap);
        Assert.Equal("#AABBCC", loaded.PrimaryHex);
        Assert.Equal("#22D3EE", loaded.SecondaryHex);
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void WrongTypesFallBackFieldByField()
    {
        dir.Create().Write("""
            {
              "glow": "bright",
              "enabled": "false",
              "fpsCap": 30.5,
              "animation": 1,
              "onBattery": "Sometimes",
              "whenSilent": "Hide, IdleGlow",
              "monitors": "2",
              "primaryHex": 8150271,
              "toggleHotkey": null,
              "customMonitorIds": "DISPLAY1",
              "coverTaskbar": 0,
              "brightness": 0.3,
              "colorMode": "manual",
              "autoUpdate": false
            }
            """);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        var d = new Settings();
        Assert.Equal(d.Glow, loaded.Glow);
        Assert.Equal(d.Enabled, loaded.Enabled);
        Assert.Equal(d.FpsCap, loaded.FpsCap);
        Assert.Equal(d.Animation, loaded.Animation);
        Assert.Equal(d.OnBattery, loaded.OnBattery);
        Assert.Equal(d.WhenSilent, loaded.WhenSilent);
        Assert.Equal(d.Monitors, loaded.Monitors);
        Assert.Equal(d.PrimaryHex, loaded.PrimaryHex);
        Assert.Equal(d.ToggleHotkey, loaded.ToggleHotkey);
        Assert.Empty(loaded.CustomMonitorIds);
        Assert.Equal(d.CoverTaskbar, loaded.CoverTaskbar);
        // The valid fields next to them are kept, and the file is not treated as corrupt.
        Assert.Equal(0.3f, loaded.Brightness);
        Assert.Equal(ColorMode.Manual, loaded.ColorMode);
        Assert.False(loaded.AutoUpdate);
        Assert.False(File.Exists(dir.BadFile));
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("30.0", 30)]
    [InlineData("3e1", 30)]
    [InlineData("1.2E+2", 120)]
    [InlineData("-0.0", 0)]
    [InlineData("30.5", 60)]
    [InlineData("3e10", 60)]
    [InlineData("1e400", 60)]
    public void FpsCapAcceptsAnyWholeNumberLiteral(string literal, int expected)
    {
        dir.Create().Write($$"""{ "fpsCap": {{literal}} }""");
        Assert.Equal(expected, new JsonSettingsStore(dir.Path).Load().FpsCap);
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void HotkeyAndMonitorIdsAreReadOpaquely()
    {
        dir.Create().Write("""
            {
              "toggleHotkey": "",
              "customMonitorIds": [null, "", " ", "B", "b", 5, true, ["x"], "B"]
            }
            """);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        Assert.Equal("", loaded.ToggleHotkey); // empty = no hotkey (H-009)
        Assert.Equal(new[] { " ", "B", "b", "B" }, loaded.CustomMonitorIds);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        dir.Create().Write("""
            {
              "futureFeature": { "nested": [1, 2, { "deep": null }] },
              "glow": 0.7,
              "x": null,
              "theme": "Dark"
            }
            """);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        SettingsTestData.AssertSameValues(new Settings { Glow = 0.7f }, loaded);
        Assert.False(File.Exists(dir.BadFile));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "glow": 0.7, "animation": "Off" }""")]
    [InlineData("""{ "version": 1, "customMonitorIds": ["M1"], "monitors": "Custom" }""")]
    public void MissingFieldsTakeTheirDefaults(string json)
    {
        dir.Create().Write(json);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        using JsonDocument document = JsonDocument.Parse(json);
        // Exactly the fields in the file (other than version) differ from the defaults.
        string[] present = document.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .Where(n => n != "version")
            .Select(n => char.ToUpperInvariant(n[0]) + n[1..])
            .Order()
            .ToArray();
        Assert.Equal(present, SettingsTestData.ChangedFields(new Settings(), loaded));
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void PropertyNamesAndEnumNamesIgnoreCaseAndTheLastDuplicateWins()
    {
        dir.Create().Write("""{ "Glow": 0.2, "glow": 0.7, "GLOW": 0.8, "ANIMATION": "idleglow", "FpsCap": 30 }""");
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        Assert.Equal(0.8f, loaded.Glow);
        Assert.Equal(AnimationMode.IdleGlow, loaded.Animation);
        Assert.Equal(30, loaded.FpsCap);
    }

    [Fact]
    public void CommentsTrailingCommasAndBomAreAccepted()
    {
        dir.Create();
        string json = """
            {
              // hand-edited
              "glow": 0.7, /* softer */
              "customMonitorIds": ["A",],
            }
            """;
        File.WriteAllBytes(dir.SettingsFile, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        Assert.Equal(0.7f, loaded.Glow);
        Assert.Equal(new[] { "A" }, loaded.CustomMonitorIds);
        Assert.False(File.Exists(dir.BadFile));
    }

    public static TheoryData<byte[]> CorruptFiles => new()
    {
        Array.Empty<byte>(),
        Encoding.UTF8.GetBytes("   \n"),
        Encoding.UTF8.GetBytes("not json"),
        Encoding.UTF8.GetBytes("""{ "glow": 0.7"""), // truncated: a partial write by some other tool
        Encoding.UTF8.GetBytes("""{ "glow": 0.7 }}"""),
        Encoding.UTF8.GetBytes("""{ "glow": 0.7 } { "glow": 0.2 }"""),
        Encoding.UTF8.GetBytes("[]"),
        Encoding.UTF8.GetBytes("null"),
        Encoding.UTF8.GetBytes("42"),
        Encoding.UTF8.GetBytes("\"settings\""),
        new byte[] { (byte)'{', (byte)'"', (byte)'g', 0xFF, 0xFE, (byte)'"', (byte)':', (byte)'1', (byte)'}' }, // invalid UTF-8 name
        Encoding.UTF8.GetBytes("""{ "toggleHotkey": "x" }""").Select(b => b == (byte)'x' ? (byte)0xFF : b).ToArray(), // invalid UTF-8 value
        new byte[] { 0xFF, 0xFE, (byte)'{', 0, (byte)'}', 0 }, // UTF-16
    };

    [Theory]
    [MemberData(nameof(CorruptFiles))]
    public void CorruptFileIsBackedUpAndDefaultsAreReturned(byte[] contents)
    {
        dir.Create();
        File.WriteAllBytes(dir.SettingsFile, contents);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        SettingsTestData.AssertSameValues(new Settings(), loaded);
        Assert.Equal(contents, File.ReadAllBytes(dir.BadFile));
        Assert.Equal(contents, File.ReadAllBytes(dir.SettingsFile)); // left in place until the next save
    }

    [Fact]
    public void BackupOverwritesAnOlderBackupAndTheNextSaveReplacesTheBadFile()
    {
        dir.Create();
        File.WriteAllText(dir.BadFile, "older backup");
        dir.Write("{ broken");
        var store = new JsonSettingsStore(dir.Path);
        Assert.Equal(0.45f, store.Load().Glow);
        Assert.Equal("{ broken", File.ReadAllText(dir.BadFile));

        store.Save(new Settings { Glow = 0.7f });
        Assert.Equal(0.7f, new JsonSettingsStore(dir.Path).Load().Glow);
        Assert.Equal("{ broken", File.ReadAllText(dir.BadFile));
    }

    [Fact]
    public void OversizedFileIsTreatedAsCorrupt()
    {
        dir.Create();
        string json = """{ "glow": 0.7 }""";
        dir.Write(json.PadRight(JsonSettingsStore.MaxFileBytes));
        Assert.Equal(0.7f, new JsonSettingsStore(dir.Path).Load().Glow); // exactly at the limit: read
        Assert.False(File.Exists(dir.BadFile));

        dir.Write(json.PadRight(JsonSettingsStore.MaxFileBytes + 1));
        Assert.Equal(0.45f, new JsonSettingsStore(dir.Path).Load().Glow);
        Assert.Equal(JsonSettingsStore.MaxFileBytes + 1, new FileInfo(dir.BadFile).Length);
    }

    [Fact]
    public void SettingsPathThatIsADirectoryGivesDefaults()
    {
        Directory.CreateDirectory(dir.SettingsFile);
        var store = new JsonSettingsStore(dir.Path);
        SettingsTestData.AssertSameValues(new Settings(), store.Load());
        Assert.ThrowsAny<IOException>(() => store.Save(new Settings()));
        Assert.Empty(dir.TempFiles());
    }

    [Fact]
    public void NewerVersionLoadsWhatItKnowsAndSaveWritesTheCurrentVersion()
    {
        dir.Create().Write("""{ "version": 9, "glow": 0.7, "glowFalloff": "quadratic", "animation": "Rainbow" }""");
        var store = new JsonSettingsStore(dir.Path);
        Settings loaded = store.Load();
        Assert.Equal(0.7f, loaded.Glow);
        Assert.Equal(AnimationMode.MusicSync, loaded.Animation); // a newer enum value this build doesn't know
        Assert.Equal(SettingsMigrator.CurrentVersion, loaded.Version);
        Assert.False(File.Exists(dir.BadFile));

        store.Save(loaded);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(dir.SettingsFile));
        Assert.Equal(SettingsMigrator.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("glowFalloff", out _));
        Assert.Equal(0.7, json.RootElement.GetProperty("glow").GetDouble(), 6);
    }

    [Theory]
    [InlineData("""{ "glow": 0.7 }""")]
    [InlineData("""{ "version": "one", "glow": 0.7 }""")]
    [InlineData("""{ "version": 0, "glow": 0.7 }""")]
    [InlineData("""{ "version": -4, "glow": 0.7 }""")]
    [InlineData("""{ "version": 1.5, "glow": 0.7 }""")]
    [InlineData("""{ "version": 1.0, "glow": 0.7 }""")]
    [InlineData("""{ "version": -1e10, "glow": 0.7 }""")]
    [InlineData("""{ "version": null, "glow": 0.7 }""")]
    public void MissingOrOddVersionsReadAsVersionOne(string json)
    {
        dir.Create().Write(json);
        Settings loaded = new JsonSettingsStore(dir.Path).Load();
        Assert.Equal(0.7f, loaded.Glow);
        Assert.Equal(1, loaded.Version);
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void FailedWriteKeepsThePreviousFile()
    {
        var store = new JsonSettingsStore(dir.Path);
        store.Save(SettingsTestData.FullyCustom());
        byte[] before = File.ReadAllBytes(dir.SettingsFile);

        store.WrapTempStream = file => new FailingStream(file, failAfterBytes: 40);
        Assert.Throws<IOException>(() => store.Save(new Settings { Glow = 0.1f }));

        Assert.Equal(before, File.ReadAllBytes(dir.SettingsFile));
        Assert.Empty(dir.TempFiles());
        Assert.Equal(new[] { dir.SettingsFile }, Directory.GetFiles(dir.Path));
        SettingsTestData.AssertSameValues(SettingsTestData.FullyCustom(), new JsonSettingsStore(dir.Path).Load());

        // The store keeps working once the disk does.
        store.WrapTempStream = null;
        store.Save(new Settings { Glow = 0.1f });
        Assert.Equal(0.1f, store.Load().Glow);
    }

    [Fact]
    public void FailedFirstWriteLeavesNoFile()
    {
        var store = new JsonSettingsStore(dir.Path) { WrapTempStream = file => new FailingStream(file, failAfterBytes: 0) };
        Assert.Throws<IOException>(() => store.Save(new Settings()));
        Assert.False(File.Exists(dir.SettingsFile));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void FailureRightBeforeTheSwapKeepsThePreviousFile()
    {
        var store = new JsonSettingsStore(dir.Path);
        store.Save(new Settings { Glow = 0.2f });
        byte[] before = File.ReadAllBytes(dir.SettingsFile);
        Settings? staged = null;
        store.BeforeCommit = temp =>
        {
            // The temporary file is complete and flushed, in the same directory, and the target is still the old one.
            Assert.Equal(dir.Path, System.IO.Path.GetDirectoryName(temp));
            staged = SettingsJson.Deserialize(File.ReadAllBytes(temp), SettingsMigrator.Default);
            Assert.Equal(before, File.ReadAllBytes(dir.SettingsFile));
            throw new IOException("The process cannot access the file because it is being used by another process.");
        };

        Assert.Throws<IOException>(() => store.Save(new Settings { Glow = 0.9f }));
        Assert.Equal(0.9f, staged!.Glow);
        Assert.Equal(before, File.ReadAllBytes(dir.SettingsFile));
        Assert.Empty(dir.TempFiles());
    }

    [Fact]
    public void ReplaceThatRemovedTheOldFileThenFailedLeavesTheNewFile()
    {
        // ReplaceFile's ERROR_UNABLE_TO_MOVE_REPLACEMENT: settings.json is already gone, the new contents are only in
        // the temporary file, and .NET throws. Deleting that file would lose every setting.
        var store = new JsonSettingsStore(dir.Path);
        store.Save(new Settings { Glow = 0.2f });
        store.BeforeCommit = _ =>
        {
            File.Delete(dir.SettingsFile);
            throw new IOException("Unable to move the replacement file to the file to be replaced.");
        };

        Assert.Throws<IOException>(() => store.Save(SettingsTestData.FullyCustom()));
        SettingsTestData.AssertSameValues(SettingsTestData.FullyCustom(), new JsonSettingsStore(dir.Path).Load());
        Assert.Empty(dir.TempFiles());
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void ReplaceThatRemovedTheOldFileWritesTheNewOneWhenTheTemporaryFileCantBeMoved()
    {
        // As above, but the temporary file can't be renamed either (on Windows, whatever broke the swap may still
        // hold it): the same bytes are written in place directly.
        var store = new JsonSettingsStore(dir.Path);
        store.Save(new Settings { Glow = 0.2f });
        store.BeforeCommit = temp =>
        {
            File.Delete(dir.SettingsFile);
            File.Delete(temp);
            throw new IOException("Unable to move the replacement file to the file to be replaced.");
        };

        Assert.Throws<IOException>(() => store.Save(SettingsTestData.FullyCustom()));
        SettingsTestData.AssertSameValues(SettingsTestData.FullyCustom(), new JsonSettingsStore(dir.Path).Load());
        Assert.Empty(dir.TempFiles());
    }

    [Fact]
    public void TemporaryFileIsKeptWhenItIsTheLastCopy()
    {
        var store = new JsonSettingsStore(dir.Path);
        store.Save(new Settings { Glow = 0.2f });
        string? staged = null;
        store.BeforeCommit = temp =>
        {
            staged = temp;
            File.Delete(dir.SettingsFile);
            Directory.CreateDirectory(dir.SettingsFile); // nothing can be put in its place now
            throw new IOException("Unable to move the replacement file to the file to be replaced.");
        };

        Assert.Throws<IOException>(() => store.Save(SettingsTestData.FullyCustom()));
        Assert.True(File.Exists(staged));
        SettingsTestData.AssertSameValues(SettingsTestData.FullyCustom(), SettingsJson.Deserialize(File.ReadAllBytes(staged!), SettingsMigrator.Default)!);
    }

    [Fact]
    public void PartialTemporaryFileNeverTakesTheSettingsFilesPlace()
    {
        var store = new JsonSettingsStore(dir.Path);
        store.Save(new Settings { Glow = 0.2f });
        store.WrapTempStream = file =>
        {
            File.Delete(dir.SettingsFile); // even with no settings file left, a half-written one is worse than none
            return new FailingStream(file, failAfterBytes: 40);
        };

        Assert.Throws<IOException>(() => store.Save(SettingsTestData.FullyCustom()));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void SuccessfulSaveDeletesTemporaryFilesLeftByACrash()
    {
        dir.Create();
        string[] leftovers =
        [
            dir.File("settings.json.0123456789abcdef0123456789abcdef.tmp"),
            dir.File("settings.json.fedcba9876543210fedcba9876543210.tmp"),
        ];
        foreach (string leftover in leftovers) File.WriteAllText(leftover, """{ "glow": 0."""); // cut off mid-write
        string[] unrelated = [dir.File("other.tmp"), dir.File("settings.json.bak"), dir.File("my-settings.json.1.tmp")];
        foreach (string file in unrelated) File.WriteAllText(file, "keep");

        var store = new JsonSettingsStore(dir.Path);
        store.WrapTempStream = file => new FailingStream(file, failAfterBytes: 0);
        Assert.Throws<IOException>(() => store.Save(new Settings { Glow = 0.7f }));
        Assert.All(leftovers, leftover => Assert.True(File.Exists(leftover))); // only a successful save cleans up

        store.WrapTempStream = null;
        store.Save(new Settings { Glow = 0.7f });
        Assert.Equal(0.7f, store.Load().Glow);
        Assert.All(leftovers, leftover => Assert.False(File.Exists(leftover)));
        Assert.All(unrelated, file => Assert.Equal("keep", File.ReadAllText(file)));
    }

    [Fact]
    public void FileLockedForAMomentIsRetriedInsteadOfTreatedAsCorrupt()
    {
        new JsonSettingsStore(dir.Path).Save(new Settings { Glow = 0.7f });
        int retries = 0;
        // Another program (a save in another process, a scanner, a backup tool) opened the file without sharing it.
        var holder = new FileStream(dir.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var store = new JsonSettingsStore(dir.Path)
        {
            BeforeReadRetry = () =>
            {
                if (++retries == JsonSettingsStore.ReadAttempts - 1) holder.Dispose(); // released before the last try
            },
        };

        try
        {
            Assert.Equal(0.7f, store.Load().Glow);
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal(JsonSettingsStore.ReadAttempts - 1, retries);
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void FileLockedForLongerGivesTheDefaultsAfterTheLastTry()
    {
        new JsonSettingsStore(dir.Path).Save(new Settings { Glow = 0.7f });
        int retries = 0;
        var store = new JsonSettingsStore(dir.Path) { BeforeReadRetry = () => retries++ };
        using (new FileStream(dir.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            SettingsTestData.AssertSameValues(new Settings(), store.Load());
        }

        Assert.Equal(JsonSettingsStore.ReadAttempts - 1, retries);
        Assert.Equal(0.7f, store.Load().Glow); // and read normally once it is free
    }

    [Fact]
    public void ConcurrentSavesAreSerializedAndTheFileIsAlwaysWhole()
    {
        var store = new JsonSettingsStore(dir.Path);
        int inside = 0, maxInside = 0;
        store.BeforeCommit = _ =>
        {
            int now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            Thread.Sleep(1); // widen the window so overlapping saves would be caught
            Interlocked.Decrement(ref inside);
        };

        const int threads = 8, savesPerThread = 15;
        var saved = new ConcurrentBag<string>();
        var errors = new ConcurrentQueue<Exception>();
        using var start = new Barrier(threads);
        Thread[] workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < savesPerThread; i++)
            {
                string hotkey = $"Ctrl+F{t}-{i}";
                try
                {
                    store.Save(new Settings { ToggleHotkey = hotkey, Glow = (t * savesPerThread + i) / 200f });
                    saved.Add(hotkey);
                }
                catch (Exception e)
                {
                    errors.Enqueue(e);
                }
            }
        })).ToArray();
        foreach (Thread worker in workers) worker.Start();
        foreach (Thread worker in workers) worker.Join();

        Assert.Empty(errors);
        Assert.Equal(1, maxInside);
        Assert.Equal(threads * savesPerThread, saved.Count);
        Assert.Contains(new JsonSettingsStore(dir.Path).Load().ToggleHotkey, saved);
        Assert.Empty(dir.TempFiles());
        Assert.False(File.Exists(dir.BadFile));
    }

    [Fact]
    public void ReaderOnAnotherStoreNeverSeesAPartialFile()
    {
        // Separate instances share no lock, like a second process: only the atomic swap keeps the reader safe.
        var writer = new JsonSettingsStore(dir.Path);
        var reader = new JsonSettingsStore(dir.Path);
        writer.Save(new Settings { ToggleHotkey = "start" });
        var valid = new ConcurrentDictionary<string, bool>();
        valid["start"] = true;
        // Windows' ReplaceFile isn't one rename, so a read there may briefly find no file and get the defaults (or
        // find it locked, which Load retries). A partial file would still fail: it can't parse, so it would leave
        // settings.bad.json behind.
        if (OperatingSystem.IsWindows()) valid[new Settings().ToggleHotkey] = true;
        var seen = new ConcurrentBag<string>();
        int writes = 0;
        using var done = new CancellationTokenSource();

        var readerThread = new Thread(() =>
        {
            while (!done.IsCancellationRequested) seen.Add(reader.Load().ToggleHotkey);
        });
        readerThread.Start();
        for (int i = 0; i < 300; i++)
        {
            string hotkey = $"Alt+{i}";
            valid[hotkey] = true;
            try
            {
                writer.Save(new Settings { ToggleHotkey = hotkey, CustomMonitorIds = Enumerable.Repeat(hotkey, i % 20).ToArray() });
                writes++;
            }
            catch (IOException) when (OperatingSystem.IsWindows())
            {
                // Windows may refuse a swap while the reader holds the file; Save then keeps the old file (documented).
            }
        }

        done.Cancel();
        readerThread.Join();
        Assert.True(writes > 0);
        Assert.NotEmpty(seen);
        Assert.All(seen, hotkey => Assert.True(valid.ContainsKey(hotkey), $"Reader saw '{hotkey}'"));
        Assert.False(File.Exists(dir.BadFile));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
