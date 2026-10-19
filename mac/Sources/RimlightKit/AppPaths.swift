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
}
