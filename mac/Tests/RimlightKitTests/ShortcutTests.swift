import AppKit
import Carbon.HIToolbox
import XCTest

@testable import RimlightKit

/// The global shortcut: the Windows setting format, the Mac's keys and rules.
final class ShortcutTests: XCTestCase {
    func testTheDefaultIsControlOptionL() throws {
        let shortcut = try XCTUnwrap(Shortcut(setting: "Ctrl+Alt+L"))
        XCTAssertEqual(shortcut.display, "⌃⌥L")
        XCTAssertEqual(shortcut.carbonKeyCode, kVK_ANSI_L)
        XCTAssertEqual(shortcut.carbonModifiers, UInt32(controlKey | optionKey))
        XCTAssertTrue(shortcut.macAllows)
        XCTAssertEqual(shortcut.menuEquivalent?.key, "l")
    }

    func testAPressedKeyBecomesTheSetting() throws {
        let shortcut = try XCTUnwrap(Shortcut(keyCode: UInt16(kVK_ANSI_K), flags: [.command, .shift]))
        XCTAssertEqual(shortcut.setting, "Shift+Win+K") // "Win" is Command in the shared file format
        XCTAssertEqual(shortcut.display, "⇧⌘K")
        XCTAssertNotNil(Shortcut(keyCode: UInt16(kVK_F5), flags: []))  // a function key alone is fine
    }

    func testOptionOnlyShortcutsAreRefusedOnTheMac() {
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_ANSI_L), flags: [.option]))          // macOS 15 refuses these
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_ANSI_L), flags: [.option, .shift]))
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_ANSI_L), flags: []))                // a plain letter: never
        XCTAssertEqual(Shortcut(setting: "Alt+L")?.macAllows, false) // valid on Windows, not registered here
        XCTAssertNil(Shortcut(setting: ""))
    }

    func testEveryNamedKeyMapsBothWays() {
        for vk in [0x41, 0x5A, 0x30, 0x39, 0x70, 0x83, 0x20, 0x25, 0xBF] {
            let carbon = KeyCodes.carbon(forVirtualKey: vk)
            XCTAssertNotNil(carbon, "VK \(vk)")
            XCTAssertEqual(carbon.flatMap(KeyCodes.virtualKey(forCarbon:)), vk)
        }
    }

    func testMacOSKeepsItsOwnCommandShortcuts() {
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_ANSI_Q), flags: [.command]))      // ⌘Q would quit nothing else
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_ANSI_W), flags: [.command]))
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_Tab), flags: [.command, .shift]))
        XCTAssertNil(Shortcut(keyCode: UInt16(kVK_Space), flags: [.command]))
        XCTAssertNotNil(Shortcut(keyCode: UInt16(kVK_ANSI_Q), flags: [.command, .control])) // ⌃⌘Q is the user's
    }

    func testWindowsOnlyKeysHaveMacPlaces() {
        XCTAssertEqual(Shortcut(setting: "Ctrl+Insert")?.carbonKeyCode, kVK_Help)
        XCTAssertEqual(Shortcut(setting: "Ctrl+Oem102")?.carbonKeyCode, kVK_ISO_Section)
        XCTAssertNil(Shortcut(setting: "Ctrl+Pause")?.carbonKeyCode) // no Mac key: Settings says so
    }
}
