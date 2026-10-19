import AppKit
import Sparkle

/// Updates through Sparkle 2 (doc 06 §5, docs/10-MACOS.md §6): the appcast is a release asset, each update is
/// EdDSA-signed by the release workflow, and the check is the app's only network request. With AutoUpdate on, it
/// checks every 12 hours and downloads in the background, and the update installs when the app quits; with it off,
/// only "Check for Updates…" checks. Main thread.
@MainActor
final class Updater {
    private let controller: SPUStandardUpdaterController?

    /// Nil when the build has no update key (a local or pull-request build): such a copy never checks.
    init() {
        guard Self.isConfigured(Bundle.main.infoDictionary ?? [:]) else {
            controller = nil
            return
        }
        controller = SPUStandardUpdaterController(startingUpdater: false, updaterDelegate: nil,
                                                  userDriverDelegate: nil)
    }

    /// Whether this copy can update itself: "Check for Updates…" is shown only then.
    var isAvailable: Bool { controller != nil }

    /// Follows the AutoUpdate setting, and starts the updater the first time.
    func apply(autoUpdate: Bool) {
        guard let controller else { return }
        let updater = controller.updater
        updater.automaticallyChecksForUpdates = autoUpdate
        updater.automaticallyDownloadsUpdates = autoUpdate
        updater.sendsSystemProfile = false
        if !started {
            started = true
            controller.startUpdater()
        }
    }

    /// The menu's and the About page's "Check for Updates…": works with AutoUpdate off too (doc 06 §5).
    func checkNow() {
        controller?.checkForUpdates(nil)
    }

    private var started = false

    /// An update needs both a feed and the key that checks its signatures.
    nonisolated static func isConfigured(_ info: [String: Any]) -> Bool {
        let feed = (info["SUFeedURL"] as? String) ?? ""
        let key = (info["SUPublicEDKey"] as? String) ?? ""
        return feed.hasPrefix("https://") && !key.isEmpty
    }
}
