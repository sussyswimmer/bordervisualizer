import CRimlight
import Foundation
import XCTest

@testable import RimlightKit

/// The C ABI against the real, NativeAOT-published libRimlightCore (the C# tests cover the JIT build).
final class CoreBridgeTests: XCTestCase {
    private var directory: URL!

    override func setUpWithError() throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: directory)
    }

    func testTheLibrarySpeaksTheHeadersAbi() {
        XCTAssertEqual(rl_abi_version(), RL_ABI_VERSION)
        XCTAssertTrue(Core.abiMatches)
    }

    func testNameAndVersionComeFromCore() {
        XCTAssertFalse(Core.appName.isEmpty)
        XCTAssertNotNil(Core.version.range(of: #"^\d+\.\d+\.\d+"#, options: .regularExpression), Core.version)
    }

    func testAShortBufferIsAskedForAgain() {
        var calls: [Int32] = []
        let text = Core.readString { buffer, capacity in
            calls.append(capacity)
            return capacity < 2000 ? 1999 : rl_app_name(buffer, capacity)
        }
        XCTAssertEqual(calls, [1024, 2000])
        XCTAssertEqual(text, Core.appName)
    }

    func testSettingsDefaultWhenThereIsNoFile() throws {
        let settings = try load()
        XCTAssertEqual(settings["toggleHotkey"] as? String, "Ctrl+Alt+L")
        XCTAssertEqual(settings["animation"] as? String, "MusicSync")
        XCTAssertFalse(FileManager.default.fileExists(atPath: directory.path))
    }

    func testSettingsRoundTripThroughCoresStore() throws {
        var settings = try load()
        settings["glow"] = 0.7
        settings["brightness"] = 5 // out of range: Core clamps it to 1
        settings["customMonitorIds"] = ["37D8832A-2D66-02CA-B9F7-8F30A301B230"]
        let json = String(decoding: try JSONSerialization.data(withJSONObject: settings), as: UTF8.self)
        XCTAssertEqual(Core.saveSettings(directory: directory.path, json: json), RL_OK)

        let file = directory.appendingPathComponent("settings.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        let reloaded = try load()
        XCTAssertEqual(try XCTUnwrap(reloaded["glow"] as? Double), 0.7, accuracy: 1e-6)
        XCTAssertEqual(reloaded["brightness"] as? Double, 1)
        XCTAssertEqual(reloaded["customMonitorIds"] as? [String], ["37D8832A-2D66-02CA-B9F7-8F30A301B230"])
    }

    func testBadJsonIsAStatus() {
        XCTAssertEqual(Core.saveSettings(directory: directory.path, json: "not json"), RL_BAD_JSON)
        XCTAssertEqual(rl_settings_save(nil, nil), RL_INVALID_ARGUMENT)
    }

    private func load() throws -> [String: Any] {
        let json = try XCTUnwrap(Core.loadSettings(directory: directory.path))
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
    }
}
