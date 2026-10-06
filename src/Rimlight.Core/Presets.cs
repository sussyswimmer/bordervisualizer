using Rimlight.Core.SettingsStorage;

namespace Rimlight.Core;

/// <summary>The five built-in looks from doc 06 §1, applied in one click from the tray and Settings.</summary>
/// <remarks>
/// <para>Every preset sets the same appearance fields: <see cref="Settings.PrimaryHex"/>,
/// <see cref="Settings.SecondaryHex"/>, <see cref="Settings.PrimaryRatio"/>, <see cref="Settings.CoreThicknessDip"/>,
/// <see cref="Settings.Glow"/> and <see cref="Settings.Brightness"/>. Applying presets one after another therefore
/// gives the same look as applying the last one alone (only Minimal's Idle Glow mode stays until the user changes the
/// mode). Corner radius and taskbar coverage fit the user's screen, not the look, so they are kept.</para>
/// <para>Every preset also sets <see cref="Settings.OverrideAlbumColor"/> to true and keeps
/// <see cref="Settings.ColorMode"/>, so the look shows even in Album Art mode, and turning Override off brings the album
/// colors back (H-009). Minimal also sets <see cref="Settings.Animation"/> to <see cref="AnimationMode.IdleGlow"/>.
/// Nothing else changes.</para>
/// <para>Each <c>Apply</c> returns a new, validated <see cref="Settings"/> and throws
/// <see cref="ArgumentNullException"/> for null. For swatches, read the colors of <c>Apply(new Settings())</c>.</para>
/// </remarks>
public static class Presets
{
    /// <summary>The presets in menu order: Aurora, Sunset, Neon, Ember, Minimal.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Aurora:</b> teal into violet with a wide glow (0.6).</item>
    /// <item><b>Sunset:</b> orange into pink.</item>
    /// <item><b>Neon:</b> magenta and cyan, an even split, a thick 12-DIP core at full brightness.</item>
    /// <item><b>Ember:</b> red into amber. "Slow" is a dim (0.6), very soft (glow 0.8), thin-cored look, so beats
    /// swell like embers instead of flashing; the engine's motion speed isn't a setting, and Sensitivity is motion,
    /// which presets leave alone.</item>
    /// <item><b>Minimal:</b> a soft warm-to-cool white 1-DIP hairline with little glow, in Idle Glow mode.</item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<(string Name, Func<Settings, Settings> Apply)> All { get; } = Array.AsReadOnly(
        new (string Name, Func<Settings, Settings> Apply)[]
        {
            ("Aurora", s => Look(s, "#14E3C5", "#9D4DFF", ratio: 0.60f, coreDip: 6f, glow: 0.60f, brightness: 0.85f)),
            ("Sunset", s => Look(s, "#FF8A2A", "#FF4F9A", ratio: 0.55f, coreDip: 6f, glow: 0.50f, brightness: 0.85f)),
            ("Neon", s => Look(s, "#FF2BD6", "#00E5FF", ratio: 0.50f, coreDip: 12f, glow: 0.35f, brightness: 1.00f)),
            ("Ember", s => Look(s, "#FF3A1F", "#FFB01F", ratio: 0.65f, coreDip: 4f, glow: 0.80f, brightness: 0.60f)),
            ("Minimal", s => Look(s, "#F4F1EA", "#E8EDF5", ratio: 0.50f, coreDip: 1f, glow: 0.15f, brightness: 0.55f,
                animation: AnimationMode.IdleGlow)),
        });

    private static Settings Look(
        Settings settings,
        string primaryHex,
        string secondaryHex,
        float ratio,
        float coreDip,
        float glow,
        float brightness,
        AnimationMode? animation = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SettingsValidator.Validate(settings with
        {
            OverrideAlbumColor = true,
            PrimaryHex = primaryHex,
            SecondaryHex = secondaryHex,
            PrimaryRatio = ratio,
            CoreThicknessDip = coreDip,
            Glow = glow,
            Brightness = brightness,
            Animation = animation ?? settings.Animation,
        });
    }
}
