namespace Rimlight.Core;

/// <summary>Appearance presets; populated by C7.</summary>
public static class Presets
{
    /// <summary>The built-in appearance transformations (not implemented until C7).</summary>
    public static IReadOnlyList<(string Name, Func<Settings, Settings> Apply)> All =>
        throw new NotImplementedException("Appearance presets arrive in C7.");
}
