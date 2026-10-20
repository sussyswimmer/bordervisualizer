import AppKit
import os

/// The menu bar app. M0: the icon and its menu (name and version from Core, the settings folder, Quit).
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "app")
    private var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard Core.abiMatches else {
            log.error("libRimlightCore speaks a different ABI than this app")
            let alert = NSAlert()
            alert.messageText = "This copy of \(Core.appName) is damaged."
            alert.informativeText = "Download it again and replace this copy."
            alert.runModal()
            NSApp.terminate(nil)
            return
        }

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = StatusIcon.make()
        item.button?.toolTip = Core.appName
        item.button?.setAccessibilityLabel(Core.appName)
        item.menu = makeMenu()
        statusItem = item
        log.info("\(Core.appName, privacy: .public) \(Core.version, privacy: .public) started")
    }

    private func makeMenu() -> NSMenu {
        let menu = NSMenu()
        let title = NSMenuItem(title: "\(Core.appName) \(Core.version)", action: nil, keyEquivalent: "")
        title.isEnabled = false
        menu.addItem(title)
        menu.addItem(.separator())
        menu.addItem(withTitle: "Open Settings Folder", action: #selector(openSettingsFolder), keyEquivalent: "")
            .target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit \(Core.appName)", action: #selector(NSApplication.terminate(_:)),
                     keyEquivalent: "q")
        return menu
    }

    /// Writes settings.json if it isn't there yet (Core's defaults, in the same format as Windows) and shows it in
    /// Finder. Loading and saving it back also normalizes a hand-edited file, as Core's store does on Windows.
    @objc private func openSettingsFolder() {
        let directory = AppPaths.supportDirectory
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        } catch {
            log.error("Creating the settings folder failed: \(error.localizedDescription, privacy: .public)")
        }
        if let json = Core.loadSettings(directory: directory.path) {
            let status = Core.saveSettings(directory: directory.path, json: json)
            if status != 0 { log.error("Saving the settings failed: \(status)") }
        }
        if FileManager.default.fileExists(atPath: AppPaths.settingsFile.path) {
            NSWorkspace.shared.activateFileViewerSelecting([AppPaths.settingsFile])
        } else {
            NSWorkspace.shared.open(directory)
        }
    }
}
