namespace Rimlight.Platform.Watchers;

// What SHQueryUserNotificationState reports, reduced to what the fullscreen rules use.
internal enum NotificationState
{
    Normal,
    NotPresent,       // QUNS_NOT_PRESENT: a screen saver runs, or the session isn't the active one
    Busy,             // QUNS_BUSY: Windows considers the foreground window fullscreen
    D3DFullscreen,    // QUNS_RUNNING_D3D_FULL_SCREEN: an exclusive-mode Direct3D app
    PresentationMode, // QUNS_PRESENTATION_MODE: the user turned on presentation settings
}

// A rectangle in physical pixels (right and bottom exclusive), as GetWindowRect and GetMonitorInfo report it.
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(PixelRect other) =>
        Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;
}

// A shown top-level window as the rules see it: its rectangle, and the monitor it is on (GDI device name and rectangle).
internal readonly record struct WindowFacts(nint Window, PixelRect Bounds, bool Maximized, string Monitor, PixelRect MonitorBounds);

// A monitor paused because this window is fullscreen on it. Loose: the window covers the monitor without matching it
// exactly, and Windows itself called it fullscreen.
internal readonly record struct FullscreenEntry(nint Window, string Monitor, bool Loose);

internal readonly record struct FullscreenVerdict(bool Everywhere, FullscreenEntry[] Entries);

// Decides what fullscreen apps pause (doc 04 §4). Pure: FullscreenDetector gathers the facts from Windows.
internal static class FullscreenRules
{
    // foreground: null when it doesn't count (the shell's desktop, taskbar, Alt+Tab or Task View, one of our windows),
    // isn't shown, or there is none. noForeground: Windows reports no foreground window at all. previous: the last
    // decision's entries, each with its window's facts now (null: closed, hidden, minimized or cloaked).
    public static FullscreenVerdict Decide(NotificationState state, bool noForeground, WindowFacts? foreground,
        IReadOnlyList<(FullscreenEntry Entry, WindowFacts? Now)> previous)
    {
        // Not tied to a window: a screen saver, presentation settings, or an exclusive-mode app while no window has
        // the focus.
        bool everywhere = state is NotificationState.NotPresent or NotificationState.PresentationMode
            || (state == NotificationState.D3DFullscreen && noForeground);
        var entries = new List<FullscreenEntry>(previous.Count + 1);

        if (foreground is { } window)
        {
            // Doc 04 §4: the foreground window exactly covers its monitor (borderless games, fullscreen video). When
            // Windows says fullscreen (Busy; D3D: an exclusive-mode app, whose window fills its output), a window that
            // covers its monitor without matching it exactly counts too. Only that window's monitor pauses, so other
            // monitors keep their glow (doc 07 Phase 5). Windows' state alone never pauses a window that doesn't cover
            // its monitor: D3D is still reported for a moment after switching out of an exclusive-mode game, when the
            // foreground is already some other window.
            bool exact = window.Bounds == window.MonitorBounds;
            bool loose = !exact && (state is NotificationState.Busy or NotificationState.D3DFullscreen) && CoversLoosely(window);
            if (exact || loose) entries.Add(new FullscreenEntry(window.Window, window.Monitor, loose));
        }

        // A fullscreen window keeps its monitor paused when another monitor gets the focus (a video on one screen while
        // working on the other), until it leaves fullscreen, moves, is minimized, hidden or closed, or another window
        // is activated on its monitor.
        foreach ((FullscreenEntry entry, WindowFacts? now) in previous)
        {
            if (now is not { } facts || facts.Monitor != entry.Monitor) continue;
            bool covers = entry.Loose ? CoversLoosely(facts) : facts.Bounds == facts.MonitorBounds;
            if (!covers) continue;
            if (foreground is { } active && active.Monitor == entry.Monitor && active.Window != entry.Window) continue;
            if (entries.Exists(e => e.Window == entry.Window || e.Monitor == entry.Monitor)) continue;
            entries.Add(entry);
        }

        return new FullscreenVerdict(everywhere, [.. entries]);
    }

    // Covers its monitor, and isn't maximized: a maximized window's rectangle includes resize borders outside its work
    // area, which on a monitor without a taskbar is the whole monitor.
    private static bool CoversLoosely(WindowFacts window) => !window.Maximized && window.Bounds.Contains(window.MonitorBounds);
}
