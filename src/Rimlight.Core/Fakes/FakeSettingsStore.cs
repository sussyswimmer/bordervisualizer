namespace Rimlight.Core.Fakes;

// K0 stand-in only: persistence, recovery and validation arrive in C7.
internal sealed class FakeSettingsStore(string directory) : ISettingsStore
{
    private Settings current = new();
    public string Path { get; } = System.IO.Path.Combine(directory, "settings.json");
    public Settings Load() => current;
    public void Save(Settings settings) => current = settings;
}
