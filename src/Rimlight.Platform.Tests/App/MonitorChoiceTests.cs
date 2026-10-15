using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// MonitorChoice (K7, doc 06 §1, H-009): the Displays page's choices. Custom lists hold stable IDs, compared ordinally,
// and keep the IDs of monitors that are unplugged right now.
public sealed class MonitorChoiceTests
{
    private static readonly MonitorInfo Main = Monitor("ID-MAIN", primary: true);
    private static readonly MonitorInfo Side = Monitor("ID-SIDE");
    private static readonly MonitorInfo Tv = Monitor("ID-TV");
    private static readonly MonitorInfo[] Attached = [Main, Side];

    [Fact]
    public void LitFollowsTheSelection()
    {
        Assert.True(MonitorChoice.IsLit(new Settings(), Side));
        Assert.True(MonitorChoice.IsLit(new Settings { Monitors = MonitorSelection.PrimaryOnly }, Main));
        Assert.False(MonitorChoice.IsLit(new Settings { Monitors = MonitorSelection.PrimaryOnly }, Side));
        var custom = new Settings { Monitors = MonitorSelection.Custom, CustomMonitorIds = ["ID-SIDE"] };
        Assert.True(MonitorChoice.IsLit(custom, Side));
        Assert.False(MonitorChoice.IsLit(custom, Main));
        Assert.False(MonitorChoice.IsLit(custom with { CustomMonitorIds = ["id-side"] }, Side)); // ordinal
        Assert.False(MonitorChoice.IsLit(custom with { CustomMonitorIds = null! }, Side));
    }

    [Fact]
    public void ChooseStartsFromWhatIsLitNow()
    {
        Settings all = MonitorChoice.ChooseDisplays(new Settings(), Attached);
        Assert.Equal(MonitorSelection.Custom, all.Monitors);
        Assert.Equal(new[] { "ID-MAIN", "ID-SIDE" }, all.CustomMonitorIds);

        Settings primary = MonitorChoice.ChooseDisplays(new Settings { Monitors = MonitorSelection.PrimaryOnly }, Attached);
        Assert.Equal(new[] { "ID-MAIN" }, primary.CustomMonitorIds);

        Settings already = new() { Monitors = MonitorSelection.Custom, CustomMonitorIds = ["ID-SIDE"] };
        Assert.Same(already, MonitorChoice.ChooseDisplays(already, Attached));
    }

    [Fact]
    public void ChooseRestoresAPreviousListThatLightsAnAttachedMonitor()
    {
        var previous = new Settings { Monitors = MonitorSelection.All, CustomMonitorIds = ["ID-TV", "ID-SIDE"] };
        Settings chosen = MonitorChoice.ChooseDisplays(previous, Attached);
        Assert.Equal(MonitorSelection.Custom, chosen.Monitors);
        Assert.Same(previous.CustomMonitorIds, chosen.CustomMonitorIds);

        // A list that lights only unplugged monitors would show nothing: start from what is lit, keeping the TV's ID.
        var stale = new Settings { Monitors = MonitorSelection.PrimaryOnly, CustomMonitorIds = ["ID-TV"] };
        Assert.Equal(new[] { "ID-TV", "ID-MAIN" }, MonitorChoice.ChooseDisplays(stale, Attached).CustomMonitorIds);
    }

    [Fact]
    public void ToggleChangesOnlyThatMonitorAndKeepsUnpluggedOnes()
    {
        var custom = new Settings { Monitors = MonitorSelection.Custom, CustomMonitorIds = ["ID-TV", "ID-MAIN"] };
        Assert.Equal(new[] { "ID-TV", "ID-MAIN", "ID-SIDE" }, MonitorChoice.Toggle(custom, Attached, Side, lit: true).CustomMonitorIds);
        Assert.Equal(new[] { "ID-TV" }, MonitorChoice.Toggle(custom, Attached, Main, lit: false).CustomMonitorIds);
        // Toggling on twice doesn't duplicate.
        Assert.Equal(new[] { "ID-TV", "ID-MAIN" }, MonitorChoice.Toggle(custom, Attached, Main, lit: true).CustomMonitorIds);

        // From All or Main display, a toggle switches to Choose starting from what is lit, so only that monitor changes.
        Settings fromAll = MonitorChoice.Toggle(new Settings(), Attached, Side, lit: false);
        Assert.Equal(MonitorSelection.Custom, fromAll.Monitors);
        Assert.Equal(new[] { "ID-MAIN" }, fromAll.CustomMonitorIds);
        Settings fromPrimary = MonitorChoice.Toggle(new Settings { Monitors = MonitorSelection.PrimaryOnly, CustomMonitorIds = ["ID-TV"] },
            Attached, Side, lit: true);
        Assert.Equal(new[] { "ID-TV", "ID-MAIN", "ID-SIDE" }, fromPrimary.CustomMonitorIds);
    }

    [Fact]
    public void OnlyNullAndEmptyEntriesAreDropped()
    {
        // H-009: whitespace and other odd IDs are opaque and kept, like C7 keeps them.
        var custom = new Settings { Monitors = MonitorSelection.Custom, CustomMonitorIds = ["", " ", null!, "ID-MAIN"] };
        Assert.Equal(new[] { " ", "ID-MAIN", "ID-SIDE" }, MonitorChoice.Toggle(custom, Attached, Side, lit: true).CustomMonitorIds);
        Assert.Equal(new[] { " ", "ID-MAIN" }, MonitorChoice.Toggle(custom, [Main, Side, Tv], Tv, lit: false).CustomMonitorIds);
    }

    private static MonitorInfo Monitor(string id, bool primary = false) =>
        new(id, @"\\.\DISPLAY" + id.Length, null, false, primary, 0, 0, 1920, 1080, 60);
}
