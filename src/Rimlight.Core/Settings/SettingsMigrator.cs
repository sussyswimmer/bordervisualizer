using System.Text.Json;

namespace Rimlight.Core.SettingsStorage;

/// <summary>One schema upgrade: rewrites the fields of a version <see cref="From"/> file into version
/// <see cref="From"/> + 1.</summary>
/// <param name="From">The version this step upgrades from.</param>
/// <param name="Upgrade">Edits the top-level fields in place. Keys are case-insensitive (a file may say
/// <c>glow</c> or <c>Glow</c>); a step only touches the fields it is about and tolerates them being absent.</param>
internal readonly record struct MigrationStep(int From, Action<Dictionary<string, JsonElement>> Upgrade);

/// <summary>
/// Upgrades a settings file written by an older build to the current schema, one version at a time, before its
/// fields are read and validated (doc 02 "Settings storage").
/// </summary>
/// <remarks>
/// <para>Version 1 is the first schema, so <see cref="Default"/> has no steps yet. When a field is renamed, split or
/// re-scaled, bump <see cref="CurrentVersion"/> and append one step per version. For example, a version 2 that renames
/// <c>glow</c> to <c>glowAmount</c> adds:</para>
/// <code>new MigrationStep(1, fields => { if (fields.Remove("glow", out JsonElement value)) fields["glowAmount"] = value; })</code>
/// <para>New fields with defaults don't need a step: a missing field reads as its default.</para>
/// <para>A missing, non-integer or below-1 version is read as version 1; a whole number may be written as <c>2.0</c>,
/// and one past <see cref="int.MaxValue"/> counts as <see cref="int.MaxValue"/>. A file from a newer build is not
/// migrated: the fields this build knows are read and the rest are ignored.</para>
/// </remarks>
internal sealed class SettingsMigrator
{
    /// <summary>The schema version this build writes.</summary>
    internal const int CurrentVersion = 1;

    /// <summary>The first schema version; older or missing versions are read as this one.</summary>
    internal const int FirstVersion = 1;

    /// <summary>This build's step table: one step per version from <see cref="FirstVersion"/> up to
    /// <see cref="CurrentVersion"/>.</summary>
    internal static SettingsMigrator Default { get; } = new(CurrentVersion, []);

    private readonly MigrationStep[] steps;

    /// <summary>Creates a migrator for a schema at <paramref name="currentVersion"/>.</summary>
    /// <param name="currentVersion">The newest version; the target of every migration.</param>
    /// <param name="steps">Exactly one step per version from <see cref="FirstVersion"/> to
    /// <paramref name="currentVersion"/> − 1, in order.</param>
    /// <exception cref="ArgumentException">The table has a gap, a duplicate or a step out of order.</exception>
    internal SettingsMigrator(int currentVersion, IReadOnlyList<MigrationStep> steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, FirstVersion);
        if (steps.Count != currentVersion - FirstVersion)
        {
            throw new ArgumentException($"Expected {currentVersion - FirstVersion} steps, got {steps.Count}.", nameof(steps));
        }

        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].From != FirstVersion + i || steps[i].Upgrade is null)
            {
                throw new ArgumentException($"Step {i} must upgrade from version {FirstVersion + i}.", nameof(steps));
            }
        }

        LatestVersion = currentVersion;
        this.steps = [.. steps];
    }

    /// <summary>The version every file is migrated to and every save writes.</summary>
    internal int LatestVersion { get; }

    /// <summary>Reads the <c>version</c> field the way <see cref="Migrate"/> interprets it.</summary>
    /// <param name="fields">The file's top-level fields (case-insensitive keys).</param>
    /// <returns>The file's version, at least <see cref="FirstVersion"/>.</returns>
    internal static int ReadVersion(IReadOnlyDictionary<string, JsonElement> fields) =>
        fields.TryGetValue(nameof(Settings.Version), out JsonElement element)
        && element.ValueKind == JsonValueKind.Number
        && SettingsJson.TryGetWholeNumber(element, out int version)
            ? Math.Max(version, FirstVersion)
            : FirstVersion;

    /// <summary>Applies every step from <paramref name="fileVersion"/> up to <see cref="LatestVersion"/>.</summary>
    /// <param name="fields">The file's top-level fields, edited in place.</param>
    /// <param name="fileVersion">The version the file was written with.</param>
    /// <returns>The schema version the fields are in now: <see cref="LatestVersion"/>, or
    /// <paramref name="fileVersion"/> when that is newer.</returns>
    internal int Migrate(Dictionary<string, JsonElement> fields, int fileVersion)
    {
        if (fileVersion >= LatestVersion) return fileVersion;
        for (int version = Math.Max(fileVersion, FirstVersion); version < LatestVersion; version++)
        {
            steps[version - FirstVersion].Upgrade(fields);
        }

        return LatestVersion;
    }
}
