import AppKit

/// Starts the menu bar app: an accessory app (no Dock icon; Info.plist's LSUIElement too), so its overlays can later
/// show over other apps' full-screen Spaces (docs/10-MACOS.md §3.1).
public enum RimlightApp {
    @MainActor
    public static func run() {
        // One copy at a time (doc 06 §4): a second launch hands its request to the first and quits.
        guard SingleInstance.claim() else {
            SingleInstance.notifyRunningCopy(demo: CommandLine.arguments.contains("--demo"))
            return
        }
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}
