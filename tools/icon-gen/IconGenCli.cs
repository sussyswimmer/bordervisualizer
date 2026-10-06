using System.Globalization;

namespace Rimlight.IconGen;

/// <summary>The icon-gen command line. <see cref="Run"/> is the whole program, so tests can drive it.</summary>
internal static class IconGenCli
{
    internal const string Usage = """
        icon-gen: render SVG art to a multi-size Windows .ico or to a PNG.

        Usage:
          icon-gen ico <out.ico> --svg <file> [options]   Write an .ico with one image per size
          icon-gen png <out.png> --svg <file> --size <px> [--opacity <f>]
          icon-gen --help

        Options:
          --svg <file>        The art. Its viewBox (or width and height) is scaled to fill each square image.
          --small-svg <file>  Simplified art for the small .ico sizes, up to --small-max
          --small-max <px>    Largest size drawn from --small-svg (default 32)
          --sizes <list>      .ico sizes, 1 to 256, e.g. 16,20,24,32 (default 16,20,24,32,40,48,64,256)
          --size <px>         PNG size, 1 to 4096
          --opacity <f>       Multiply every pixel's alpha by f, 0 to 1; 0.5 gives the tray's
                              "glow off" icon (default 1)

        .ico layout: images below 256 px are 32-bpp bitmaps with an alpha channel, 256 px is a PNG.
        build/icons.sh and build/icons.ps1 regenerate every icon in the repo with this tool.

        Exit codes: 0 success, 1 failed (missing or invalid SVG, unwritable output), 2 bad command line.

        """;

    private const int DefaultSmallMax = 32;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            (args.Length == 0 ? error : output).Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        Options options;
        try
        {
            options = Parse(args);
        }
        catch (UsageException e)
        {
            error.WriteLine("icon-gen: " + e.Message);
            error.WriteLine("Run 'icon-gen --help' for usage.");
            return 2;
        }

        try
        {
            return options.Command == "ico" ? WriteIco(options, output) : WritePng(options, output);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            error.WriteLine("icon-gen: " + e.Message);
            return 1;
        }
    }

    private static int WriteIco(Options options, TextWriter output)
    {
        using SvgRasterizer art = SvgRasterizer.FromFile(options.Svg);
        using SvgRasterizer? small = options.SmallSvg is null ? null : SvgRasterizer.FromFile(options.SmallSvg);
        byte[] ico = IconBuilder.BuildIco(
            options.Sizes,
            size => small is not null && size <= options.SmallMax ? small : art,
            options.Opacity);
        WriteFile(options.Output, ico);

        string list = string.Join(", ", options.Sizes.Order().Select(s =>
            small is not null && s <= options.SmallMax ? s + "*" : s.ToString(CultureInfo.InvariantCulture)));
        output.WriteLine($"Wrote {options.Output} ({ico.Length:N0} bytes): {list} px"
            + (small is null ? "" : $" (* from {options.SmallSvg})")
            + (options.Opacity < 1f ? $", opacity {options.Opacity.ToString(CultureInfo.InvariantCulture)}" : ""));
        return 0;
    }

    private static int WritePng(Options options, TextWriter output)
    {
        using SvgRasterizer art = SvgRasterizer.FromFile(options.Svg);
        byte[] png = IconBuilder.BuildPng(art, options.Size, options.Opacity);
        WriteFile(options.Output, png);
        output.WriteLine($"Wrote {options.Output} ({png.Length:N0} bytes): {options.Size}x{options.Size} px"
            + (options.Opacity < 1f ? $", opacity {options.Opacity.ToString(CultureInfo.InvariantCulture)}" : ""));
        return 0;
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    internal static Options Parse(string[] args)
    {
        string command = args[0];
        if (command is not ("ico" or "png"))
        {
            throw new UsageException($"unknown command '{command}'; expected 'ico' or 'png'.");
        }

        string? outputPath = null;
        string? svg = null;
        string? smallSvg = null;
        int? smallMax = null;
        int[]? sizes = null;
        int? size = null;
        float opacity = 1f;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--svg":
                    svg = Value(args, ref i);
                    break;
                case "--small-svg" when command == "ico":
                    smallSvg = Value(args, ref i);
                    break;
                case "--small-max" when command == "ico":
                    smallMax = Int(arg, Value(args, ref i), 1, IcoWriter.MaxSize);
                    break;
                case "--sizes" when command == "ico":
                    sizes = SizeList(Value(args, ref i));
                    break;
                case "--size" when command == "png":
                    size = Int(arg, Value(args, ref i), 1, 4096);
                    break;
                case "--opacity":
                    opacity = Opacity(Value(args, ref i));
                    break;
                default:
                    if (arg.StartsWith('-') && arg.Length > 1)
                    {
                        throw new UsageException($"unknown option '{arg}' for '{command}'.");
                    }

                    if (outputPath is not null)
                    {
                        throw new UsageException($"unexpected argument '{arg}'; give one output file.");
                    }

                    outputPath = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new UsageException($"give the output file: icon-gen {command} <out.{command}> --svg <file>.");
        }

        if (svg is null)
        {
            throw new UsageException("--svg <file> is required.");
        }

        if (command == "png" && size is null)
        {
            throw new UsageException("--size <px> is required for png.");
        }

        if (smallMax is not null && smallSvg is null)
        {
            throw new UsageException("--small-max needs --small-svg.");
        }

        return new Options(
            command,
            outputPath,
            svg,
            smallSvg,
            smallMax ?? DefaultSmallMax,
            sizes ?? [.. IconBuilder.DefaultSizes],
            size ?? 0,
            opacity);
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new UsageException($"{args[i]} needs a value.");
        }

        return args[++i];
    }

    private static int Int(string option, string text, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
        {
            throw new UsageException($"{option} must be a whole number from {min} to {max}, not '{text}'.");
        }

        return value;
    }

    private static int[] SizeList(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        int[] sizes = parts.Select(p => Int("--sizes", p, 1, IcoWriter.MaxSize)).ToArray();
        int? duplicate = sizes.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => (int?)g.Key).FirstOrDefault();
        if (duplicate is not null)
        {
            throw new UsageException($"--sizes lists {duplicate} twice.");
        }

        return sizes;
    }

    private static float Opacity(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || !(value >= 0f && value <= 1f))
        {
            throw new UsageException($"--opacity must be a number from 0 to 1, not '{text}'.");
        }

        return value;
    }

    internal sealed record Options(
        string Command,
        string Output,
        string Svg,
        string? SmallSvg,
        int SmallMax,
        int[] Sizes,
        int Size,
        float Opacity);

    private sealed class UsageException(string message) : Exception(message);
}
