namespace Rimlight.Native;

/// <summary>
/// What a C ABI call returns when it fails. Every failure is negative. Functions that write a string return its length
/// in bytes (0 or more) instead of <see cref="Ok"/> on success.
/// </summary>
internal static class NativeStatus
{
    /// <summary>The call succeeded.</summary>
    public const int Ok = 0;

    /// <summary>A null pointer, a negative capacity or a string that isn't UTF-8.</summary>
    public const int InvalidArgument = -1;

    /// <summary>The settings file couldn't be written.</summary>
    public const int IoError = -2;

    /// <summary>The settings JSON couldn't be read.</summary>
    public const int BadJson = -3;

    /// <summary>Anything else: an exception the call didn't expect. It is never thrown across the boundary.</summary>
    public const int Internal = -99;
}
