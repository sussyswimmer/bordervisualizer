using Rimlight.App;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// The app's settings with the real store (C7): SettingsService (K3) saves what the UI changes, the first run's welcome
// shows once (K6), and the presets (C7) show as chosen in the tray and on the Appearance page (K6/K7).
public sealed class SettingsFlowTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rimlight-k10-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void TheWelcomeShowsOnceAndWhatTheUserChangedIsThereNextTime()
    {
        // First start: no file yet. AppController shows the welcome and marks the first run complete, like any change.
        using (var first = new SettingsService(CoreFactory.CreateSettingsStore(directory)))
        {
            Assert.False(File.Exists(Path.Combine(directory, "settings.json")));
            Assert.Equal(StartupWindow.Welcome, new StartupOptions(false, false, false).WindowAtStart(first.Current.FirstRunComplete));
            first.Update(s => s with { FirstRunComplete = true });
            first.Update(s => s with { LaunchAtStartup = false, ToggleHotkey = "Ctrl+Shift+F9" });
        } // quitting saves at once, without waiting for the debounce

        using var second = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        Assert.True(second.Current.FirstRunComplete);
        Assert.False(second.Current.LaunchAtStartup);
        Assert.Equal("Ctrl+Shift+F9", second.Current.ToggleHotkey);
        Assert.Equal(StartupWindow.Settings, new StartupOptions(false, false, false).WindowAtStart(second.Current.FirstRunComplete));
        Assert.Equal(StartupWindow.None, new StartupOptions(true, false, false).WindowAtStart(second.Current.FirstRunComplete));
    }

    [Fact]
    public void AnEmptyShortcutMeansNoneAndStaysEmpty()
    {
        using (var service = new SettingsService(CoreFactory.CreateSettingsStore(directory)))
            service.Update(s => s with { ToggleHotkey = "" }); // H-009: "Remove" on the Behavior page
        using var again = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        Assert.Equal("", again.Current.ToggleHotkey);
    }

    [Fact]
    public void ChangesAreSavedHalfASecondAfterTheLast()
    {
        using var service = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        string file = Path.Combine(directory, "settings.json");
        var seen = new List<Settings>();
        service.Changed += seen.Add;
        service.Update(s => s with { Brightness = 0.5f });
        service.Update(s => s); // no change: no event, no save
        Assert.Single(seen);
        Assert.False(File.Exists(file));
        Assert.True(SpinWait.SpinUntil(() => File.Exists(file), TimeSpan.FromSeconds(5)));
        Assert.Equal(0.5f, CoreFactory.CreateSettingsStore(directory).Load().Brightness);
    }

    [Fact]
    public void ASaveThatFailsIsTriedAgain()
    {
        // The file held by another process for a moment (an antivirus scan, on Windows): the change isn't lost.
        var store = new FailingStore(CoreFactory.CreateSettingsStore(directory), failures: 2);
        using var service = new SettingsService(store);
        service.Update(s => s with { Glow = 0.7f });
        Assert.True(SpinWait.SpinUntil(() => store.Saves == 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(3, store.Attempts);
        Assert.Equal(0.7f, CoreFactory.CreateSettingsStore(directory).Load().Glow);
    }

    [Fact]
    public void ABadFileStartsFromDefaultsAndIsKeptAside()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{ this is not json");
        using var service = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        Assert.Equal(new Settings(), service.Current);
        Assert.True(File.Exists(Path.Combine(directory, "settings.bad.json")));
    }

    [Fact]
    public void AnUpdateAfterQuittingIsIgnored()
    {
        var service = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        service.Dispose();
        service.Update(s => s with { Brightness = 0.3f }); // a window closing during exit
        Assert.Equal(new Settings().Brightness, service.Current.Brightness);
        Assert.False(File.Exists(Path.Combine(directory, "settings.json")));
    }

    [Fact]
    public void EachPresetShowsAsTheChosenOneUntilALookFieldChanges()
    {
        Assert.Equal(new[] { "Aurora", "Sunset", "Neon", "Ember", "Minimal" }, Presets.All.Select(p => p.Name));
        using var service = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        foreach ((string name, Func<Settings, Settings> apply) in Presets.All)
        {
            service.Update(apply); // the tray's "Presets" item or the Appearance chip
            Assert.Equal(new[] { name }, Chosen(service.Current));

            // Fields presets don't set leave it chosen: the same edits the pages write (doc 06 §1 ranges, upper-case hex).
            service.Update(s => s with { Sensitivity = 1.35f, FpsCap = 120, CornerRadiusDip = 12, CoverTaskbar = false });
            service.Update(s => s with { Monitors = MonitorSelection.Custom, CustomMonitorIds = ["ID-A", "ID-B"] });
            Assert.Equal(new[] { name }, Chosen(service.Current));

            // ... and so does a save and a load (validation keeps the same values and the same monitor list).
            service.Flush();
            Settings loaded = CoreFactory.CreateSettingsStore(directory).Load();
            Assert.Equal(new[] { name }, Chosen(loaded));

            // A look field the user moved off the preset's value: no preset is chosen.
            service.Update(s => s with { Glow = s.Glow == 0.5f ? 0.51f : 0.5f });
            Assert.Empty(Chosen(service.Current));
        }
        // Applying presets one after another gives the last one's look (only Minimal's Idle Glow lingers).
        service.Update(Presets.All[0].Apply);
        Assert.Equal(AnimationMode.IdleGlow, service.Current.Animation);
        Assert.True(service.Current.OverrideAlbumColor);
    }

    [Fact]
    public void APresetThatThrowsIsNeverChosen() =>
        Assert.False(PresetChoice.IsShowing(_ => throw new InvalidOperationException(), new Settings()));

    private static string[] Chosen(Settings settings) =>
        [.. Presets.All.Where(p => PresetChoice.IsShowing(p.Apply, settings)).Select(p => p.Name)];

    // A store whose first saves fail as a file in use would.
    private sealed class FailingStore(ISettingsStore inner, int failures) : ISettingsStore
    {
        private int attempts, saves;

        public int Attempts => Volatile.Read(ref attempts);
        public int Saves => Volatile.Read(ref saves);
        public string Path => inner.Path;

        public Settings Load() => inner.Load();

        public void Save(Settings settings)
        {
            if (Interlocked.Increment(ref attempts) <= failures)
                throw new IOException("The process cannot access the file because it is being used by another process.");
            inner.Save(settings);
            Interlocked.Increment(ref saves);
        }
    }
}
