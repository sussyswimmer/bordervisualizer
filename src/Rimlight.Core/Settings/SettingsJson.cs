using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rimlight.Core.SettingsStorage;

/// <summary>
/// The settings file format: a JSON object with camelCase property names (<c>version</c>, <c>primaryHex</c>,
/// <c>customMonitorIds</c>, …), enums as their C# member names (<c>"MusicSync"</c>), indented, UTF-8 without a BOM.
/// </summary>
/// <remarks>
/// <para>Writing uses the source-generated <see cref="SettingsJsonContext"/>. Reading is field by field instead of one
/// deserializer call, so a field with the wrong type or an unknown enum name falls back to its own default instead of
/// failing the whole file (doc 06 §1: "reset invalid fields to their defaults").</para>
/// <para>Reading is lenient about hand edits: property names match case-insensitively (the last duplicate wins),
/// enum names too, comments and trailing commas are allowed, a UTF-8 BOM is skipped, unknown fields are ignored and
/// missing fields take their defaults. Enums must be names; numbers are rejected. Monitor-ID entries that aren't
/// non-empty strings are dropped.</para>
/// </remarks>
internal static class SettingsJson
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Writes <paramref name="settings"/> as UTF-8 JSON. Validate first; this writes values as given.</summary>
    /// <param name="settings">Validated settings.</param>
    /// <returns>The file contents.</returns>
    internal static byte[] Serialize(Settings settings) =>
        JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.Settings);

    /// <summary>Reads, migrates and validates a settings file.</summary>
    /// <param name="utf8">The file contents.</param>
    /// <param name="migrator">Upgrades older schemas.</param>
    /// <returns>Valid settings, or null when the root isn't a JSON object.</returns>
    /// <exception cref="JsonException">The text isn't JSON.</exception>
    /// <exception cref="InvalidOperationException">A string holds invalid UTF-8.</exception>
    internal static Settings? Deserialize(ReadOnlyMemory<byte> utf8, SettingsMigrator migrator)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (utf8.Span.StartsWith(bom)) utf8 = utf8[bom.Length..];

        using JsonDocument document = JsonDocument.Parse(utf8, DocumentOptions);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            fields[property.Name] = property.Value; // last duplicate wins
        }

        migrator.Migrate(fields, SettingsMigrator.ReadVersion(fields));
        Settings settings = SettingsValidator.Validate(Read(fields));
        return settings.Version == migrator.LatestVersion ? settings : settings with { Version = migrator.LatestVersion };
    }

    // Every Settings property except Version, which the migrator owns. The round-trip test fails if one is missing.
    private static Settings Read(IReadOnlyDictionary<string, JsonElement> f)
    {
        Settings d = SettingsValidator.Defaults;
        return new Settings
        {
            Enabled = ReadBool(f, nameof(Settings.Enabled), d.Enabled),
            Animation = ReadEnum(f, nameof(Settings.Animation), d.Animation),
            WhenSilent = ReadEnum(f, nameof(Settings.WhenSilent), d.WhenSilent),
            ColorMode = ReadEnum(f, nameof(Settings.ColorMode), d.ColorMode),
            OverrideAlbumColor = ReadBool(f, nameof(Settings.OverrideAlbumColor), d.OverrideAlbumColor),
            PrimaryHex = ReadString(f, nameof(Settings.PrimaryHex), d.PrimaryHex),
            SecondaryHex = ReadString(f, nameof(Settings.SecondaryHex), d.SecondaryHex),
            PrimaryRatio = ReadFloat(f, nameof(Settings.PrimaryRatio), d.PrimaryRatio),
            CoreThicknessDip = ReadFloat(f, nameof(Settings.CoreThicknessDip), d.CoreThicknessDip),
            Glow = ReadFloat(f, nameof(Settings.Glow), d.Glow),
            Brightness = ReadFloat(f, nameof(Settings.Brightness), d.Brightness),
            Sensitivity = ReadFloat(f, nameof(Settings.Sensitivity), d.Sensitivity),
            CornerRadiusDip = ReadFloat(f, nameof(Settings.CornerRadiusDip), d.CornerRadiusDip),
            CoverTaskbar = ReadBool(f, nameof(Settings.CoverTaskbar), d.CoverTaskbar),
            Monitors = ReadEnum(f, nameof(Settings.Monitors), d.Monitors),
            CustomMonitorIds = ReadStringList(f, nameof(Settings.CustomMonitorIds), d.CustomMonitorIds),
            FpsCap = ReadInt(f, nameof(Settings.FpsCap), d.FpsCap),
            PauseInFullscreen = ReadBool(f, nameof(Settings.PauseInFullscreen), d.PauseInFullscreen),
            OnBattery = ReadEnum(f, nameof(Settings.OnBattery), d.OnBattery),
            HideFromScreenCapture = ReadBool(f, nameof(Settings.HideFromScreenCapture), d.HideFromScreenCapture),
            LaunchAtStartup = ReadBool(f, nameof(Settings.LaunchAtStartup), d.LaunchAtStartup),
            ToggleHotkey = ReadString(f, nameof(Settings.ToggleHotkey), d.ToggleHotkey),
            AutoUpdate = ReadBool(f, nameof(Settings.AutoUpdate), d.AutoUpdate),
            FirstRunComplete = ReadBool(f, nameof(Settings.FirstRunComplete), d.FirstRunComplete),
        };
    }

    private static bool ReadBool(IReadOnlyDictionary<string, JsonElement> f, string name, bool fallback) =>
        f.TryGetValue(name, out JsonElement e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? e.GetBoolean()
            : fallback;

    private static float ReadFloat(IReadOnlyDictionary<string, JsonElement> f, string name, float fallback) =>
        f.TryGetValue(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out float value)
            ? value
            : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, JsonElement> f, string name, int fallback) =>
        f.TryGetValue(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int value)
            ? value
            : fallback;

    private static string ReadString(IReadOnlyDictionary<string, JsonElement> f, string name, string fallback) =>
        f.TryGetValue(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : fallback;

    private static T ReadEnum<T>(IReadOnlyDictionary<string, JsonElement> f, string name, T fallback) where T : struct, Enum
    {
        if (!f.TryGetValue(name, out JsonElement e) || e.ValueKind != JsonValueKind.String) return fallback;
        string text = e.GetString()!;
        // Exact member names only: Enum.TryParse would also take "1" or "MusicSync, Off".
        foreach (T value in Enum.GetValues<T>())
        {
            if (string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase)) return value;
        }

        return fallback;
    }

    private static IReadOnlyList<string> ReadStringList(IReadOnlyDictionary<string, JsonElement> f, string name, IReadOnlyList<string> fallback)
    {
        if (!f.TryGetValue(name, out JsonElement e) || e.ValueKind != JsonValueKind.Array) return fallback;
        var list = new List<string>(e.GetArrayLength());
        foreach (JsonElement item in e.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } id) list.Add(id);
        }

        return list.AsReadOnly();
    }
}

/// <summary>Source-generated writer for the settings file (camelCase, string enums, indented).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
