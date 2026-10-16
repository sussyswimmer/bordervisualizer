using Rimlight.Platform.Watchers;
using Xunit;

namespace Rimlight.Platform.Tests;

// FullscreenRules (K5, doc 04 §4, doc 07 Phase 5): which fullscreen apps pause the glow, and where.
public sealed class FullscreenRulesTests
{
    private const string Left = @"\\.\DISPLAY1";
    private const string Right = @"\\.\DISPLAY2";
    private static readonly PixelRect LeftBounds = new(0, 0, 1920, 1080);
    private static readonly PixelRect RightBounds = new(1920, 0, 4480, 1440);
    private static readonly (FullscreenEntry, WindowFacts?)[] NoneBefore = [];

    [Fact]
    public void AWindowExactlyCoveringItsMonitorPausesThatMonitorOnly()
    {
        FullscreenVerdict verdict = Decide(NotificationState.Normal, Window(1, LeftBounds, Left), NoneBefore);
        Assert.False(verdict.Everywhere);
        Assert.Equal(new[] { new FullscreenEntry(1, Left, Loose: false) }, verdict.Entries);

        verdict = Decide(NotificationState.Normal, Window(2, RightBounds, Right), NoneBefore);
        Assert.Equal(new[] { new FullscreenEntry(2, Right, Loose: false) }, verdict.Entries);
    }

    [Theory]
    [InlineData(NotificationStateName.Normal)]
    [InlineData(NotificationStateName.Busy)]
    public void AMaximizedWindowNeverPauses(NotificationStateName state)
    {
        // Its rectangle reaches past the monitor by the resize borders.
        var bounds = new PixelRect(-8, -8, 1928, 1088);
        FullscreenVerdict verdict = Decide((NotificationState)state, Window(1, bounds, Left, maximized: true), NoneBefore);
        Assert.Empty(verdict.Entries);
        Assert.False(verdict.Everywhere);
    }

    [Fact]
    public void BusyCountsAWindowThatCoversItsMonitorLoosely()
    {
        var bounds = new PixelRect(-1, -1, 1921, 1081);
        Assert.Empty(Decide(NotificationState.Normal, Window(1, bounds, Left), NoneBefore).Entries);
        FullscreenVerdict verdict = Decide(NotificationState.Busy, Window(1, bounds, Left), NoneBefore);
        Assert.Equal(new[] { new FullscreenEntry(1, Left, Loose: true) }, verdict.Entries);
        Assert.False(verdict.Everywhere); // QUNS_BUSY never pauses every monitor
        // A smaller window doesn't count, even when Windows says busy.
        Assert.Empty(Decide(NotificationState.Busy, Window(1, new PixelRect(0, 0, 1920, 1040), Left), NoneBefore).Entries);
    }

    [Fact]
    public void ExclusiveDirect3DPausesAWindowThatCoversItsMonitorOrEverywhereWithoutAForegroundWindow()
    {
        // An exclusive-mode window fills its output: it covers its monitor, loosely (K5's review).
        var covering = new PixelRect(-1, -1, 1921, 1081);
        FullscreenVerdict verdict = Decide(NotificationState.D3DFullscreen, Window(1, covering, Left), NoneBefore);
        Assert.Equal(new[] { new FullscreenEntry(1, Left, Loose: true) }, verdict.Entries);
        Assert.False(verdict.Everywhere);
        Assert.Equal(new[] { new FullscreenEntry(1, Left, Loose: false) },
            Decide(NotificationState.D3DFullscreen, Window(1, LeftBounds, Left), NoneBefore).Entries);

        // Windows' state alone pauses nothing: a small or maximized foreground window, or one that doesn't count.
        Assert.Empty(Decide(NotificationState.D3DFullscreen, Window(1, new PixelRect(0, 0, 800, 600), Left), NoneBefore).Entries);
        Assert.Empty(Decide(NotificationState.D3DFullscreen, Window(1, new PixelRect(-8, -8, 1928, 1088), Left, maximized: true), NoneBefore).Entries);
        verdict = Decide(NotificationState.D3DFullscreen, null, NoneBefore);
        Assert.Empty(verdict.Entries);
        Assert.False(verdict.Everywhere);

        // No foreground window at all: nothing to tie the exclusive-mode app to, so every monitor pauses.
        verdict = Decide(NotificationState.D3DFullscreen, null, NoneBefore, noForeground: true);
        Assert.True(verdict.Everywhere);
        Assert.Empty(verdict.Entries);
        Assert.False(Decide(NotificationState.Busy, null, NoneBefore, noForeground: true).Everywhere);
    }

