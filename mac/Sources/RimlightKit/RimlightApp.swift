import AppKit

/// Starts the menu bar app: an accessory app (no Dock icon; Info.plist's LSUIElement too), so its overlays can later
/// show over other apps' full-screen Spaces (docs/10-MACOS.md §3.1).
public enum RimlightApp {
    @MainActor
    public static func run() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}
