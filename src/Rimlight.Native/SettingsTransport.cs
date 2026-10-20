using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Rimlight.Core;

namespace Rimlight.Native;

/// <summary>
/// The settings as JSON across the C ABI, so the Swift app can read and edit every field, strings and lists included.
/// Same names as the settings file (camelCase, enums as names; CORE-API §7.2), but only a transport: the file itself is
/// always read and written by Core's own <see cref="ISettingsStore"/>, which validates it. Core's JSON is internal, so
/// this is a source-generated context of its own over the public <see cref="Settings"/> record.
/// </summary>
internal static class SettingsTransport
{
    public static string Serialize(Settings settings) =>
        JsonSerializer.Serialize(settings, SettingsTransportContext.Default.Settings);

    /// <summary>
    /// The settings in <paramref name="json"/>, with the defaults for any field it leaves out; null when it isn't a
    /// JSON object of settings.
    /// </summary>
    /// <remarks>
    /// The source generator sets every init-only property when it builds a record, so a field missing from the JSON
    /// would get its type's default (false, 0) instead of the record's (Enabled true, Glow 0.45). The fields given are
    /// therefore laid over the serialized defaults first, matching names without regard to case.
    /// </remarks>
    public static Settings? Deserialize(string json)
    {
        try
        {
            if (JsonNode.Parse(json, documentOptions: DocumentOptions) is not JsonObject given) return null;
            JsonObject merged = JsonSerializer.SerializeToNode(new Settings(), SettingsTransportContext.Default.Settings)!.AsObject();
            foreach ((string name, JsonNode? value) in given.ToArray())
            {
                string key = merged.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;
                given.Remove(name);
                merged[key] = value;
            }
            return merged.Deserialize(SettingsTransportContext.Default.Settings);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsTransportContext : JsonSerializerContext;
