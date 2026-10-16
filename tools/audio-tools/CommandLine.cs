using System.Globalization;

namespace Rimlight.AudioTools;

/// <summary>A command line error: tools print the message and a usage hint, and exit with code 2.</summary>
/// <param name="message">What is wrong with the command line.</param>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Minimal option parsing for the console tools: positional arguments, <c>--name value</c>, <c>--name=value</c>,
/// <c>--flag</c>, and <c>--</c> to end options. Every option must be declared, so a typo is an error rather than
/// silently ignored. Numbers use the invariant culture (a dot as the decimal separator) on every machine.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);

    /// <summary>Parses arguments.</summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="valueOptions">Option names (without dashes) that take a value.</param>
    /// <param name="flags">Option names (without dashes) that take none.</param>
    /// <exception cref="UsageException">An option is unknown, repeated, or missing its value.</exception>
    public CommandLine(IEnumerable<string> args, IEnumerable<string> valueOptions, IEnumerable<string> flags)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new HashSet<string>(valueOptions, StringComparer.Ordinal);
        var flagSet = new HashSet<string>(flags, StringComparer.Ordinal);
        using IEnumerator<string> e = args.GetEnumerator();
        bool positionalOnly = false;
        while (e.MoveNext())
        {
            string arg = e.Current;
            if (positionalOnly || !arg.StartsWith("--", StringComparison.Ordinal))
            {
                Positional.Add(arg);
                continue;
            }
            if (arg == "--")
            {
                positionalOnly = true;
                continue;
            }
            string name = arg[2..];
            string? value = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            if (flagSet.Contains(name))
            {
                if (value is not null) throw new UsageException($"--{name} takes no value.");
            }
            else if (values.Contains(name))
            {
                if (value is null)
                {
                    if (!e.MoveNext()) throw new UsageException($"--{name} needs a value.");
                    value = e.Current;
                }
            }
            else
            {
                throw new UsageException($"Unknown option --{name}.");
            }
            if (!options.TryAdd(name, value)) throw new UsageException($"--{name} is given twice.");
        }
    }

    /// <summary>Arguments that are not options, in order.</summary>
    public List<string> Positional { get; } = [];

    /// <summary>Whether a flag or option was given.</summary>
    /// <param name="name">The option name without dashes.</param>
    /// <returns>True if present.</returns>
    public bool Has(string name) => options.ContainsKey(name);

    /// <summary>An option's text.</summary>
    /// <param name="name">The option name without dashes.</param>
    /// <returns>The value, or null if the option was not given.</returns>
    public string? Text(string name) => options.TryGetValue(name, out string? value) ? value : null;

    /// <summary>An option as a finite number in a range.</summary>
    /// <param name="name">The option name without dashes.</param>
    /// <param name="fallback">The value when the option is absent.</param>
    /// <param name="min">The smallest allowed value.</param>
    /// <param name="max">The largest allowed value.</param>
    /// <returns>The number.</returns>
    /// <exception cref="UsageException">The value is not a number in range.</exception>
    public double Number(string name, double fallback, double min, double max)
    {
        string? text = Text(name);
        if (text is null) return fallback;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new UsageException($"--{name} expects a number, not \"{text}\".");
        if (value < min || value > max)
            throw new UsageException($"--{name} must be from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");
        return value;
    }

    /// <summary>An option as a whole number in a range.</summary>
    /// <param name="name">The option name without dashes.</param>
    /// <param name="fallback">The value when the option is absent.</param>
    /// <param name="min">The smallest allowed value.</param>
    /// <param name="max">The largest allowed value.</param>
    /// <returns>The number.</returns>
    /// <exception cref="UsageException">The value is not a whole number in range.</exception>
    public int Integer(string name, int fallback, int min, int max)
    {
        string? text = Text(name);
        if (text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new UsageException($"--{name} expects a whole number, not \"{text}\".");
        if (value < min || value > max) throw new UsageException($"--{name} must be from {min} to {max}.");
        return value;
    }

    /// <summary>An option as a seed.</summary>
    /// <param name="name">The option name without dashes.</param>
    /// <param name="fallback">The value when the option is absent.</param>
    /// <returns>The seed.</returns>
    /// <exception cref="UsageException">The value is not a non-negative whole number.</exception>
    public ulong Seed(string name, ulong fallback)
    {
        string? text = Text(name);
        if (text is null) return fallback;
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value)
            ? value
            : throw new UsageException($"--{name} expects a non-negative whole number, not \"{text}\".");
    }

    /// <summary>An option as a level in dB (e.g. "-12") or "off".</summary>
    /// <param name="name">The option name without dashes.</param>
    /// <param name="fallback">The linear amplitude when the option is absent.</param>
    /// <returns>The linear amplitude: 10^(dB/20), or 0 for "off".</returns>
    /// <exception cref="UsageException">The value is neither a level of at most +24 dB nor "off".</exception>
    public double Level(string name, double fallback)
    {
        string? text = Text(name);
        if (text is null) return fallback;
        if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) return 0;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double db) || !double.IsFinite(db) || db > 24)
            throw new UsageException($"--{name} expects a level in dB (at most +24) or \"off\", not \"{text}\".");
        return Math.Pow(10, db / 20);
    }
}
