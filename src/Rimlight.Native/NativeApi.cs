using System.Reflection;
using Rimlight.Core;

namespace Rimlight.Native;

/// <summary>
/// What the C ABI does, as ordinary managed methods (<see cref="Exports"/> only marshals and guards them), so tests can
/// call them directly. Settings calls are made from the Mac app's main thread; the rest from any thread.
/// </summary>
internal static class NativeApi
{
    /// <summary>The C ABI's version: the Swift app refuses a library whose version differs from its header's.</summary>
    public const int AbiVersion = 1;

    /// <summary>The app's display name, <see cref="AppInfo.Name"/>: the Swift app never spells it.</summary>
    public static string AppName => AppInfo.Name;

    /// <summary>
    /// The version from <c>&lt;Version&gt;</c> in <c>src/Directory.Build.props</c>, without the source revision the SDK
    /// appends after a <c>+</c>.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The settings in <paramref name="directory"/>'s settings.json, validated, as transport JSON.</summary>
    /// <remarks>Never fails: a missing or bad file gives the defaults (CORE-API §7.2).</remarks>
    public static string LoadSettings(string directory) =>
        SettingsTransport.Serialize(CoreFactory.CreateSettingsStore(directory).Load());

    /// <summary>Validates the settings in <paramref name="json"/> and writes them to the directory's settings.json.</summary>
    /// <returns><see cref="NativeStatus.Ok"/>, <see cref="NativeStatus.BadJson"/> or <see cref="NativeStatus.IoError"/>.</returns>
    public static int SaveSettings(string directory, string json)
    {
        Settings? settings = SettingsTransport.Deserialize(json);
        if (settings is null) return NativeStatus.BadJson;
        try
        {
            CoreFactory.CreateSettingsStore(directory).Save(settings);
            return NativeStatus.Ok;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return NativeStatus.IoError;
        }
    }

    private static string ReadVersion()
    {
        string? informational = typeof(NativeApi).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational)) return "0.0.0";
        int plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
