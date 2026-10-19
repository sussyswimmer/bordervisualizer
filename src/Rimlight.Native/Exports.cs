using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rimlight.Native;

/// <summary>
/// The C ABI the Swift app calls (<c>mac/Sources/CRimlight/include/rimlight_core.h</c> declares it: keep the two in
/// sync). Strings are UTF-8: inputs NUL-terminated, outputs into a caller-owned buffer (<see cref="Utf8Text"/>).
/// </summary>
/// <remarks>
/// An exception that escapes an <see cref="UnmanagedCallersOnlyAttribute"/> method ends the process, so every export
/// catches everything and returns <see cref="NativeStatus.Internal"/> instead.
/// </remarks>
internal static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "rl_abi_version")]
    public static int AbiVersion() => NativeApi.AbiVersion;

    [UnmanagedCallersOnly(EntryPoint = "rl_app_name")]
    public static int AppName(byte* buffer, int capacity) =>
        Guard(() => Utf8Text.Write(NativeApi.AppName, buffer, capacity));

    [UnmanagedCallersOnly(EntryPoint = "rl_version")]
    public static int Version(byte* buffer, int capacity) =>
        Guard(() => Utf8Text.Write(NativeApi.Version, buffer, capacity));

    [UnmanagedCallersOnly(EntryPoint = "rl_settings_load")]
    public static int SettingsLoad(byte* directory, byte* buffer, int capacity) => Guard(() =>
    {
        string? dir = Utf8Text.Read(directory);
        if (string.IsNullOrWhiteSpace(dir)) return NativeStatus.InvalidArgument;
        return Utf8Text.Write(NativeApi.LoadSettings(dir), buffer, capacity);
    });

    [UnmanagedCallersOnly(EntryPoint = "rl_settings_save")]
    public static int SettingsSave(byte* directory, byte* json) => Guard(() =>
    {
        string? dir = Utf8Text.Read(directory);
        string? text = Utf8Text.Read(json);
        if (string.IsNullOrWhiteSpace(dir) || text is null) return NativeStatus.InvalidArgument;
        return NativeApi.SaveSettings(dir, text);
    });

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Guard(Func<int> call)
    {
        try
        {
            return call();
        }
        catch (Exception)
        {
            return NativeStatus.Internal;
        }
    }
}
