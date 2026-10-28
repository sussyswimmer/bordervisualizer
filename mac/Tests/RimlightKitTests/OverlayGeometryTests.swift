import XCTest

@testable import RimlightKit

/// The camera notch an overlay goes round (Maxwell's request: "detect if theres a notch and go around it for the
/// lighting"), from NSScreen's safe area and the menu bar's areas either side of the notch, as plain numbers.
final class OverlayGeometryTests: XCTestCase {
    // A 14-inch MacBook Pro at its default 1512 x 982 points: the menu bar's areas either side of the notch are 659
    // points wide, and the notch is 32 points tall.
    private let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)

    func testANotchedScreenHasItsNotchInTheOverlaysPoints() {
        let notch = OverlayController.notch(screenFrame: screen, panelFrame: screen, topInset: 32, leftAreaWidth: 659,
                                            rightAreaWidth: 659)
        XCTAssertEqual(notch, CGRect(x: 659, y: 0, width: 194, height: 32)) // 659...853 across, 32 down
    }

    func testTheNotchIsMeasuredFromTheOverlaysTopLeft() {
        // The same laptop to the right of a larger display, 400 points lower: the notch stays where it is on the
        // screen, whatever the screen's place in the desktop.
        let placed = screen.offsetBy(dx: 2560, dy: -400)
        XCTAssertEqual(OverlayController.notch(screenFrame: placed, panelFrame: placed, topInset: 32,
                                               leftAreaWidth: 659, rightAreaWidth: 659),
                       CGRect(x: 659, y: 0, width: 194, height: 32))
        // An overlay that starts 70 points right of the screen's left edge (the Dock on the left) and 10 points
        // below its top: the notch moves left by 70 and loses the 10 points above the overlay.
        let panel = CGRect(x: 70, y: 0, width: 1442, height: 972)
        XCTAssertEqual(OverlayController.notch(screenFrame: screen, panelFrame: panel, topInset: 32, leftAreaWidth: 659,
                                               rightAreaWidth: 659),
                       CGRect(x: 589, y: 0, width: 194, height: 22))
    }

    func testAnOverlayBelowTheMenuBarHasNoNotch() {
        // "Cover the menu bar and Dock" off: the overlay is the visible frame, below the menu bar, which is at least
        // as tall as the notch.
        for menuBar in [32.0, 37.0] as [CGFloat] {
            let visible = CGRect(x: 0, y: 70, width: 1512, height: 982 - 70 - menuBar)
            XCTAssertNil(OverlayController.notch(screenFrame: screen, panelFrame: visible, topInset: 32,
                                                 leftAreaWidth: 659, rightAreaWidth: 659), "menu bar \(menuBar)")
        }
        // A menu bar shorter than the notch would leave its lower part over the overlay.
        let lower = CGRect(x: 0, y: 0, width: 1512, height: 958)
        XCTAssertEqual(OverlayController.notch(screenFrame: screen, panelFrame: lower, topInset: 32, leftAreaWidth: 659,
                                               rightAreaWidth: 659),
                       CGRect(x: 659, y: 0, width: 194, height: 8))
    }

    func testAScreenWithoutANotchHasNone() {
        // An external display: no safe area at the top and no areas beside a notch.
        let external = CGRect(x: 1512, y: 0, width: 2560, height: 1440)
        XCTAssertNil(OverlayController.notch(screenFrame: external, panelFrame: external, topInset: 0,
                                             leftAreaWidth: nil, rightAreaWidth: nil))
        XCTAssertNil(OverlayController.notch(screenFrame: external, panelFrame: external, topInset: 0,
                                             leftAreaWidth: 1000, rightAreaWidth: 1000))
        // A safe area without both areas, or areas that leave no gap between them, is no notch either.
        XCTAssertNil(OverlayController.notch(screenFrame: screen, panelFrame: screen, topInset: 32, leftAreaWidth: 659,
                                             rightAreaWidth: nil))
        XCTAssertNil(OverlayController.notch(screenFrame: screen, panelFrame: screen, topInset: 32, leftAreaWidth: 756,
                                             rightAreaWidth: 756))
    }
}
