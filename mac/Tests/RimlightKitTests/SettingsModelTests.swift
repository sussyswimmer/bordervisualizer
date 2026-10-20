import CRimlight
import SwiftUI
import XCTest

@testable import RimlightKit

/// The Settings window's model pieces that need no window: the settings as Core sends them, colours, presets.
final class SettingsModelTests: XCTestCase {
    func testCoresSettingsDecodeAndEncodeWithTheFileNames() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).path
        let json = try XCTUnwrap(Core.loadSettings(directory: directory))
        let settings = try XCTUnwrap(AppSettings.decode(json))
        XCTAssertEqual(settings, AppSettings.fallback) // the Swift fallback is Core's defaults
        let encoded = try XCTUnwrap(settings.encoded())
        XCTAssertTrue(encoded.contains("\"coreThicknessDip\""))
        XCTAssertEqual(AppSettings.decode(encoded), settings)
    }

    func testColoursRoundTripAsSrgbHex() {
        for hex in ["#7C5CFF", "#22D3EE", "#000000", "#FFFFFF", "#FF8A2A"] {
            XCTAssertEqual(Color(hex: hex).hex, hex)
        }
        XCTAssertEqual(Color(hex: "nonsense").hex, Color.gray.hex)
    }

    func testPresetsApplyTheirLookAndKeepTheRest() throws {
        XCTAssertGreaterThan(rl_presets_count(), 0)
        var base = AppSettings.fallback
        base.fpsCap = 30
        let json = try XCTUnwrap(base.encoded())
        let applied = try XCTUnwrap(json.withCString { source in
            Core.readString { rl_preset_apply(0, source, $0, $1) }
        }.flatMap(AppSettings.decode))
        XCTAssertNotEqual(applied.primaryHex, base.primaryHex)
        XCTAssertTrue(applied.overrideAlbumColor)
        XCTAssertEqual(applied.fpsCap, 30) // a preset changes the look only
    }
}
