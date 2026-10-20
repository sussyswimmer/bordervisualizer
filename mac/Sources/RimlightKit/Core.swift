import CRimlight
import Foundation

/// The shared Core, through libRimlightCore's C ABI (rimlight_core.h).
public enum Core {
    /// Whether the bundled library speaks the ABI this app was built against.
    public static var abiMatches: Bool { rl_abi_version() == rl_header_abi_version() }

    /// The app's display name, from Core's AppInfo: the Swift code never spells it.
    public static let appName: String = readString { rl_app_name($0, $1) } ?? ProcessInfo.processInfo.processName

    /// The app's version, from src/Directory.Build.props.
    public static let version: String = readString { rl_version($0, $1) } ?? "0.0.0"

    /// The settings in `directory`'s settings.json, validated by Core, as JSON with the file's field names; the
    /// defaults when there is no file. Nil only if the library fails.
    public static func loadSettings(directory: String) -> String? {
        directory.withCString { dir in readString { rl_settings_load(dir, $0, $1) } }
    }

    /// Validates `json` and writes it to `directory`'s settings.json. Returns RL_OK or a negative status.
    public static func saveSettings(directory: String, json: String) -> Int32 {
        directory.withCString { dir in json.withCString { rl_settings_save(dir, $0) } }
    }

    /// Calls a string-returning function with a buffer, and once more with a larger one if the string didn't fit.
    static func readString(_ call: (UnsafeMutablePointer<CChar>?, Int32) -> Int32) -> String? {
        var capacity: Int32 = 1024
        for _ in 0..<3 {
            var buffer = [CChar](repeating: 0, count: Int(capacity))
            let length = buffer.withUnsafeMutableBufferPointer { call($0.baseAddress, capacity) }
            if length < 0 || length == Int32.max { return nil }
            if length < capacity {
                return String(decoding: buffer[..<Int(length)].map { UInt8(bitPattern: $0) }, as: UTF8.self)
            }
            capacity = length + 1
        }
        return nil
    }
}

/// Runs `body` with a C string for `text`, or NULL for nil.
func withOptionalCString<Result>(_ text: String?, _ body: (UnsafePointer<CChar>?) -> Result) -> Result {
    guard let text else { return body(nil) }
    return text.withCString { body($0) }
}
