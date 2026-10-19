using System.Text;
using Rimlight.Core;

namespace Rimlight.Native.Tests;

// The exports through the same unmanaged function pointers the Swift app calls, so the marshalling and the guards are
// what's tested, not only the managed methods behind them.
public sealed unsafe class ExportsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rimlight-native-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void AbiVersionIsFour()
    {
        delegate* unmanaged<int> abi = &Exports.AbiVersion;
        Assert.Equal(4, abi());
    }

    [Fact]
    public void AppNameIsAppInfoName()
    {
        delegate* unmanaged<byte*, int, int> name = &Exports.AppName;
        Assert.Equal(AppInfo.Name, ReadString(name));
    }

    [Fact]
    public void VersionIsTheBuildPropsVersionWithoutTheSourceRevision()
    {
        delegate* unmanaged<byte*, int, int> version = &Exports.Version;
        string text = ReadString(version);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", text);
        Assert.Equal(NativeApi.Version, text);
    }

    [Fact]
    public void AStringIsWrittenOnlyWhenTheBufferHasRoomForItsNul()
    {
        delegate* unmanaged<byte*, int, int> name = &Exports.AppName;
        int length = Encoding.UTF8.GetByteCount(AppInfo.Name);
        byte* buffer = stackalloc byte[64];
        new Span<byte>(buffer, 64).Fill(0xAA);

        Assert.Equal(length, name(null, 0));          // a length query
        Assert.Equal(length, name(buffer, length));   // no room for the NUL: nothing written
        Assert.Equal(0xAA, buffer[0]);
        Assert.Equal(length, name(buffer, length + 1));
        Assert.Equal(0, buffer[length]);
        Assert.Equal(NativeStatus.InvalidArgument, name(buffer, -1));
        Assert.Equal(NativeStatus.InvalidArgument, name(null, 8));
    }

    [Fact]
    public void LoadingFromAnEmptyDirectoryGivesTheDefaults()
    {
        Settings loaded = Load();
        Assert.Equal(new Settings().Glow, loaded.Glow);
        Assert.Equal(new Settings().ToggleHotkey, loaded.ToggleHotkey);
        Assert.False(File.Exists(Path.Combine(directory, "settings.json"))); // loading never writes
    }

    [Fact]
    public void SettingsRoundTripThroughTheFile()
    {
        string json = SettingsTransport.Serialize(new Settings
        {
            Glow = 0.7f,
            Animation = AnimationMode.IdleGlow,
            PrimaryHex = "#FF8A2A",
            CustomMonitorIds = ["37D8832A-2D66-02CA-B9F7-8F30A301B230"],
            ToggleHotkey = "Ctrl+Win+G",
        });
        Assert.Equal(NativeStatus.Ok, Save(json));

        Settings loaded = Load();
        Assert.Equal(0.7f, loaded.Glow);
        Assert.Equal(AnimationMode.IdleGlow, loaded.Animation);
        Assert.Equal("#FF8A2A", loaded.PrimaryHex);
        Assert.Equal(["37D8832A-2D66-02CA-B9F7-8F30A301B230"], loaded.CustomMonitorIds);
        Assert.Equal("Ctrl+Win+G", loaded.ToggleHotkey);
    }

    [Fact]
    public void TheTransportUsesTheFileFormatsNames()
    {
        string json = SettingsTransport.Serialize(new Settings());
        Assert.Contains("\"coreThicknessDip\":6", json);
        Assert.Contains("\"animation\":\"MusicSync\"", json);
        Assert.Contains("\"customMonitorIds\":[]", json);
    }

    [Fact]
    public void SavingValidatesThroughCoresStore()
    {
        Assert.Equal(NativeStatus.Ok, Save("""{ "glow": 7, "primaryHex": "red", "fpsCap": 75 }"""));
        Settings loaded = Load();
        Assert.Equal(1f, loaded.Glow);                     // clamped
        Assert.Equal(new Settings().PrimaryHex, loaded.PrimaryHex); // not #RRGGBB: the default
        Assert.Equal(60, loaded.FpsCap);
    }

    [Fact]
    public void FieldsLeftOutKeepTheirDefaults()
    {
        Assert.Equal(NativeStatus.Ok, Save("""{ "Glow": 0.7 }"""));
        Settings loaded = Load();
        Assert.Equal(0.7f, loaded.Glow);
        Settings defaults = new();
        Assert.True(loaded.Enabled);
        Assert.Equal(defaults.Brightness, loaded.Brightness);
        Assert.Equal(defaults.CoreThicknessDip, loaded.CoreThicknessDip);
        Assert.Equal(defaults.FpsCap, loaded.FpsCap);
        Assert.Equal(defaults.LaunchAtStartup, loaded.LaunchAtStartup);
        Assert.Equal(defaults.ToggleHotkey, loaded.ToggleHotkey);
    }

    [Fact]
    public void BadInputIsAStatusNeverAnException()
    {
        Assert.Equal(NativeStatus.BadJson, Save("not json"));
        Assert.Equal(NativeStatus.BadJson, Save("[1, 2]"));
        Assert.Equal(NativeStatus.BadJson, Save("""{ "animation": "Sideways" }"""));

        delegate* unmanaged<byte*, byte*, int> save = &Exports.SettingsSave;
        delegate* unmanaged<byte*, byte*, int, int> load = &Exports.SettingsLoad;
        Assert.Equal(NativeStatus.InvalidArgument, save(null, null));
        Assert.Equal(NativeStatus.InvalidArgument, load(null, null, 0));
        byte* invalidUtf8 = stackalloc byte[] { 0xC3, 0x28, 0 };
        Assert.Equal(NativeStatus.InvalidArgument, load(invalidUtf8, null, 0));
    }

    [Fact]
    public void AnUnwritableDirectoryIsAnIoError()
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "a-file");
        File.WriteAllText(file, "");
        // A directory path that runs through a file can't be created on any platform.
        Assert.Equal(NativeStatus.IoError, Save("{}", Path.Combine(file, "settings")));
    }

    private Settings Load()
    {
        delegate* unmanaged<byte*, byte*, int, int> load = &Exports.SettingsLoad;
        byte[] dir = Utf8Z(directory);
        int length;
        fixed (byte* d = dir) length = load(d, null, 0);
        Assert.True(length > 0);
        byte[] buffer = new byte[length + 1];
        fixed (byte* d = dir)
        fixed (byte* b = buffer)
            Assert.Equal(length, load(d, b, buffer.Length));
        Settings? settings = SettingsTransport.Deserialize(Encoding.UTF8.GetString(buffer, 0, length));
        Assert.NotNull(settings);
        return settings;
    }

    private int Save(string json, string? dir = null)
    {
        delegate* unmanaged<byte*, byte*, int> save = &Exports.SettingsSave;
        byte[] d = Utf8Z(dir ?? directory);
        byte[] j = Utf8Z(json);
        fixed (byte* dp = d)
        fixed (byte* jp = j)
            return save(dp, jp);
    }

    private static string ReadString(delegate* unmanaged<byte*, int, int> call)
    {
        int length = call(null, 0);
        Assert.True(length >= 0);
        byte[] buffer = new byte[length + 1];
        fixed (byte* b = buffer) Assert.Equal(length, call(b, buffer.Length));
        Assert.Equal(0, buffer[length]);
        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static byte[] Utf8Z(string text) => Encoding.UTF8.GetBytes(text + "\0");
}
