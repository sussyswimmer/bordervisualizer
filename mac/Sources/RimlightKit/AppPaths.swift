import AppKit
import Foundation

/// Where the app keeps its files: ~/Library/Application Support/<name> for settings.json (the Windows app's
/// %APPDATA%\<name>, doc 06 §1) and ~/Library/Logs/<name> for logs.
public enum AppPaths {
    public static var supportDirectory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent(Core.appName, isDirectory: true)
    }

    public static var settingsFile: URL {
        supportDirectory.appendingPathComponent("settings.json", isDirectory: false)
    }

    /// Writes settings.json if it isn't there yet (Core's defaults, in the same format as Windows) and shows it in
    /// Finder.
    @MainActor
    static func revealSettingsFile() {
        let directory = supportDirectory
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        if !FileManager.default.fileExists(atPath: settingsFile.path),
           let json = Core.loadSettings(directory: directory.path) {
            _ = Core.saveSettings(directory: directory.path, json: json)
        }
        if FileManager.default.fileExists(atPath: settingsFile.path) {
            NSWorkspace.shared.activateFileViewerSelecting([settingsFile])
        } else {
            NSWorkspace.shared.open(directory)
        }
    }
}
