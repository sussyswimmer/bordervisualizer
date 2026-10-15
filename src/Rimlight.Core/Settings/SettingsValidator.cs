// The folder is Settings/, but the namespace can't be Rimlight.Core.Settings: it would collide with the Settings
// record (CS0101, see HANDOFF H-002).
namespace Rimlight.Core.SettingsStorage;

/// <summary>
/// Brings any <see cref="Settings"/> into the doc 06 §1 ranges. Every field that can't be used is reset to its default
/// on its own, so one bad value never costs the user the rest of their settings.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Finite numbers outside their range are clamped to the nearest end; NaN and ±∞ become the default.</item>
/// <item><see cref="Settings.FpsCap"/> outside {0, 30, 60, 120} becomes 60.</item>
/// <item>Undefined enum values become the default.</item>
/// <item>Colors: <c>#RRGGBB</c> or the CSS shorthand <c>#RGB</c>, hex digits in either case, surrounding whitespace
/// ignored. They are stored as upper-case <c>#RRGGBB</c> (<c>#7c5cff</c> → <c>#7C5CFF</c>, <c>#abc</c> →
/// <c>#AABBCC</c>). Anything else, including a missing <c>#</c> or an alpha channel, becomes the default.</item>
/// <item><see cref="Settings.ToggleHotkey"/> is opaque (H-009): only null becomes the default. Platform parses it,
/// and an empty string means "no hotkey".</item>
/// <item><see cref="Settings.CustomMonitorIds"/> are opaque device IDs compared ordinally (H-009): null and empty
/// entries are dropped; everything else is kept in order, including duplicates, whitespace and IDs of monitors that
/// are unplugged right now. A null list becomes empty.</item>
/// <item><see cref="Settings.Version"/> becomes <see cref="SettingsMigrator.CurrentVersion"/>: it names the schema of
/// a saved file, not a user value.</item>
/// </list>
/// </remarks>
internal static class SettingsValidator
{
    /// <summary>The defaults every invalid field falls back to.</summary>
    internal static Settings Defaults { get; } = new();

    internal const float MinPrimaryRatio = 0.1f, MaxPrimaryRatio = 0.9f;
    internal const float MinCoreThicknessDip = 0f, MaxCoreThicknessDip = 40f;
    internal const float MinGlow = 0f, MaxGlow = 1f;
    internal const float MinBrightness = 0.1f, MaxBrightness = 1f;
    internal const float MinSensitivity = 0.25f, MaxSensitivity = 2f;
    internal const float MinCornerRadiusDip = 0f, MaxCornerRadiusDip = 40f;

    /// <summary>Returns <paramref name="settings"/> with every field valid.</summary>
    /// <param name="settings">Settings from any source; fields may hold anything.</param>
    /// <returns>The same instance when it is already valid, otherwise a corrected copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    internal static Settings Validate(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings d = Defaults;
        Settings valid = settings with
        {
            Version = SettingsMigrator.CurrentVersion,
            Animation = Defined(settings.Animation, d.Animation),
            WhenSilent = Defined(settings.WhenSilent, d.WhenSilent),
            ColorMode = Defined(settings.ColorMode, d.ColorMode),
            PrimaryHex = NormalizeHex(settings.PrimaryHex) ?? d.PrimaryHex,
            SecondaryHex = NormalizeHex(settings.SecondaryHex) ?? d.SecondaryHex,
            PrimaryRatio = Clamp(settings.PrimaryRatio, MinPrimaryRatio, MaxPrimaryRatio, d.PrimaryRatio),
            CoreThicknessDip = Clamp(settings.CoreThicknessDip, MinCoreThicknessDip, MaxCoreThicknessDip, d.CoreThicknessDip),
            Glow = Clamp(settings.Glow, MinGlow, MaxGlow, d.Glow),
            Brightness = Clamp(settings.Brightness, MinBrightness, MaxBrightness, d.Brightness),
            Sensitivity = Clamp(settings.Sensitivity, MinSensitivity, MaxSensitivity, d.Sensitivity),
            CornerRadiusDip = Clamp(settings.CornerRadiusDip, MinCornerRadiusDip, MaxCornerRadiusDip, d.CornerRadiusDip),
            Monitors = Defined(settings.Monitors, d.Monitors),
            CustomMonitorIds = CleanMonitorIds(settings.CustomMonitorIds),
            FpsCap = IsValidFpsCap(settings.FpsCap) ? settings.FpsCap : d.FpsCap,
            OnBattery = Defined(settings.OnBattery, d.OnBattery),
            ToggleHotkey = settings.ToggleHotkey ?? d.ToggleHotkey,
        };
        // Record equality: a valid input comes back as the same instance (the monitor list is reused when clean).
        return valid == settings ? settings : valid;
    }

    /// <summary>Whether <paramref name="fpsCap"/> is one of the caps doc 06 offers (0 = native refresh rate).</summary>
    internal static bool IsValidFpsCap(int fpsCap) => fpsCap is 0 or 30 or 60 or 120;

    /// <summary>Parses a <c>#RRGGBB</c> or <c>#RGB</c> color into upper-case <c>#RRGGBB</c>.</summary>
    /// <param name="hex">The text to check; may be null.</param>
    /// <returns>The canonical color, or null if <paramref name="hex"/> isn't a valid color.</returns>
    internal static string? NormalizeHex(string? hex)
    {
        if (hex is null) return null;
        ReadOnlySpan<char> text = hex.AsSpan().Trim();
        if (text.Length is not (4 or 7) || text[0] != '#') return null;
        ReadOnlySpan<char> digits = text[1..];
        foreach (char c in digits)
        {
            if (!char.IsAsciiHexDigit(c)) return null;
        }

        Span<char> result = stackalloc char[7];
        result[0] = '#';
        for (int i = 0; i < 6; i++)
        {
            // #RGB doubles each digit, as in CSS: #abc is #AABBCC.
            char c = digits.Length == 3 ? digits[i / 2] : digits[i];
            result[i + 1] = char.ToUpperInvariant(c);
        }

        return result.SequenceEqual(hex) ? hex : new string(result);
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static T Defined<T>(T value, T fallback) where T : struct, Enum => Enum.IsDefined(value) ? value : fallback;

    private static IReadOnlyList<string> CleanMonitorIds(IReadOnlyList<string>? ids)
    {
        if (ids is null) return Defaults.CustomMonitorIds;
        bool clean = true;
        foreach (string? id in ids)
        {
            if (string.IsNullOrEmpty(id))
            {
                clean = false;
                break;
            }
        }

        if (clean) return ids;
        var kept = new List<string>(ids.Count);
        foreach (string? id in ids)
        {
            if (!string.IsNullOrEmpty(id)) kept.Add(id);
        }

        return kept.AsReadOnly();
    }
}
