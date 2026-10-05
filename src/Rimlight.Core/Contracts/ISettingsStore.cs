namespace Rimlight.Core;

/// <summary>Persistent settings storage; the real implementation arrives in C7.</summary>
public interface ISettingsStore
{
    /// <summary>Loads settings without throwing; backs up corrupt files and falls back to defaults.</summary>
    /// <returns>Validated settings or defaults.</returns>
    Settings Load();
    /// <summary>Atomically writes the settings.</summary>
    /// <param name="settings">Settings snapshot to save.</param>
    void Save(Settings settings);
    /// <summary>Full path to the settings file.</summary>
    string Path { get; }
}
