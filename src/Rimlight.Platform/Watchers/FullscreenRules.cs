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
    // foreground: null when there is none, or it doesn't count (the shell's desktop, taskbar, Alt+Tab or Task View, or
    // one of our windows). previous: the last decision's entries, each with its window's facts now (null: closed,
    // hidden, minimized or cloaked).
    public static FullscreenVerdict Decide(NotificationState state, WindowFacts? foreground,
        IReadOnlyList<(FullscreenEntry Entry, WindowFacts? Now)> previous)
    {
        // Not tied to a window: a screen saver, presentation settings.
        bool everywhere = state is NotificationState.NotPresent or NotificationState.PresentationMode;
        var entries = new List<FullscreenEntry>(previous.Count + 1);

        if (foreground is { } window)
        {
            // Doc 04 §4: the foreground window exactly covers its monitor (borderless games, fullscreen video). A
            // maximized window never matches: its rectangle includes resize borders outside the monitor. When Windows
            // says fullscreen (Busy: it also covers the monitor, but not exactly and not maximized; D3D: exclusive
            // mode), only that window's monitor pauses too, so other monitors keep their glow (doc 07 Phase 5).
            bool exact = window.Bounds == window.MonitorBounds;
            bool loose = !exact && (state == NotificationState.D3DFullscreen
                || (state == NotificationState.Busy && !window.Maximized && window.Bounds.Contains(window.MonitorBounds)));
            if (exact || loose) entries.Add(new FullscreenEntry(window.Window, window.Monitor, loose));
        }
        else if (state == NotificationState.D3DFullscreen)
        {
            everywhere = true; // an exclusive-mode app, but no window to tie it to
        }

        // A fullscreen window keeps its monitor paused when another monitor gets the focus (a video on one screen while
        // working on the other), until it leaves fullscreen, moves, is minimized, hidden or closed, or another window
        // is activated on its monitor.
        foreach ((FullscreenEntry entry, WindowFacts? now) in previous)
        {
            if (now is not { } facts || facts.Monitor != entry.Monitor) continue;
            bool covers = entry.Loose ? facts.Bounds.Contains(facts.MonitorBounds) : facts.Bounds == facts.MonitorBounds;
            if (!covers) continue;
            if (foreground is { } active && active.Monitor == entry.Monitor && active.Window != entry.Window) continue;
            if (entries.Exists(e => e.Window == entry.Window || e.Monitor == entry.Monitor)) continue;
            entries.Add(entry);
        }

        return new FullscreenVerdict(everywhere, [.. entries]);
    }
}
