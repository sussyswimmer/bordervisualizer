using System.Runtime.InteropServices;
using System.Text;

namespace Rimlight.Native;

/// <summary>
/// UTF-8 strings across the C ABI. A string goes out into a buffer the caller owns, so no memory crosses the boundary
/// to be freed: the call returns the string's length in bytes and writes it with a terminating NUL only when the
/// capacity has room for both. A caller with too small a buffer calls again with a larger one.
/// </summary>
internal static unsafe class Utf8Text
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Writes <paramref name="value"/> into the caller's buffer when it fits.</summary>
    /// <returns>The length in bytes without the NUL, or <see cref="NativeStatus.InvalidArgument"/>.</returns>
    public static int Write(string value, byte* buffer, int capacity)
    {
        if (capacity < 0 || (buffer == null && capacity > 0)) return NativeStatus.InvalidArgument;
        int length = Encoding.UTF8.GetByteCount(value);
        if (capacity > length)
        {
            var destination = new Span<byte>(buffer, capacity);
            Encoding.UTF8.GetBytes(value, destination);
            destination[length] = 0;
        }
        return length;
    }

    /// <summary>Reads a NUL-terminated UTF-8 string; null for a null pointer or bytes that aren't valid UTF-8.</summary>
    public static string? Read(byte* text)
    {
        if (text == null) return null;
        // Scans for the NUL without reading past the page it is on (a span of int.MaxValue bytes could).
        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
        try
        {
            return Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
