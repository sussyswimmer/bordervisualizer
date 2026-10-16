using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimlight.Core;

namespace Rimlight.AudioTools;

/// <summary>
/// Reads and writes <see cref="AudioTuning"/> as JSON: one flat object whose keys are AudioTuning property names
/// (e.g. <c>{ "Sensitivity": 1.25, "FluxThresholdMultiplier": 1.8 }</c>). Any subset may be given; missing properties
/// keep their defaults. Keys match case-insensitively, unknown keys are errors (to catch typos), and comments and
/// trailing commas are allowed. This is the format of the debug visualizer's "Copy params as JSON" (K8).
/// </summary>
public static class TuningJson
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Parses tuning JSON on top of the defaults.</summary>
    /// <param name="json">A JSON object of AudioTuning properties.</param>
    /// <returns>The tuning. Values are not range-checked here; the analyzer validates them.</returns>
    /// <exception cref="JsonException">The text is not a JSON object of AudioTuning properties.</exception>
    public static AudioTuning Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<AudioTuning>(json, ReadOptions)
            ?? throw new JsonException("Expected a JSON object of AudioTuning properties, not null.");
    }

    /// <summary>Reads tuning JSON from a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The tuning.</returns>
    /// <exception cref="JsonException">The file is not a JSON object of AudioTuning properties.</exception>
    public static AudioTuning Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Writes every AudioTuning property as indented JSON that <see cref="Parse"/> reads back.</summary>
    /// <param name="tuning">The tuning.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(AudioTuning tuning) => JsonSerializer.Serialize(tuning, WriteOptions);

    /// <summary>The properties that differ from the defaults, as "Name=value" (invariant culture).</summary>
    /// <param name="tuning">The tuning.</param>
    /// <returns>One entry per changed property, in declaration order.</returns>
    public static IReadOnlyList<string> Overrides(AudioTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var defaults = new AudioTuning();
        var changed = new List<string>();
        foreach (PropertyInfo property in typeof(AudioTuning).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = property.GetValue(tuning);
            if (!Equals(value, property.GetValue(defaults)))
                changed.Add($"{property.Name}={Convert.ToString(value, CultureInfo.InvariantCulture)}");
        }
        return changed;
    }
}
