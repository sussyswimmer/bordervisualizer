namespace Rimlight.Core;

/// <summary>Appearance presets; populated by C7.</summary>
public static class Presets
{
    /// <summary>The built-in appearance transformations; empty until C7 supplies the real presets.</summary>
    public static IReadOnlyList<(string Name, Func<Settings, Settings> Apply)> All { get; } =
        Array.Empty<(string Name, Func<Settings, Settings> Apply)>();
}
