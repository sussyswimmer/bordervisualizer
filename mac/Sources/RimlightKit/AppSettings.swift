import Foundation

/// The settings as Core's C ABI carries them: the settings file's fields and names (CORE-API §7.1). Core validates
/// them when they are saved; the app reads and edits them through this.
struct AppSettings: Codable, Equatable {
    var version: Int
    var enabled: Bool
    var animation: String          // MusicSync | IdleGlow | Off
    var whenSilent: String         // IdleGlow | Hide
    var movement: String           // Flow | Pulse: light travelling round the edge, or the whole edge pulsing
    var colorMode: String          // AlbumArt | Manual
    var overrideAlbumColor: Bool
    var primaryHex: String
    var secondaryHex: String
    var primaryRatio: Double
    var coreThicknessDip: Double   // points on the Mac (10-MACOS §3.3)
    var glow: Double
    var brightness: Double
    var sensitivity: Double
    var cornerRadiusDip: Double
    var edgeStyle: String          // Glow | Line: the soft glow, or a bright line along the edge with a glow inside
    var coverTaskbar: Bool         // the Mac's "Cover the menu bar and Dock"
    var monitors: String           // All | PrimaryOnly | Custom
    var customMonitorIds: [String] // display UUIDs on the Mac
    var fpsCap: Int
    var pauseInFullscreen: Bool
    var onBattery: String          // Normal | Reduce | Pause
    var hideFromScreenCapture: Bool
    var launchAtStartup: Bool
    var toggleHotkey: String       // "Win" is Command on the Mac
    var autoUpdate: Bool
    var firstRunComplete: Bool

    static func decode(_ json: String) -> AppSettings? {
        try? JSONDecoder().decode(AppSettings.self, from: Data(json.utf8))
    }

    func encoded() -> String? {
        guard let data = try? JSONEncoder().encode(self) else { return nil }
        return String(decoding: data, as: UTF8.self)
    }
}
