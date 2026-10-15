using System.Reflection;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Tests.SettingsStorage;

// Shared fixtures for the C7 tests. (The namespace isn't Rimlight.Tests.Settings, which would hide the Settings type.)
internal static class SettingsTestData
{
    public static readonly PropertyInfo[] Properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    // Valid, and different from the defaults in every field except Version (there is only one version so far).
    public static Settings FullyCustom() => new()
    {
        Enabled = false,
        Animation = AnimationMode.IdleGlow,
        WhenSilent = SilentBehavior.Hide,
        ColorMode = ColorMode.Manual,
        OverrideAlbumColor = true,
        PrimaryHex = "#112233",
        SecondaryHex = "#ABCDEF",
        PrimaryRatio = 0.35f,
        CoreThicknessDip = 12.5f,
        Glow = 0.9f,
        Brightness = 0.3f,
        Sensitivity = 1.75f,
        CornerRadiusDip = 8f,
        CoverTaskbar = false,
        Monitors = MonitorSelection.Custom,
        CustomMonitorIds = [@"\\?\DISPLAY#DEL40F6#5&2f3a1b2c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "id-2"],
        FpsCap = 120,
        PauseInFullscreen = false,
        OnBattery = BatteryBehavior.Pause,
        HideFromScreenCapture = true,
        LaunchAtStartup = false,
        ToggleHotkey = "Win+Shift+F9",
        AutoUpdate = false,
        FirstRunComplete = true,
    };

    // Every field invalid in a way validation must repair.
    public static Settings Messy() => new()
    {
        Animation = (AnimationMode)42,
        PrimaryHex = "violet",
        SecondaryHex = null!,
        PrimaryRatio = float.NaN,
        CoreThicknessDip = -3f,
        Glow = float.PositiveInfinity,
        Brightness = 7f,
        Sensitivity = 0f,
        CustomMonitorIds = [null!, "", "A"],
        FpsCap = 45,
        ToggleHotkey = null!,
    };

    // Field-by-field equality; record equality would compare CustomMonitorIds by reference.
    public static void AssertSameValues(Settings expected, Settings actual, params string[] except)
    {
        foreach (PropertyInfo property in Properties)
        {
            if (except.Contains(property.Name)) continue;
            object? e = property.GetValue(expected);
            object? a = property.GetValue(actual);
            if (e is IEnumerable<string> expectedList && a is IEnumerable<string> actualList)
            {
                Assert.True(expectedList.SequenceEqual(actualList), $"{property.Name}: [{string.Join(", ", expectedList)}] != [{string.Join(", ", actualList)}]");
            }
            else
            {
                Assert.True(Equals(e, a), $"{property.Name}: expected {e}, got {a}");
            }
        }
    }

    // The names of the fields whose values differ.
    public static string[] ChangedFields(Settings before, Settings after) =>
        Properties.Where(p =>
        {
            object? b = p.GetValue(before);
            object? a = p.GetValue(after);
            return b is IEnumerable<string> bl && a is IEnumerable<string> al ? !bl.SequenceEqual(al) : !Equals(b, a);
        }).Select(p => p.Name).Order().ToArray();
}

// A directory under the temp folder that is deleted afterwards. It doesn't exist until something creates it.
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rimlight-tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string SettingsFile => File("settings.json");

    public string BadFile => File("settings.bad.json");

    public TempDirectory Create()
    {
        Directory.CreateDirectory(Path);
        return this;
    }

    public void Write(string text) => System.IO.File.WriteAllText(SettingsFile, text);

    public string[] TempFiles() =>
        Directory.Exists(Path) ? Directory.GetFiles(Path, "*.tmp") : [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

// Lets a few bytes through, then fails like a full disk.
internal sealed class FailingStream(Stream inner, int failAfterBytes) : Stream
{
    private int written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        int allowed = Math.Min(buffer.Length, failAfterBytes - written);
        inner.Write(buffer[..allowed]);
        inner.Flush();
        written += allowed;
        if (allowed < buffer.Length) throw new IOException("There is not enough space on the disk.");
    }
}
