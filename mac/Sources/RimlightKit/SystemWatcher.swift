import AppKit
import IOKit.ps
import os

/// The system events the glow follows (doc 02 "Lifetime events", docs/10-MACOS.md §5), on the main thread:
/// - sleep and wake, the displays sleeping, the screen locking and another user switching in: the glow pauses;
/// - on battery or in Low Power Mode: the `OnBattery` setting applies (Reduce: 30 fps at half scale; Pause);
/// - a full-screen window on a display: the glow there fades out ("Pause in fullscreen apps", doc 04 §4), checked when
///   another app comes to the front and every 2 s, which also notices the Dock moving when the glow leaves it room.
@MainActor
final class SystemWatcher {
    struct Callbacks {
        var paused: @MainActor (Bool) -> Void           // the glow should pause everywhere
        var onBattery: @MainActor (Bool) -> Void
        var fullscreen: @MainActor (Set<String>) -> Void // display UUIDs covered by a full-screen window
        var woke: @MainActor () -> Void
        var tick: @MainActor () -> Void                 // every 2 s while running
    }

    static let checkSeconds: TimeInterval = 2

    private let callbacks: Callbacks
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "system")
    private var observers: [(NotificationCenter, NSObjectProtocol)] = []
    private var powerSource: CFRunLoopSource?
    private var timer: Timer?
    private var reasons: Set<String> = []  // why the glow is paused: "sleep", "displays", "locked", "session"
    private var battery = false
    private let screens: @MainActor () -> [(id: String, frame: NSRect)]
    private let checksFullscreen: @MainActor () -> Bool

    /// `screens`: the overlaid displays with their frames; `checksFullscreen`: whether "Pause in fullscreen apps" is on
    /// and the glow could show.
    init(callbacks: Callbacks, screens: @escaping @MainActor () -> [(id: String, frame: NSRect)],
         checksFullscreen: @escaping @MainActor () -> Bool) {
        self.callbacks = callbacks
        self.screens = screens
        self.checksFullscreen = checksFullscreen
    }

    func start() {
        let workspace = NSWorkspace.shared.notificationCenter
        observe(workspace, NSWorkspace.willSleepNotification) { $0.set("sleep", true) }
        observe(workspace, NSWorkspace.didWakeNotification) {
            $0.set("sleep", false)
            $0.callbacks.woke()
        }
        observe(workspace, NSWorkspace.screensDidSleepNotification) { $0.set("displays", true) }
        observe(workspace, NSWorkspace.screensDidWakeNotification) { $0.set("displays", false) }
        observe(workspace, NSWorkspace.sessionDidResignActiveNotification) { $0.set("session", true) }
        observe(workspace, NSWorkspace.sessionDidBecomeActiveNotification) { $0.set("session", false) }
        observe(workspace, NSWorkspace.didActivateApplicationNotification) { $0.checkFullscreen() }
        // Locking is announced only by these undocumented distributed notifications (used by Chromium too).
        let distributed = DistributedNotificationCenter.default()
        observe(distributed, Notification.Name("com.apple.screenIsLocked")) { $0.set("locked", true) }
        observe(distributed, Notification.Name("com.apple.screenIsUnlocked")) { $0.set("locked", false) }
        observe(NotificationCenter.default, Notification.Name.NSProcessInfoPowerStateDidChange) { $0.checkPower() }

        // Battery: IOKit's power-source notification, on the main run loop.
        let context = Unmanaged.passUnretained(self).toOpaque()
        if let source = IOPSNotificationCreateRunLoopSource({ context in
            guard let context else { return }
            let watcher = Unmanaged<SystemWatcher>.fromOpaque(context).takeUnretainedValue()
            MainActor.assumeIsolated { watcher.checkPower() }
        }, context)?.takeRetainedValue() {
            CFRunLoopAddSource(CFRunLoopGetMain(), source, .defaultMode)
            powerSource = source
        } else {
            log.error("Can't follow the power source; the battery setting won't apply")
        }

        let timer = Timer(timeInterval: Self.checkSeconds, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.checkFullscreen()
                self?.callbacks.tick()
            }
        }
        timer.tolerance = 0.5 // wakes coalesce with the system's: the check costs almost nothing
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
        checkPower()
        checkFullscreen()
    }

    // MARK: - State

    private func observe(_ center: NotificationCenter, _ name: Notification.Name,
                         _ handle: @escaping @MainActor (SystemWatcher) -> Void) {
        let token = center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                if let self { handle(self) }
            }
        }
        observers.append((center, token))
    }

    private func set(_ reason: String, _ on: Bool) {
        let before = !reasons.isEmpty
        if on { reasons.insert(reason) } else { reasons.remove(reason) }
        if before != !reasons.isEmpty {
            log.info("Glow \(on ? "paused" : "resumed", privacy: .public): \(reason, privacy: .public)")
            callbacks.paused(!reasons.isEmpty)
        }
    }

    private func checkPower() {
        let value = Self.onBattery() || ProcessInfo.processInfo.isLowPowerModeEnabled
        guard value != battery else { return }
        battery = value
        callbacks.onBattery(value)
    }

    /// Whether the Mac runs on its battery (IOPSGetProvidingPowerSourceType).
    static func onBattery() -> Bool {
        guard let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let type = IOPSGetProvidingPowerSourceType(info)?.takeUnretainedValue() else { return false }
        return (type as String) == kIOPMBatteryPowerKey
    }

    private func checkFullscreen() {
        guard checksFullscreen() else { return callbacks.fullscreen([]) }
        callbacks.fullscreen(Self.coveredDisplays(screens()))
    }

    /// The displays a window of the frontmost app covers exactly (doc 04 §4: borderless games, full-screen video,
    /// and macOS full-screen Spaces). Window bounds and owners need no permission; only names would need Screen
    /// Recording (Apple DTS, forums 839069).
    static func coveredDisplays(_ screens: [(id: String, frame: NSRect)]) -> Set<String> {
        guard let front = NSWorkspace.shared.frontmostApplication, front.processIdentifier != getpid(),
              let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements],
                                                       kCGNullWindowID) as? [[String: Any]] else { return [] }
        let primaryHeight = NSScreen.screens.first?.frame.height ?? 0
        var covered: Set<String> = []
        for window in windows {
            guard (window[kCGWindowOwnerPID as String] as? pid_t) == front.processIdentifier,
                  (window[kCGWindowLayer as String] as? Int) == 0,
                  let boundsInfo = window[kCGWindowBounds as String] as? NSDictionary,
                  let bounds = CGRect(dictionaryRepresentation: boundsInfo as CFDictionary) else { continue }
            for screen in screens where bounds.equalTo(windowFrame(of: screen.frame, primaryHeight: primaryHeight)) {
                covered.insert(screen.id)
            }
        }
        return covered
    }

    /// A screen's frame in window-list coordinates: global, from the primary screen's top-left (NSScreen's are from
    /// its bottom-left).
    static func windowFrame(of screenFrame: NSRect, primaryHeight: CGFloat) -> CGRect {
        CGRect(x: screenFrame.minX, y: primaryHeight - screenFrame.maxY, width: screenFrame.width,
               height: screenFrame.height)
    }
}
