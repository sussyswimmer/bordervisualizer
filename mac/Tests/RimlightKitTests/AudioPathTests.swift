import CRimlight
import XCTest

@testable import RimlightKit

/// The audio path's pieces that run without a permission prompt: the C ring and its mono mix, and a tap that is made
/// but never opened (opening it would ask for "System Audio Recording").
final class AudioPathTests: XCTestCase {
    func testTheRingKeepsTheNewestSamplesAndMixesToMono() throws {
        let ring = try XCTUnwrap(rlc_ring_create(8))
        defer { rlc_ring_destroy(ring) }
        let input: [Float] = (0..<12).map(Float.init)
        XCTAssertEqual(rlc_ring_write(ring, input, 12), 8) // full: 4 dropped
        XCTAssertEqual(rlc_ring_dropped(ring), 4)
        var output = [Float](repeating: -1, count: 3)
        XCTAssertEqual(rlc_ring_read(ring, &output, 3), 3)
        XCTAssertEqual(output, [5, 6, 7]) // the newest three

        let interleaved: [Float] = [1, 3, 3, 5, 5, 7]
        let written = interleaved.withUnsafeBufferPointer { samples -> Int32 in
            var planes: [UnsafePointer<Float>?] = [samples.baseAddress]
            return rlc_ring_write_mono(ring, &planes, 1, 2, 3)
        }
        XCTAssertEqual(written, 3)
        XCTAssertEqual(rlc_ring_read(ring, &output, 3), 3)
        XCTAssertEqual(output, [2, 4, 6])
    }

    func testANewTapIsClosed() throws {
        let tap = try XCTUnwrap(SystemAudioTap())
        XCTAssertEqual(tap.sampleRate, 0)
        XCTAssertEqual(tap.generation, 0)
        XCTAssertFalse(tap.cantHear)
        XCTAssertEqual(rlc_ring_capacity(tap.ring), SystemAudioTap.ringCapacity)
    }
}
