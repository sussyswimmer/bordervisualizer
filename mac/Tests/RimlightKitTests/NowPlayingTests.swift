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
        XCTAssertEqual(Color(linear: RlRgb(r: 0.2158605, g: 0.2158605, b: 0.2158605)).hex, "#808080") // mid grey
    }

    /// Spotify's cover address comes from another app, so only Spotify's own image hosts over HTTPS are fetched.
    func testOnlySpotifyImageHostsAreFetched() {
        func allowed(_ text: String) -> Bool { URL(string: text).map(NowPlayingService.isSpotifyImage) ?? false }
        XCTAssertTrue(allowed("https://i.scdn.co/image/ab67616d0000b273abcdef"))
        XCTAssertTrue(allowed("https://image-cdn-ak.spotifycdn.com/image/ab67616d0000b273abcdef"))
        XCTAssertFalse(allowed("http://i.scdn.co/image/ab67616d0000b273abcdef"))        // not HTTPS
        XCTAssertFalse(allowed("https://example.com/image.jpg"))
        XCTAssertFalse(allowed("https://i.scdn.co.example.com/image"))                 // look-alike host
        XCTAssertFalse(allowed("https://user@i.scdn.co/image"))
        XCTAssertFalse(allowed("file:///etc/passwd"))
    }
}
