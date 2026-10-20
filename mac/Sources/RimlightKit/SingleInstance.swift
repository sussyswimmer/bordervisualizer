import AppKit
import Darwin
import os

/// One copy at a time (doc 06 §4): an exclusive lock on a file in Application Support, held while the app runs. A
/// second launch tells the running copy what it was asked for (show Settings, or start the demo) and quits.
enum SingleInstance {
    static var showSettingsName: Notification.Name { Notification.Name(bundleID + ".show-settings") }
    static var demoName: Notification.Name { Notification.Name(bundleID + ".demo") }
    private static var bundleID: String { Bundle.main.bundleIdentifier ?? Core.appName }
    nonisolated(unsafe) private static var lockFile: Int32 = -1

    /// True when this is the only copy (the lock is now held for the app's life).
    static func claim() -> Bool {
        let directory = AppPaths.supportDirectory
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let path = directory.appendingPathComponent("instance.lock").path
        let file = open(path, O_RDWR | O_CREAT | O_CLOEXEC, 0o600)
        guard file >= 0 else { // can't tell: run rather than refuse
            Logger(subsystem: bundleID, category: "instance").error("The instance lock couldn't be opened (errno \(errno))")
            return true
        }
        if flock(file, LOCK_EX | LOCK_NB) != 0 {
            close(file)
            return false
        }
        lockFile = file
        return true
    }

    /// The second copy's message to the first.
    static func notifyRunningCopy(demo: Bool) {
        DistributedNotificationCenter.default().postNotificationName(
            demo ? demoName : showSettingsName, object: nil, userInfo: nil, deliverImmediately: true)
    }
}
