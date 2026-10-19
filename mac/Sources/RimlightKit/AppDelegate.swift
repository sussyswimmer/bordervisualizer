import AppKit
import Metal
import os

/// The menu bar app: the glow on every chosen screen (OverlayController) driven by the Mac's sound (SystemAudioTap),
/// Glow On, Demo Mode (`--demo` starts in it), a warning when the app can't hear the Mac, the settings folder and Quit.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "app")
    private var statusItem: NSStatusItem?
    private var loop: CoreLoop?
    private var overlays: OverlayController?
    private var glowItem: NSMenuItem?
    private var demoItem: NSMenuItem?
    private var cantHearItem: NSMenuItem?
    private var tap: SystemAudioTap?

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard Core.abiMatches else {
            fail("This copy of \(Core.appName) is damaged.", "Download it again and replace this copy.",
                 reason: "libRimlightCore speaks a different ABI than this app")
            return
        }

        let directory = AppPaths.supportDirectory
        let demo = CommandLine.arguments.contains("--demo")
        guard let loop = CoreLoop(settingsDirectory: directory.path, demo: demo) else {
            fail("\(Core.appName) couldn't start.", "Its settings couldn't be read.", reason: "rl_loop_create failed")
            return
        }
        self.loop = loop

        guard let device = MTLCreateSystemDefaultDevice() else {
            fail("\(Core.appName) can't draw on this Mac.", "No Metal graphics device was found.", reason: "No Metal device")
            return
        }
        tap = SystemAudioTap()
        if tap == nil { log.error("The audio ring couldn't be made; the glow won't hear music") }
        do {
            overlays = OverlayController(loop: loop, metal: try MetalGlow(device: device), tap: tap)
        } catch {
            fail("\(Core.appName) can't draw on this Mac.", error.localizedDescription,
                 reason: "Metal setup failed: \(error)")
            return
        }

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = StatusIcon.make()
        item.button?.toolTip = Core.appName
        item.button?.setAccessibilityLabel(Core.appName)
        item.menu = makeMenu()
        statusItem = item
        log.info("\(Core.appName, privacy: .public) \(Core.version, privacy: .public) started (demo: \(demo))")
    }

    private func makeMenu() -> NSMenu {
        let menu = NSMenu()
        menu.delegate = self
        let title = NSMenuItem(title: "\(Core.appName) \(Core.version)", action: nil, keyEquivalent: "")
        title.isEnabled = false
        menu.addItem(title)
        let cantHear = menu.addItem(withTitle: "\(Core.appName) can't hear your Mac. Allow it…",
                                    action: #selector(openAudioPrivacy), keyEquivalent: "")
        cantHear.target = self
        cantHear.isHidden = true
        cantHearItem = cantHear
        menu.addItem(.separator())
        let glow = menu.addItem(withTitle: "Glow On", action: #selector(toggleGlow), keyEquivalent: "")
        glow.target = self
        glowItem = glow
        let demo = menu.addItem(withTitle: "Demo Mode", action: #selector(toggleDemo), keyEquivalent: "")
        demo.target = self
        demoItem = demo
        menu.addItem(.separator())
        menu.addItem(withTitle: "Open Settings Folder", action: #selector(openSettingsFolder), keyEquivalent: "")
            .target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit \(Core.appName)", action: #selector(NSApplication.terminate(_:)),
                     keyEquivalent: "q")
        return menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        glowItem?.state = loop?.settings?.enabled == true ? .on : .off
        demoItem?.state = loop?.demo == true ? .on : .off
        statusItem?.button?.appearsDisabled = loop?.settings?.enabled == false
        cantHearItem?.isHidden = tap?.cantHear != true
    }

    /// Privacy & Security › Screen & System Audio Recording, where "System Audio Recording Only" lists the app.
    @objc private func openAudioPrivacy() {
        let pane = "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture"
        if let url = URL(string: pane) { NSWorkspace.shared.open(url) }
    }

    @objc private func toggleGlow() {
        guard let loop, var settings = loop.settings else { return }
        settings.enabled.toggle()
        let status = loop.apply(settings)
        if status != 0 { log.error("Saving the settings failed: \(status)") }
        statusItem?.button?.appearsDisabled = !settings.enabled
        overlays?.settingsChanged()
    }

    @objc private func toggleDemo() {
        guard let loop else { return }
        loop.demo.toggle()
        overlays?.settingsChanged()
    }

    /// Writes settings.json if it isn't there yet (Core's defaults, in the same format as Windows) and shows it in
    /// Finder.
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

    private func fail(_ message: String, _ detail: String, reason: String) {
        log.error("\(reason, privacy: .public)")
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = detail
        alert.runModal()
        NSApp.terminate(nil)
    }
}
