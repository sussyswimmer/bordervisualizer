import AppKit
import CRimlight
import Metal
import os

/// The menu bar app (doc 06 §2): the glow on every chosen screen (OverlayController) driven by the Mac's sound
/// (SystemAudioTap), its menu, the Settings window, and the first run.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "app")
    private var statusItem: NSStatusItem?
    private var loop: CoreLoop?
    private var tap: SystemAudioTap?
    private var overlays: OverlayController?
    private var model: SettingsModel?
    private var settingsWindow: SettingsWindowController?
    private var media: NowPlayingService?
    private var watcher: SystemWatcher?
    private var hotkey: GlobalHotkey?
    private var appliedHotkey: String?
    private var appliedLogin: Bool?

    // Menu items whose state follows the settings (menuNeedsUpdate).
    private var glowItem: NSMenuItem?
    private var demoItem: NSMenuItem?
    private var cantHearItem: NSMenuItem?
    private var modeItems: [(NSMenuItem, String)] = []
    private var colorItems: [(NSMenuItem, String)] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard Core.abiMatches else {
            fail("This copy of \(Core.appName) is damaged.", "Download it again and replace this copy.",
                 reason: "libRimlightCore speaks a different ABI than this app")
            return
        }
        let demo = CommandLine.arguments.contains("--demo")
        guard let loop = CoreLoop(settingsDirectory: AppPaths.supportDirectory.path, demo: demo) else {
            fail("\(Core.appName) couldn't start.", "Its settings couldn't be read.", reason: "rl_loop_create failed")
            return
        }
        self.loop = loop
        guard let device = MTLCreateSystemDefaultDevice() else {
            fail("\(Core.appName) can't draw on this Mac.", "No Metal graphics device was found.",
                 reason: "No Metal device")
            return
        }

        tap = SystemAudioTap()
        if tap == nil { log.error("The audio ring couldn't be made; the glow won't hear music") }
        let firstRun = loop.settings?.firstRunComplete == false
        // The first run explains the "System Audio Recording" prompt before the tap may open (10-MACOS §2.1).
        if firstRun { tap?.setAllowed(false) }

        do {
            overlays = OverlayController(loop: loop, metal: try MetalGlow(device: device), tap: tap)
        } catch {
            fail("\(Core.appName) can't draw on this Mac.", error.localizedDescription,
                 reason: "Metal setup failed: \(error)")
            return
        }
        let model = SettingsModel(loop: loop, device: device, tap: tap) { [weak self] in
            self?.settingsChanged()
        }
        self.model = model
        settingsWindow = SettingsWindowController(model: model)

        // Now playing and album colours (M4): the tooltip, the Color page, and the palette for the glow.
        let media = NowPlayingService(loop: loop) { [weak self] track, denied in
            self?.model?.mediaChanged(track, automationDenied: denied)
            self?.updateStatusItem()
        }
        media.start()
        media.setAlbumColorsWanted(model.usesAlbumColors)
        self.media = media

        // System events (M5): sleep, displays, lock, power, full-screen windows.
        let watcher = SystemWatcher(
            callbacks: .init(
                paused: { [weak self] paused in
                    self?.loop?.setPaused(paused)
                    self?.overlays?.settingsChanged()
                },
                power: { [weak self] battery, lowPower in
                    self?.overlays?.setPower(onBattery: battery, lowPower: lowPower)
                },
                fullscreen: { [weak self] displays in self?.overlays?.setFullscreen(displays) },
                woke: { [weak self] in
                    self?.overlays?.wake()
                    self?.tap?.restartAfterWake()
                },
                tick: { [weak self] in self?.overlays?.checkLayout() }),
            screens: { [weak self] in self?.overlays?.overlaidScreens ?? [] },
            checksFullscreen: { [weak self] in
                guard let settings = self?.model?.settings else { return false }
                return settings.pauseInFullscreen && settings.enabled && settings.animation != "Off"
            })
        watcher.start()
        self.watcher = watcher

        // The shortcut, the login item, and a second launch's requests (M6).
        hotkey = GlobalHotkey { [weak self] in self?.toggleGlow() }
        let distributed = DistributedNotificationCenter.default()
        distributed.addObserver(forName: SingleInstance.showSettingsName, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.showSettings() }
        }
        distributed.addObserver(forName: SingleInstance.demoName, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, let loop = self.loop, !loop.demo else { return }
                loop.demo = true
                self.overlays?.settingsChanged()
            }
        }

        NSApp.mainMenu = Self.makeMainMenu()
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = StatusIcon.make()
        item.button?.setAccessibilityLabel(Core.appName)
        item.menu = makeMenu()
        statusItem = item
        updateStatusItem()
        applyShell() // after the menu exists: Glow On shows the shortcut
        log.info("\(Core.appName, privacy: .public) \(Core.version, privacy: .public) started (demo: \(demo))")

        if firstRun { settingsWindow?.show() } // PRD §7: the first launch shows Settings with a welcome
    }

    func applicationWillTerminate(_ notification: Notification) {
        model?.saveNow()
    }

    // MARK: - The menu bar menu (doc 06 §2)

    private func makeMenu() -> NSMenu {
        let menu = NSMenu()
        menu.delegate = self
        let glow = menu.addItem(withTitle: "Glow On", action: #selector(toggleGlow), keyEquivalent: "")
        glow.target = self
        glowItem = glow
        let cantHear = menu.addItem(withTitle: "\(Core.appName) can't hear your Mac. Allow it…",
                                    action: #selector(openAudioPrivacy), keyEquivalent: "")
        cantHear.target = self
        cantHear.isHidden = true
        cantHearItem = cantHear
        menu.addItem(.separator())

        let mode = NSMenu()
        for (title, value) in [("Music Sync", "MusicSync"), ("Idle Glow", "IdleGlow"), ("Off", "Off")] {
            let item = mode.addItem(withTitle: title, action: #selector(chooseMode(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = value
            modeItems.append((item, value))
        }
        menu.addItem(withTitle: "Mode", action: nil, keyEquivalent: "").submenu = mode

        let colors = NSMenu()
        for (title, value) in [("From Album Art", "AlbumArt"), ("Manual", "Manual")] {
            let item = colors.addItem(withTitle: title, action: #selector(chooseColors(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = value
            colorItems.append((item, value))
        }
        menu.addItem(withTitle: "Colors", action: nil, keyEquivalent: "").submenu = colors

        let presets = NSMenu()
        for preset in model?.presets ?? [] {
            let item = presets.addItem(withTitle: preset.name, action: #selector(choosePreset(_:)), keyEquivalent: "")
            item.target = self
            item.tag = Int(preset.index)
        }
        menu.addItem(withTitle: "Presets", action: nil, keyEquivalent: "").submenu = presets
        menu.addItem(.separator())

        let demo = menu.addItem(withTitle: "Demo Mode", action: #selector(toggleDemo), keyEquivalent: "")
        demo.target = self
        demoItem = demo
        menu.addItem(withTitle: "Settings…", action: #selector(showSettings), keyEquivalent: ",").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit \(Core.appName)", action: #selector(NSApplication.terminate(_:)),
                     keyEquivalent: "q")
        return menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        guard let settings = model?.settings else { return }
        glowItem?.state = settings.enabled ? .on : .off
        demoItem?.state = loop?.demo == true ? .on : .off
        cantHearItem?.isHidden = tap?.cantHear != true
        for (item, value) in modeItems { item.state = settings.animation == value ? .on : .off }
        for (item, value) in colorItems { item.state = settings.colorMode == value ? .on : .off }
    }

    @objc private func toggleGlow() { model?.settings.enabled.toggle() }

    /// The shortcut and the login item follow their settings (on change, and once at start).
    private func applyShell() {
        guard let model else { return }
        let settings = model.settings
        if settings.toggleHotkey != appliedHotkey {
            appliedHotkey = settings.toggleHotkey
            model.hotkeyFailed = hotkey?.register(settings.toggleHotkey) == false
            if let equivalent = Shortcut(setting: settings.toggleHotkey)?.menuEquivalent {
                glowItem?.keyEquivalent = equivalent.key
                glowItem?.keyEquivalentModifierMask = equivalent.mask
            } else {
                glowItem?.keyEquivalent = ""
            }
        }
        if settings.launchAtStartup != appliedLogin {
            appliedLogin = settings.launchAtStartup
            LoginItem.apply(settings.launchAtStartup)
        }
        model.loginNeedsApproval = LoginItem.needsApproval
    }

    @objc private func chooseMode(_ sender: NSMenuItem) {
        if let value = sender.representedObject as? String { model?.settings.animation = value }
    }

    @objc private func chooseColors(_ sender: NSMenuItem) {
        if let value = sender.representedObject as? String { model?.settings.colorMode = value }
    }

    @objc private func choosePreset(_ sender: NSMenuItem) {
        guard let model, let preset = model.presets.first(where: { Int($0.index) == sender.tag }) else { return }
        model.apply(preset: preset)
    }

    @objc private func toggleDemo() {
        guard let loop else { return }
        loop.demo.toggle()
        overlays?.settingsChanged()
    }

    @objc private func showSettings() {
        settingsWindow?.show()
        media?.recheck() // Automation may have been allowed meanwhile
    }

    @objc private func openAudioPrivacy() { AudioPrivacy.open() }

    private func settingsChanged() {
        overlays?.settingsChanged()
        if let model { media?.setAlbumColorsWanted(model.usesAlbumColors) }
        watcher?.recheck() // "Pause in full-screen apps" applies at once
        applyShell()
        updateStatusItem()
    }

    /// The icon dims while the glow is off (doc 06 §2), and the tooltip names the track: the Windows tooltip's text.
    private func updateStatusItem() {
        statusItem?.button?.appearsDisabled = model?.settings.enabled == false
        let track = model?.nowPlaying
        statusItem?.button?.toolTip = Core.readString { buffer, capacity in
            withOptionalCString(track?.title) { title in
                withOptionalCString(track?.artist) { artist in
                    withOptionalCString(track?.app) { app in
                        rl_tooltip(title, artist, app, track?.isPlaying == true ? 1 : 0, buffer, capacity)
                    }
                }
            }
        } ?? Core.appName
    }

    /// An accessory app shows no menu bar of its own, but the Settings window's text fields still need the Edit
    /// shortcuts, and ⌘W should close it.
    private static func makeMainMenu() -> NSMenu {
        let main = NSMenu()
        let app = NSMenu()
        app.addItem(withTitle: "Quit \(Core.appName)", action: #selector(NSApplication.terminate(_:)),
                    keyEquivalent: "q")
        main.addItem(withTitle: Core.appName, action: nil, keyEquivalent: "").submenu = app
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        main.addItem(withTitle: "Edit", action: nil, keyEquivalent: "").submenu = edit
        let window = NSMenu(title: "Window")
        window.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        main.addItem(withTitle: "Window", action: nil, keyEquivalent: "").submenu = window
        return main
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
