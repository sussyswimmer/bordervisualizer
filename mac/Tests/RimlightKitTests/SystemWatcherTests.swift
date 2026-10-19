import XCTest

@testable import RimlightKit

/// The full-screen check's geometry: NSScreen frames (from the bottom-left) against window bounds (from the top-left).
final class SystemWatcherTests: XCTestCase {
    func testScreenFramesBecomeWindowListCoordinates() {
        // A 1512 x 982 laptop with a 2560 x 1440 display above it, left-aligned.
        let primary = NSRect(x: 0, y: 0, width: 1512, height: 982)
        let above = NSRect(x: 0, y: 982, width: 2560, height: 1440)
        XCTAssertEqual(SystemWatcher.windowFrame(of: primary, primaryHeight: 982), CGRect(x: 0, y: 0, width: 1512, height: 982))
        XCTAssertEqual(SystemWatcher.windowFrame(of: above, primaryHeight: 982),
                       CGRect(x: 0, y: -1440, width: 2560, height: 1440))
        // To the right of it instead, top-aligned.
        let right = NSRect(x: 1512, y: -458, width: 2560, height: 1440)
        XCTAssertEqual(SystemWatcher.windowFrame(of: right, primaryHeight: 982),
                       CGRect(x: 1512, y: 0, width: 2560, height: 1440))
    }

    func testAFullScreenWindowBelowTheNotchCoversTheDisplay() {
        let frame = CGRect(x: 0, y: 0, width: 1512, height: 982)
        XCTAssertTrue(SystemWatcher.covers(frame, frame, topInset: 0))
        XCTAssertTrue(SystemWatcher.covers(CGRect(x: 0, y: 32, width: 1512, height: 950), frame, topInset: 32))
        XCTAssertFalse(SystemWatcher.covers(CGRect(x: 0, y: 32, width: 1512, height: 950), frame, topInset: 0))
        XCTAssertFalse(SystemWatcher.covers(CGRect(x: 0, y: 25, width: 1512, height: 900), frame, topInset: 32))
    }
}
