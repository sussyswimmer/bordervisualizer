import XCTest

@testable import RimlightKit

/// A copy updates itself only with both a feed and the key that checks the updates' signatures.
final class UpdaterTests: XCTestCase {
    private let feed = "https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/appcast.xml"
    private let key = "pfIShU4dEXqPd5ObYNfDBiQWcXozk7estwzTnF9BamQ="

    func testAReleaseBuildCanUpdate() {
        XCTAssertTrue(Updater.isConfigured(["SUFeedURL": feed, "SUPublicEDKey": key]))
    }

    func testABuildWithoutTheKeyNeverChecks() {
        XCTAssertFalse(Updater.isConfigured(["SUFeedURL": feed, "SUPublicEDKey": ""]))
        XCTAssertFalse(Updater.isConfigured(["SUFeedURL": feed]))
        XCTAssertFalse(Updater.isConfigured([:]))
    }

    func testTheFeedMustBeHttps() {
        XCTAssertFalse(Updater.isConfigured(["SUFeedURL": "http://example.com/appcast.xml", "SUPublicEDKey": key]))
    }
}