    [Fact]
    public void LeavingAnExclusiveGameWhileWindowsStillSaysDirect3DPausesOnlyTheGamesMonitor()
    {
        // D3D is still reported for a moment after Alt+Tab out of an exclusive-mode game (K5's review).
        var game = new FullscreenEntry(1, Left, Loose: true);
        (FullscreenEntry, WindowFacts?)[] before = [(game, Window(1, new PixelRect(-1, -1, 1921, 1081), Left))];
        WindowFacts browser = Window(2, new PixelRect(2000, 100, 3000, 900), Right);
        WindowFacts maximized = Window(3, new PixelRect(1912, -8, 4488, 1408), Right, maximized: true);
        Assert.Equal(new[] { game }, Decide(NotificationState.D3DFullscreen, browser, before).Entries);
        Assert.Equal(new[] { game }, Decide(NotificationState.D3DFullscreen, maximized, before).Entries);
        FullscreenVerdict desktop = Decide(NotificationState.D3DFullscreen, null, before); // a click on the desktop
        Assert.Equal(new[] { game }, desktop.Entries);
        Assert.False(desktop.Everywhere);
    }

    [Fact]
    public void FileExplorerOrOurSettingsWindowEndsAStickyPauseButNeverPauses()
    {
        // K5's review: windows the user works in (File Explorer, Run and Properties in the shell's process, our settings
        // window) end a sticky pause on their monitor, but never count as a fullscreen app themselves.
        var video = new FullscreenEntry(1, Left, Loose: false);
        (FullscreenEntry, WindowFacts?)[] before = [(video, Window(1, LeftBounds, Left))];
        WindowFacts explorerLeft = Window(4, new PixelRect(100, 100, 900, 700), Left);
        WindowFacts explorerRight = Window(4, new PixelRect(2000, 100, 3000, 900), Right);
        Assert.Empty(Decide(NotificationState.Normal, explorerLeft, before, mayBeFullscreen: false).Entries);
        Assert.Empty(Decide(NotificationState.D3DFullscreen, explorerLeft, before, mayBeFullscreen: false).Entries);
        Assert.Equal(new[] { video }, Decide(NotificationState.Normal, explorerRight, before, mayBeFullscreen: false).Entries);

        // Even covering a monitor, with Windows calling it fullscreen.
        foreach (NotificationState state in new[] { NotificationState.Normal, NotificationState.Busy, NotificationState.D3DFullscreen })
        {
            FullscreenVerdict verdict = Decide(state, Window(4, RightBounds, Right), NoneBefore, mayBeFullscreen: false);
            Assert.Empty(verdict.Entries);
            Assert.False(verdict.Everywhere);
        }
    }

    [Theory]
    [InlineData(NotificationStateName.NotPresent)]        // a screen saver
    [InlineData(NotificationStateName.PresentationMode)]  // presentation settings
    public void AScreenSaverOrPresentationModePausesEverywhere(NotificationStateName state)
    {
        Assert.True(Decide((NotificationState)state, null, NoneBefore).Everywhere);
        Assert.True(Decide((NotificationState)state, Window(1, new PixelRect(0, 0, 10, 10), Left), NoneBefore).Everywhere);
    }

    [Fact]
    public void AFullscreenVideoStaysPausedWhileTheFocusIsOnAnotherMonitor()
    {
        var video = new FullscreenEntry(1, Left, Loose: false);
        WindowFacts editor = Window(2, new PixelRect(2000, 100, 3000, 900), Right);
        FullscreenVerdict verdict = Decide(NotificationState.Normal, editor, [(video, Window(1, LeftBounds, Left))]);
        Assert.Equal(new[] { video }, verdict.Entries);
    }

    [Fact]
    public void ThePauseEndsWhenTheVideoLeavesFullscreenMovesOrCloses()
    {
        var video = new FullscreenEntry(1, Left, Loose: false);
        WindowFacts editor = Window(2, new PixelRect(2000, 100, 3000, 900), Right);
        // Left fullscreen (a window again), moved to the other monitor, or closed/minimized/hidden/cloaked (no facts).
        Assert.Empty(Decide(NotificationState.Normal, editor, [(video, Window(1, new PixelRect(100, 100, 900, 700), Left))]).Entries);
        Assert.Empty(Decide(NotificationState.Normal, editor, [(video, Window(1, RightBounds, Right))]).Entries);
        Assert.Empty(Decide(NotificationState.Normal, editor, [(video, null)]).Entries);
    }

    [Fact]
    public void AnotherWindowActivatedOnThatMonitorEndsThePause()
    {
        var video = new FullscreenEntry(1, Left, Loose: false);
        WindowFacts explorer = Window(3, new PixelRect(100, 100, 900, 700), Left);
        Assert.Empty(Decide(NotificationState.Normal, explorer, [(video, Window(1, LeftBounds, Left))]).Entries);
        // The video itself in the foreground again keeps it (one entry, not two).
        Assert.Equal(new[] { video }, Decide(NotificationState.Normal, Window(1, LeftBounds, Left), [(video, Window(1, LeftBounds, Left))]).Entries);
    }

