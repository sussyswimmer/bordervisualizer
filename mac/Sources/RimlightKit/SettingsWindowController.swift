import AppKit
import SwiftUI

/// Shows the Settings window (doc 06 §3). Closing it only hides it; the app keeps running in the menu bar.
@MainActor
final class SettingsWindowController {
    private let model: SettingsModel
    private var window: NSWindow?

    init(model: SettingsModel) {
        self.model = model
    }

    func show() {
        if window == nil {
            let hosting = NSHostingController(rootView: SettingsView(model: model))
            let window = NSWindow(contentViewController: hosting)
            window.title = "\(Core.appName) Settings"
            window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
            window.setContentSize(NSSize(width: 760, height: 560))
            window.isReleasedWhenClosed = false
            window.center()
            window.setFrameAutosaveName("Settings")
            // Closing the window also ends the first run, so the glow starts hearing music either way.
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window,
                                                   queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.model.finishFirstRun() }
            }
            // The meter runs only while the window is on screen (closed, minimized or covered: nothing runs).
            NotificationCenter.default.addObserver(forName: NSWindow.didChangeOcclusionStateNotification,
                                                   object: window, queue: .main) { [weak self, weak window] _ in
                MainActor.assumeIsolated {
                    self?.model.setWindowVisible(window?.occlusionState.contains(.visible) == true)
                }
            }
            self.window = window
        }
        NSApp.activate()
        window?.makeKeyAndOrderFront(nil)
    }

    var isVisible: Bool { window?.isVisible == true }
}
