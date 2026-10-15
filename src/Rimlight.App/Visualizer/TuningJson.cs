using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// "Copy params as JSON" and "Paste params" (doc 03 §4): one flat JSON object whose keys are
/// <see cref="AudioTuning"/>'s property names, as System.Text.Json writes them by default (no renaming), e.g.
/// <c>{ "WindowSize": 2048, "BassMinHz": 30, … }</c>. It is the format <c>tools/wav-analyze --tuning</c> reads (C3,
/// doc 09 sync point 2), so the same options are used on both sides: written indented with every property; read with
/// keys matched case-insensitively, comments and trailing commas allowed, unknown keys refused (to catch typos), and
/// missing keys keeping their defaults. A number too large for a float field is refused as well: the serializer reads
/// 1e39 as infinity without an error.
/// </summary>
internal static class TuningJson
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    // The same leniency for the document that tells which keys are present.
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly PropertyInfo[] FloatProperties = typeof(AudioTuning)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(float))
        .ToArray();

    /// <summary>Every property of <paramref name="tuning"/> as indented JSON.</summary>
    public static string Serialize(AudioTuning tuning) => JsonSerializer.Serialize(tuning, WriteOptions);

    /// <summary>Reads tuning JSON on top of the defaults. Values are not range-checked here; the analyzer does that.</summary>
    /// <param name="json">The text.</param>
    /// <param name="tuning">The tuning read, or null.</param>
    /// <param name="hasSensitivity">Whether the JSON has a Sensitivity key (in any case); without one, Sensitivity is
    /// only the default.</param>
    /// <param name="error">Why it couldn't be read, or null.</param>
    /// <returns>True when <paramref name="json"/> is a JSON object of AudioTuning properties.</returns>
    public static bool TryParse(string? json, out AudioTuning? tuning, out bool hasSensitivity, out string? error)
    {
        tuning = null;
        hasSensitivity = false;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The clipboard has no text.";
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
            JsonElement root = document.RootElement;
            AudioTuning? read = root.Deserialize<AudioTuning>(ReadOptions);
            if (read is null)
            {
                error = "Expected a JSON object of tuning parameters, not null.";
                return false;
            }
            PropertyInfo? infinite = Array.Find(FloatProperties, p => p.GetValue(read) is float value && !float.IsFinite(value));
            if (infinite is not null)
            {
                error = $"{infinite.Name} is too large.";
                return false;
            }
            tuning = read;
            hasSensitivity = root.EnumerateObject().Any(p =>
                string.Equals(p.Name, nameof(AudioTuning.Sensitivity), StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException exception)
        {
            error = exception.Message;
        }
        catch (NotSupportedException exception)
        {
            error = exception.Message;
        }
        return tuning is not null;
    }
}