    [Fact]
    public void ALooseEntryKeepsTheLooseRule()
    {
        var game = new FullscreenEntry(1, Left, Loose: true);
        var bounds = new PixelRect(-1, -1, 1921, 1081);
        Assert.Equal(new[] { game }, Decide(NotificationState.Normal, null, [(game, Window(1, bounds, Left))]).Entries);
        Assert.Empty(Decide(NotificationState.Normal, null, [(game, Window(1, new PixelRect(0, 0, 1920, 1000), Left))]).Entries);
        // Maximized later: its rectangle still covers the monitor, but only by the resize borders.
        Assert.Empty(Decide(NotificationState.Normal, null, [(game, Window(1, new PixelRect(-8, -8, 1928, 1088), Left, maximized: true))]).Entries);
    }

    [Fact]
    public void TwoFullscreenAppsPauseBothMonitors()
    {
        var video = new FullscreenEntry(1, Left, Loose: false);
        FullscreenVerdict verdict = Decide(NotificationState.Normal, Window(2, RightBounds, Right), [(video, Window(1, LeftBounds, Left))]);
        Assert.Equal(new[] { new FullscreenEntry(2, Right, Loose: false), video }, verdict.Entries);
    }

    [Fact]
    public void SystemStateSaysWhenToPauseEverywhere()
    {
        var idle = new SystemState();
        Assert.False(idle.PausesEverywhere);
        Assert.True((idle with { Locked = true }).PausesEverywhere);
        Assert.True((idle with { Disconnected = true }).PausesEverywhere);
        Assert.True((idle with { Suspended = true }).PausesEverywhere);
        Assert.True((idle with { DisplayOff = true }).PausesEverywhere);
        Assert.True((idle with { FullscreenEverywhere = true }).PausesEverywhere);
        Assert.False((idle with { OnBattery = true }).PausesEverywhere);           // battery is "On battery"'s business
        Assert.False((idle with { RemoteSession = true }).PausesEverywhere);       // a connected remote session glows
        Assert.False((idle with { FullscreenMonitors = [Left] }).PausesEverywhere); // per monitor only
    }

    [Fact]
    public void SystemStateEqualityTellsARealChange()
    {
        // The watcher keeps the same monitor list instance until the set changes, so record equality is a real change.
        string[] monitors = [Left];
        var a = new SystemState { OnBattery = true, FullscreenMonitors = monitors };
        Assert.Equal(a, a with { });
        Assert.Equal(a, new SystemState { OnBattery = true, FullscreenMonitors = monitors });
        Assert.NotEqual(a, a with { Locked = true });
        Assert.NotEqual(a, a with { FullscreenMonitors = [Left] });
    }

    [Fact]
    public void SystemStateDescribesItselfForTheLog()
    {
        Assert.Equal("unlocked, display on, AC, no fullscreen", new SystemState().ToString());
        Assert.Equal("locked, remote session, display off, battery, fullscreen on " + Left + " " + Right,
            new SystemState { Locked = true, RemoteSession = true, DisplayOff = true, OnBattery = true, FullscreenMonitors = [Left, Right] }.ToString());
        Assert.Equal("unlocked, disconnected, suspended, display on, AC, fullscreen everywhere",
            new SystemState { Disconnected = true, RemoteSession = true, Suspended = true, FullscreenEverywhere = true, FullscreenMonitors = [Left] }.ToString());
    }

    // NotificationState is internal; theories name it through this public mirror.
    public enum NotificationStateName
    {
        Normal = (int)NotificationState.Normal,
        NotPresent = (int)NotificationState.NotPresent,
        Busy = (int)NotificationState.Busy,
        D3DFullscreen = (int)NotificationState.D3DFullscreen,
        PresentationMode = (int)NotificationState.PresentationMode,
    }

    // FullscreenRules.Decide as FullscreenDetector calls it. foreground: null when it doesn't count (the desktop, the
    // taskbar, our tray menu) or isn't shown; noForeground: Windows reports none at all; mayBeFullscreen: false for
    // windows that only end a sticky pause (File Explorer, the shell's dialogs, our settings window).
    private static FullscreenVerdict Decide(NotificationState state, WindowFacts? foreground,
        IReadOnlyList<(FullscreenEntry Entry, WindowFacts? Now)> previous, bool mayBeFullscreen = true, bool noForeground = false) =>
        FullscreenRules.Decide(state, noForeground, foreground, mayBeFullscreen, previous);

    private static WindowFacts Window(nint handle, PixelRect bounds, string monitor, bool maximized = false) =>
        new(handle, bounds, maximized, monitor, monitor == Left ? LeftBounds : RightBounds);
}
