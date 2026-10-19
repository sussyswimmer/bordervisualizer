import CRimlight
import ImageIO
import SwiftUI
import UniformTypeIdentifiers
import XCTest

@testable import RimlightKit

/// The media service's pieces that need no player: decoding a cover to Core's BGRA input, and showing linear colours.
final class NowPlayingTests: XCTestCase {
    func testACoverDecodesTo64PixelBgra() throws {
        // A 300 x 200 PNG, solid red.
        let width = 300, height = 200
        let space = try XCTUnwrap(CGColorSpace(name: CGColorSpace.sRGB))
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                              bytesPerRow: 0, space: space,
                                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(srgbRed: 1, green: 0, blue: 0, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        let image = try XCTUnwrap(context.makeImage())
        let png = NSMutableData()
        let destination = try XCTUnwrap(CGImageDestinationCreateWithData(png, UTType.png.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(destination, image, nil)
        XCTAssertTrue(CGImageDestinationFinalize(destination))

        let (pixels, w, h) = try XCTUnwrap(NowPlayingService.decode(png as Data))
        XCTAssertEqual(max(w, h), 64)
        XCTAssertEqual(pixels.count, w * h * 4)
        XCTAssertEqual(Array(pixels[0..<4]), [0, 0, 255, 255]) // B, G, R, A
    }

    func testLinearColoursShowAsSrgb() {
        XCTAssertEqual(Color(linear: RlRgb(r: 1, g: 0, b: 0)).hex, "#FF0000")
        XCTAssertEqual(Color(linear: RlRgb(r: 0.214041, g: 0.214041, b: 0.214041)).hex, "#808080") // mid grey
    }
}
