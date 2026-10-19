import ServiceManagement
import os

/// Launch at login (doc 06 §4) through SMAppService (macOS 13): the app itself is the login item. Main thread.
enum LoginItem {
    private static let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "login")

    /// Registers or unregisters to match the setting. Done at every start too, so an update (a new ad-hoc signature)
    /// keeps the login item pointing at this copy.
    @MainActor
    static func apply(_ enabled: Bool) {
        let service = SMAppService.mainApp
        do {
            if enabled {
                try service.register()
            } else if service.status == .enabled || service.status == .requiresApproval {
                try service.unregister()
            }
        } catch {
            log.error("Launch at login couldn't be \(enabled ? "set" : "removed", privacy: .public): \(error.localizedDescription, privacy: .public)")
        }
    }

    /// True when macOS wants the user to allow the login item in System Settings › General › Login Items.
    @MainActor
    static var needsApproval: Bool { SMAppService.mainApp.status == .requiresApproval }

    @MainActor
    static func openSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
