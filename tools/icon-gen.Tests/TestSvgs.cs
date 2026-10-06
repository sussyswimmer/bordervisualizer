namespace Rimlight.IconGen.Tests;

/// <summary>Small SVGs with known pixels, and a temporary directory that cleans up after itself.</summary>
internal static class TestSvgs
{
    /// <summary>16×16 units: the left half opaque red, the right half empty.</summary>
    public const string LeftHalfRed = """
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
          <rect x="0" y="0" width="8" height="16" fill="#FF0000"/>
        </svg>
        """;

    /// <summary>20×10 units, filled blue: letterboxed to the middle half of a square.</summary>
    public const string WideBlue = """
        <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10" viewBox="0 0 20 10">
          <rect x="0" y="0" width="20" height="10" fill="#0000FF"/>
        </svg>
        """;

    /// <summary>Filled with half-transparent green.</summary>
    public const string HalfGreen = """
        <svg xmlns="http://www.w3.org/2000/svg" width="8" height="8" viewBox="0 0 8 8">
          <rect x="0" y="0" width="8" height="8" fill="#00FF00" fill-opacity="0.5"/>
        </svg>
        """;

    public static string Solid(string color) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10" viewBox="0 0 10 10">
          <rect x="0" y="0" width="10" height="10" fill="{color}"/>
        </svg>
        """;

    /// <summary>BGRA of pixel (x, y) in a square straight-alpha buffer.</summary>
    public static (byte B, byte G, byte R, byte A) Pixel(byte[] bgra, int size, int x, int y)
    {
        int i = (y * size + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]);
    }
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "icon-gen-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, string? contents = null)
    {
        string path = System.IO.Path.Combine(Path, name);
        if (contents is not null)
        {
            System.IO.File.WriteAllText(path, contents);
        }

        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
